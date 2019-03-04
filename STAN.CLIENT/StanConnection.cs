// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Collections.Generic;
using System.Threading;
using NATS.Client;
using System.Threading.Tasks;

/*! \mainpage %NATS .NET Streaming Client.
 *
 * \section intro_sec Introduction
 *
 * The %NATS .NET Streaming Client is part of %NATS an open-source, cloud-native
 * messaging system.
 * This client, written in C#, follows the go client closely, but
 * diverges in places to follow the common design semantics of a .NET API.
 *
 * \section install_sec Installation
 *
 * Instructions to build and install the %NATS .NET C# Streaming Client can be
 * found at the [NATS .NET C# Streaming Client GitHub page](https://github.com/nats-io/csharp-nats-streaming)
 *
 * \section other_doc_section Other Documentation
 *
 * This documentation focuses on the %NATS .NET C# Streaming Client API; for additional
 * information, refer to the following:
 *
 * - [General Documentation for nats.io](http://nats.io/documentation)
 * - [NATS .NET C# Streaming Client found on GitHub](https://github.com/nats-io/csharp-nats-streaming)
 * - [NATS .NET C# Client found on GitHub](https://github.com/nats-io/csnats)
 * - [The NATS server (gnatsd) found on GitHub](https://github.com/nats-io/gnatsd)
 */

// disable XML comment warnings
#pragma warning disable 1591

namespace STAN.Client
{
    internal class PublishAck
    {
        string guidValue;

        private Timer ackTimer;
        private TimeSpan timeout;
        private volatile bool isComplete;
        private Connection connection;
        private EventHandler<StanAckHandlerArgs> ah;
        private Exception ex = null;

        internal PublishAck(Connection conn, string guid, EventHandler<StanAckHandlerArgs> handler, long timeout)
        {
            connection = conn;

            ah = handler;
            guidValue = guid;
            ackTimer = new Timer(ackTimerCb, null, Timeout.Infinite, Timeout.Infinite);
            this.timeout = TimeSpan.FromMilliseconds(timeout);
        }

        internal string GUID
        {
            get { return guidValue; }
        }

        private void ackTimerCb(object state)
        {
            connection.handleAck(GUID, "Timeout occurred.", true);
        }

        internal void wait()
        {
            SpinWait.SpinUntil(() => isComplete);

            if (ex != null)
                throw ex;
        }

        internal void complete(string error, bool dataPublished)
        {
            if (!isComplete)
            {
                ackTimer.Dispose();

                if (dataPublished)
                {
                    error = error?.Trim();

                    if (ah != null)
                    {
                        try
                        {
                            ah(this, new StanAckHandlerArgs(GUID, error));
                        }
                        catch { /* ignore user exceptions */ }
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        ex = new StanException(error);
                    }
                }

                isComplete = true;
            }
        }

        internal void StartTimeoutMonitor()
        {
            try
            {
                ackTimer.Change(timeout, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // There is no need to handle or even log this exception.
                // The only reason for this to happen is if we received
                // an ACK before being able to start the timeout timer.
                // At this point Complete(string error, bool dataPublished) 
                // has been called and the timer disposed. There is no need 
                // to start the timer to check for a timeout.
            }
        }
    }

    public class Connection : IStanConnection, IDisposable
    {
        private readonly Object mu = new object();

        private volatile bool _disposed;

        private readonly string clientID;
        private readonly string pubPrefix; // Publish prefix set by stan, append our subject.
        private readonly string subRequests; // Subject to send subscription requests.
        private readonly string unsubRequests; // Subject to send unsubscribe requests.
        private readonly string subCloseRequests; // Subject to send subscrption close requests.
        private readonly string closeRequests; // Subject to send close requests.
        private readonly string ackSubject; // publish acks

        private ISubscription ackSubscription;
        private ISubscription hbSubscription;

        private Dictionary<string, AsyncSubscription> subMap;
        private Dictionary<string, PublishAck> pubAckMap;

        internal ProtocolSerializer ps = new ProtocolSerializer();

        private StanOptions opts = null;

        private IConnection nc;
        private bool ncOwned = false;

        private Connection() { }

        internal Connection(string stanClusterID, string clientID, StanOptions options)
        {
            this.clientID = clientID;

            if (options != null)
                opts = new StanOptions(options);
            else
                opts = new StanOptions();

            if (opts.natsConn == null)
            {
                ncOwned = true;
                try
                {
                    nc = new ConnectionFactory().CreateConnection(opts.NatsURL);
                }
                catch (Exception ex)
                {
                    throw new StanConnectionException(ex);
                }
            }
            else
            {
                nc = opts.natsConn;
                ncOwned = false;
            }

            // create a heartbeat inbox
            string hbInbox = newInbox();
            hbSubscription = nc.SubscribeAsync(hbInbox, processHeartBeat);

            string discoverSubject = opts.discoverPrefix + "." + stanClusterID;

            ConnectRequest req = new ConnectRequest();
            req.ClientID = this.clientID;
            req.HeartbeatInbox = hbInbox;

            Msg cr;
            try
            {
                cr = nc.Request(discoverSubject,
                    ProtocolSerializer.marshal(req),
                    opts.ConnectTimeout);
            }
            catch (NATSTimeoutException)
            {
                throw new StanConnectRequestTimeoutException();
            }

            ConnectResponse response = new ConnectResponse();
            try
            {
                ProtocolSerializer.unmarshal(cr.Data, response);
            }
            catch (Exception e)
            {
                throw new StanConnectRequestException(e);
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                throw new StanConnectRequestException(response.Error);
            }

            // capture cluster configuration endpoints to publish and subscribe/unsubscribe
            pubPrefix = response.PubPrefix;
            subRequests = response.SubRequests;
            unsubRequests = response.UnsubRequests;
            subCloseRequests = response.SubCloseRequests;
            closeRequests = response.CloseRequests;

            // setup the Ack subscription
            ackSubject = StanConsts.DefaultACKPrefix + "." + newGUID();
            ackSubscription = nc.SubscribeAsync(ackSubject, processAck);

            // TODO:  hardcode or options?
            ackSubscription.SetPendingLimits(1024 * 1024, 32 * 1024 * 1024);

            subMap = new Dictionary<string, AsyncSubscription>();
            pubAckMap = new Dictionary<string, PublishAck>();
        }

        private void processHeartBeat(object sender, MsgHandlerEventArgs args)
        {
            nc.Publish(args.Message.Reply, null);
        }

        internal PublishAck removeAck(string guid)
        {
            PublishAck a;

            lock (mu)
            {
                if (pubAckMap.TryGetValue(guid, out a))
                {
                    pubAckMap.Remove(guid);
                    Monitor.Pulse(mu);
                }
            }

            return a;
        }

        public IConnection NATSConnection
        {
            get
            {
                return nc;
            }
        }

        private void processAck(object sender, MsgHandlerEventArgs args)
        {
            PubAck pa = new PubAck();

            try
            {
                ProtocolSerializer.unmarshal(args.Message.Data, pa);
            }
            catch (Exception)
            {
                // TODO:  (cls) handle this...
                return;
            }

            handleAck(pa.Guid, pa.Error, true);
        }

        internal void handleAck(string guid, string error, bool dataPublished)
        {
            removeAck(guid)?.complete(error, dataPublished);
        }

        static public string newGUID()
        {
            return NUID.NextGlobal;
        }

        public void Publish(string subject, byte[] data)
        {
            publish(subject, data, null).wait();
        }

        public string Publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler)
        {
            return publish(subject, data, handler).GUID;
        }

        internal PublishAck publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler)
        {
            string subj = this.pubPrefix + "." + subject;
            string guidValue = newGUID();
            byte[] b = ProtocolSerializer.createPubMsg(clientID, guidValue, subject, data);

            PublishAck a = new PublishAck(this, guidValue, handler, opts.PubAckWait);

            lock (mu)
            {
                while (pubAckMap.Count >= opts.maxPubAcksInflight)
                {
                    Monitor.Wait(mu);
                }
                pubAckMap[a.GUID] = a;
            }

            try
            {
                nc.Publish(subj, ackSubject, b);
            }
            catch
            {
                handleAck(guidValue, null, false);
                throw;
            }

            a.StartTimeoutMonitor();

            return a;
        }

        public Task<string> PublishAsync(string subject, byte[] data)
        {
            PublishAck a = publish(subject, data, null);
            Task<string> t = new Task<string>(() =>
            {
                a.wait();
                return a.GUID;
            });
            t.Start();
            return t;
        }

        private IStanSubscription subscribe(string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler, StanSubscriptionOptions options)
        {
            AsyncSubscription sub = new AsyncSubscription(this, options);

            sub.subscribe(subRequests, subject, qgroup, handler);

            lock (mu)
            {
                // Register the subscription
                subMap[sub.Inbox] = sub;
            }

            return sub;
        }

        internal void unsubscribe(string subject, string inbox, string ackInbox, bool close)
        {
            lock (mu)
            {
                subMap.Remove(inbox);
            }

            string requestSubject = unsubRequests;
            if (close)
            {
                requestSubject = subCloseRequests;
                if (string.IsNullOrEmpty(requestSubject))
                    throw new StanNoServerSupport();
            }

            UnsubscribeRequest usr = new UnsubscribeRequest();
            usr.ClientID = clientID;
            usr.Subject = subject;
            usr.Inbox = ackInbox;
            byte[] b = ProtocolSerializer.marshal(usr);

            var r = nc.Request(requestSubject, b, 2000);
            SubscriptionResponse sr = new SubscriptionResponse();
            ProtocolSerializer.unmarshal(r.Data, sr);
            if (!string.IsNullOrEmpty(sr.Error))
                throw new StanException(sr.Error);
        }

        internal static string newInbox()
        {
            return "_INBOX." + newGUID();
        }

        public IStanSubscription Subscribe(string subject, EventHandler<StanMsgHandlerArgs> handler)
        {
            return Subscribe(subject, AsyncSubscription.DefaultOptions, handler);
        }

        public IStanSubscription Subscribe(string subject, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return subscribe(subject, null, handler, options);
        }

        public IStanSubscription Subscribe(string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler)
        {
            return Subscribe(subject, qgroup, AsyncSubscription.DefaultOptions, handler);
        }

        public IStanSubscription Subscribe(string subject, string qgroup, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
            if (qgroup == null)
                throw new ArgumentNullException(nameof(qgroup));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return subscribe(subject, qgroup, handler, options);
        }

        public void Close()
        {
            if (IsClosed)
                return;

            lock (mu)
            {
                ackSubscription?.Unsubscribe();
                ackSubscription = null;

                hbSubscription?.Unsubscribe();
                hbSubscription = null;

                CloseRequest req = new CloseRequest();
                req.ClientID = this.clientID;

                try
                {
                    if (this.closeRequests != null)
                    {
                        Msg reply = nc.Request(closeRequests, ProtocolSerializer.marshal(req));
                        if (reply != null)
                        {
                            CloseResponse resp = new CloseResponse();
                            try
                            {
                                ProtocolSerializer.unmarshal(reply.Data, resp);
                            }
                            catch (Exception e)
                            {
                                throw new StanCloseRequestException(e);
                            }

                            if (!string.IsNullOrEmpty(resp.Error))
                            {
                                throw new StanCloseRequestException(resp.Error);
                            }
                        }
                    }

                    if (ncOwned)
                    {
                        nc.Dispose();
                    }
                }
                catch (StanBadSubscriptionException)
                {
                    // it's possible we never actually connected.
                    return;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    // Dispose all managed resources.

                    try
                    {
                        Close();
                    }
                    catch (Exception) {  /* ignore */ }

                    GC.SuppressFinalize(this);
                }
                // Clean up unmanaged resources here.
            }
        }

        private bool IsClosed => NATSConnection.IsClosed();

        public string ClientID
        {
            get { return this.clientID; }
        }

        internal ProtocolSerializer ProtoSer
        {
            get { return this.ps; }
        }
    }
}
