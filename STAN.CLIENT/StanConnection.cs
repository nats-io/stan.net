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

        internal Timer ackTimer;
        object cond = new Object();
        bool isComplete = false;
        private Connection connection;
        private EventHandler<StanAckHandlerArgs> ah;
        private Exception ex = null;

        internal PublishAck(Connection conn, string guid, EventHandler<StanAckHandlerArgs> handler, long timeout)
        {
            connection = conn;
            guidValue = guid;

            ah = handler;
            guidValue = guid;
            ackTimer = new Timer(ackTimerCb, null, (int)timeout, Timeout.Infinite);
        }

        internal string GUID
        {
            get { return guidValue;  }
        }

        private void ackTimerCb(object state)
        {
            if (connection.removeAck(guidValue) != null)
            {
                InvokeHandler(guidValue, "Timeout occurred.");
            }
        }

        internal void wait(int timeout)
        {
            lock (cond)
            {
                while (!isComplete)
                {
                    Monitor.Wait(cond, timeout);
                }
                if (ex != null)
                    throw ex;
            }
        }

        internal void wait()
        {
            wait(Timeout.Infinite);
        }

        internal void complete()
        {
            lock (cond)
            {
                if (!isComplete)
                {
                    ackTimer.Dispose();
                    isComplete = true;
                    Monitor.Pulse(cond);
                }
            }
        }

        internal void InvokeHandler(string guidValue, string error)
        {
            try
            {
                if (ah != null)
                {
                    ah(this, new StanAckHandlerArgs(guidValue, error));
                }
                else
                {
                    if (string.IsNullOrEmpty(error) == false)
                        ex = new StanException(error);
                }
                complete();
            }
            catch { /* ignore user exceptions */ }
        }
    }

    public class Connection : IStanConnection, IDisposable
    {
        private readonly Object mu = new Object();

        private readonly string clientID;
        private readonly object connID; // This is a NUID that uniquely identifies connections.  Stored as a protobuf bytestring.
        private readonly string pubPrefix; // Publish prefix set by stan, append our subject.
        private readonly string subRequests; // Subject to send subscription requests.
        private readonly string unsubRequests; // Subject to send unsubscribe requests.
        private readonly string subCloseRequests; // Subject to send subscrption close requests.
        private readonly string closeRequests; // Subject to send close requests.
        private readonly string ackSubject; // publish acks

        private ISubscription ackSubscription = null;
        private ISubscription hbSubscription = null;
        private ISubscription pingSubscription = null;

        private readonly object pingLock = new object();
        private readonly NUID pubNUID = new NUID();
        private Timer pingTimer;
        private readonly byte[] pingBytes;
        private readonly string pingRequests;
        private readonly string pingInbox;
        private int pingInterval;
        private int pingOut;
        private int pingMaxOut;

        private IDictionary<string, AsyncSubscription> subMap = new Dictionary<string, AsyncSubscription>();
        private BlockingDictionary<string, PublishAck> pubAckMap;

        internal ProtocolSerializer ps = new ProtocolSerializer();

        private StanOptions opts = null;

        private IConnection nc;
        private bool ncOwned = false;

        private Connection() { }

        void protoUnsubscribe()
        {
            if (nc != null && !nc.IsClosed())
            {
                ackSubscription?.Unsubscribe();
                hbSubscription?.Unsubscribe();
                pingSubscription?.Unsubscribe();
            }
        }

        internal Connection(string stanClusterID, string clientID, StanOptions options)
        {
            this.clientID = clientID;
            connID = Google.Protobuf.ByteString.CopyFrom(System.Text.Encoding.UTF8.GetBytes(pubNUID.Next));

            opts = (options != null) ? new StanOptions(options) : new StanOptions();

            if (opts.natsConn == null)
            {
                ncOwned = true;
                try
                {
                    nc = new ConnectionFactory().CreateConnection(opts.NatsURL);
                    nc.Opts.MaxReconnect = Options.ReconnectForever;
                    // TODO:  disable buffering.
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

            // Prepare a subscription on ping responses, even if we are not
            // going to need it, so that if that fails, it fails before initiating
            // a connection.
            pingSubscription = nc.SubscribeAsync(newInbox(), processPingResponse); 

            // create a heartbeat inbox
            string hbInbox = newInbox();
            hbSubscription = nc.SubscribeAsync(hbInbox, processHeartBeat);

            string discoverSubject = opts.discoverPrefix + "." + stanClusterID;

            // The streaming server expects seconds, but can handle millis
            // millis are denoted by negative numbers.
            int pi;
            if (opts.PingInterval < 1000)
                pi = opts.pingInterval * -1;
            else
                pi = opts.pingInterval / 1000;

            ConnectRequest req = new ConnectRequest
            {
                ClientID = clientID,
                HeartbeatInbox = hbInbox,
                ConnID = (Google.Protobuf.ByteString)connID,
                Protocol = StanConsts.protocolOne,
                PingMaxOut = opts.PingMaxOutstanding,
                PingInterval = pi
            };

            Msg cr;
            try
            {
                cr = nc.Request(discoverSubject,
                    ProtocolSerializer.marshal(req),
                    opts.ConnectTimeout);
            }
            catch (NATSTimeoutException)
            {
                protoUnsubscribe();
                throw new StanConnectRequestTimeoutException(
                    string.Format("No response from a streaming server with a cluster ID of '{0}'", stanClusterID));
            }

            ConnectResponse response = new ConnectResponse();
            try
            {
                ProtocolSerializer.unmarshal(cr.Data, response);
            }
            catch (Exception e)
            {
                protoUnsubscribe();
                throw new StanConnectRequestException(e);
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                protoUnsubscribe();
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

            pubAckMap = new BlockingDictionary<string, PublishAck>(opts.maxPubAcksInflight);

            // TODO - check out sub map and chans

            bool unsubPing = true;

            // Do this with servers which are at least at protcolOne.
            if (response.Protocol >= StanConsts.protocolOne)
            {

                // Note that in the future server may override client ping
                // interval value sent in ConnectRequest, so use the
                // value in ConnectResponse to decide if we send PINGs
                // and at what interval.
                // In tests, the interval could be negative to indicate
                // milliseconds.
                if (response.PingInterval != 0) {
                    unsubPing = false;

                    // These will be immutable
                    pingRequests = response.PingRequests;
                    pingInbox = pingSubscription.Subject;

                    // negative values returned from the server are ms
                    if (response.PingInterval < 0)
                    {
                        pingInterval = response.PingInterval * -1;
                    }
                    else
                    {
                        // if positive, the result is in seconds, but 
                        // in the .NET clients we always use millis.
                        pingInterval = response.PingInterval * 1000;
                    }

                    pingMaxOut = response.PingMaxOut;
                    pingBytes = ProtocolSerializer.createPing(connID);

                    pingTimer = new Timer(pingServer, null, pingInterval, Timeout.Infinite);
                }
            }
            if (unsubPing)
            {
                pingSubscription.Unsubscribe();
                pingSubscription = null;
            }

        }

        // Sends a PING (containing the connection's ID) to the server at intervals
        // specified by PingInterval option when connection is created.
        // Everytime a PING is sent, the number of outstanding PINGs is increased.
        // If the total number is > than the PingMaxOut option, then the connection
        // is closed, and connection error callback invoked if one was specified.
        private void pingServer(object state)
        {
            IConnection conn = null;
            Exception pingEx = null;
            bool lostConnection = false;

            lock (pingLock)
            {
                // we're already closed, just exit
                if (pingTimer == null)
                {
                    return;
                }

                // disable timer, will never be called.
                pingTimer.Change(Timeout.Infinite, Timeout.Infinite);

                pingOut++;
                conn = nc;

                if (pingOut > pingMaxOut)
                {
                    lostConnection = true;
                    pingEx = new StanMaxPingsException();
                }
                else
                {
                    //reactivate timer.
                    pingTimer.Change(pingInterval, Timeout.Infinite);
                }
            }

            if (lostConnection)
            {
                closeDueToPing(pingEx);
                return;
            }
            
            try
            {
                conn.Publish(pingRequests, pingInbox, pingBytes);
            }
            catch (Exception ex)
            when (
                ex is NATSConnectionClosedException || 
                ex is NATSStaleConnectionException)
            {
                closeDueToPing(ex);
            }
        }

        private void closeDueToPing(Exception ex)
        {
            lock (mu)
            {
                if (nc == null)
                {
                    return;
                }


                // Stop timer, unsubscribe, fail the pubs, etc..
                cleanupOnClose(ex);

                // No need to send Close prototol, so simply close the underlying
                // NATS connection (if we own it, and if not already closed)
                if (ncOwned && !nc.IsClosed())
                {
                    nc.Close();
                }

                // Mark this streaming connection as closed. Do this under pingMu lock.
                lock (pingLock)
                {
                    nc = null;
                }

            }

            // Schedule a task to invoke the callback.  This event handler is immutable,
            // so it's OK to use directly.
            if (opts.ConnectionLostEventHandler != null)
            {
                // Not ideal, but we really don't have a close to wait on this.
                Task.Run(() =>
                {
                    try
                    {
                        opts.ConnectionLostEventHandler(this, new StanConnLostHandlerArgs(this, ex));
                    }
                    catch { /* ignore, be nice to the background thread */ }
                });
            }
        }

        // caller must lock under mu
        private void cleanupOnClose(Exception ex)
        {
            lock (pingLock)
            {
                pingTimer?.Dispose();
            }

            // Unsubscribe only if we have a connection...
            if (nc != null && !nc.IsClosed()) {
                ackSubscription?.Unsubscribe();
                pingSubscription?.Unsubscribe();
            }

            // fail all pending pubs...
            PublishAck pa;
            var keys = pubAckMap.Keys;
            foreach (string guid in keys)
            {
                if (pubAckMap.Remove(guid, out pa))
                {
                    pa.InvokeHandler(guid, ex == null ? "Connection Closed." : ex.Message);
                }
            }
        }

        private void processPingResponse(object sender, MsgHandlerEventArgs e)
        {
            // No data means OK (no need to unmarshall)
            var data = e.Message.Data;
            if (data?.Length > 0)
            {
                var pingResp = new PingResponse();
                try
                {
                    ProtocolSerializer.unmarshal(data, pingResp);
                }
                catch
                {
                    // Ignore this as an invalid protocol message.
                    return;
                }

                string err = pingResp.Error;
                if (err?.Length > 0)
                {
                    closeDueToPing(new StanException(err));
                }
            }

            lock (pingLock)
            {
                pingOut = 0;
            }
        }

        private void processHeartBeat(object sender, MsgHandlerEventArgs args)
        {
            IConnection lnc;

            lock (mu)
            {
                lnc = nc;
            }

            if (lnc != null)
                lnc.Publish(args.Message.Reply, null);
        }

        internal PublishAck removeAck(string guid)
        {
            PublishAck a;

            lock (mu)
            {
                if (pubAckMap.Remove(guid, out a))
                    return a;
            }

            return null;
        }

        public IConnection NATSConnection
        {
            get
            {
                lock (mu)
                {
                    return nc;
                }
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

            PublishAck a = removeAck(pa.Guid);

            if (a != null)
                a.InvokeHandler(pa.Guid, pa.Error);
        }

        internal void processMsg(object sender, MsgHandlerEventArgs args)
        {
            bool isClosed = false;
            AsyncSubscription sub = null;
            Msg raw = null;

            MsgProto mp = new MsgProto();
            ProtocolSerializer.unmarshal(args.Message.Data, mp);

            raw = args.Message;

            lock (mu)
            {
                isClosed = (nc == null);
                subMap.TryGetValue(raw.Subject, out sub);
            }

            if (isClosed || sub == null)
                return;

            sub.processMsg(mp);
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
            string localAckSubject = null;
            long localAckTimeout = 0;

            string subj = this.pubPrefix + "." + subject;
            string guidValue = newGUID();
            byte[] b = ProtocolSerializer.createPubMsg(clientID, guidValue, subject, data, connID);
            PublishAck a = null;

            lock (mu)
            {
                if (nc == null)
                    throw new StanConnectionClosedException();

                if (nc.IsReconnecting())
                    throw new StanConnectionException("The NATS connection is reconnecting");

                a = new PublishAck(this, guidValue, handler, opts.PubAckWait);
                while (!pubAckMap.TryAdd(guidValue, a))
                {
                    var bd = pubAckMap;

                    Monitor.Exit(mu);
                    // Wait for space outside of the lock so 
                    // acks can be removed.
                    bd.waitForSpace();
                    Monitor.Enter(mu);

                    if (nc == null)
                    {
                        throw new StanConnectionClosedException();
                    }
                }

                localAckSubject = ackSubject;
                localAckTimeout = opts.ackTimeout;
            }

            try
            {
                nc.Publish(subj, localAckSubject, b);

                // Flush to reduce latency.
                // 
                // TODO:  Add a soft (non-ping) flush to NATS.net 
                // for this type of situation.  Only flush in 
                // blocking publish calls.
                if (handler == null)
                {
                    nc.Flush();
                }
            }
            catch
            {
                removeAck(guidValue);
                throw;
            }

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

            lock (mu)
            {
                if (nc == null)
                {
                    throw new StanConnectionClosedException();
                }

                // Register the subscription
                subMap[sub.Inbox] = sub;
                IConnection localNc = nc;
            }

            try
            {
                sub.subscribe(subRequests, subject, qgroup, handler);
            }
            catch
            {
                lock (mu)
                {
                    subMap.Remove(sub.Inbox);
                }

                throw;
            }

            return sub;
        }

        internal void unsubscribe(string subject, string inbox, string ackInbox, bool close)
        {
            IConnection lnc;

            lock (mu)
            {
                lnc = nc;
                if (lnc == null)
                    throw new StanConnectionClosedException();
                subMap.Remove(inbox);
            }

            string requestSubject = unsubRequests;
            if (close)
            {
                requestSubject = subCloseRequests;
                if (string.IsNullOrEmpty(requestSubject))
                    throw new StanNoServerSupport();
            }

            UnsubscribeRequest usr = new UnsubscribeRequest
            {
                ClientID = clientID,
                Subject = subject,
                Inbox = ackInbox
            };
            byte[] b = ProtocolSerializer.marshal(usr);

            var r = lnc.Request(requestSubject, b, 2000);
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
                throw new ArgumentNullException("subject");
            if (options == null)
                throw new ArgumentNullException("options");
            if (handler == null)
                throw new ArgumentNullException("handler");

            return subscribe(subject, null, handler, options);
        }

        public IStanSubscription Subscribe(string subject, string qgroup,EventHandler<StanMsgHandlerArgs> handler)
        {
            return Subscribe(subject, qgroup, AsyncSubscription.DefaultOptions, handler);
        }

        public IStanSubscription Subscribe(string subject, string qgroup, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException("subject");
            if (qgroup == null)
                throw new ArgumentNullException("qgroup");
            if (options == null)
                throw new ArgumentNullException("options");
            if (handler == null)
                throw new ArgumentNullException("handler");

            return subscribe(subject, qgroup, handler, options);
        }

        public void Close()
        {
            Msg reply = null;

            lock (mu)
            {
                cleanupOnClose(null);

                if (nc == null || nc.IsClosed())
                {
                    nc = null;
                    return;
                }

                CloseRequest req = new CloseRequest
                {
                    ClientID = clientID
                };

                try
                {
                    if (closeRequests != null)
                    {
                        reply = nc.Request(closeRequests, ProtocolSerializer.marshal(req));
                    }
                }
                catch (StanBadSubscriptionException)
                {
                    // it's possible we never actually connected.
                    return;
                }

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

                if (ncOwned)
                {
                    nc.Close();
                }

                nc = null;
            }
        }

        public void Dispose()
        {
            try { Close(); } catch (Exception) { }
        }

        public string ClientID 
        { 
            get { return this.clientID; }
        }

        internal ProtocolSerializer ProtoSer
        {
            get { return ps; }
        }
    }
}
