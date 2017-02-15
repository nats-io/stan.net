/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
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
 * messaging system, and is supported by [Apcera](http://www.apcera.com).
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
            ackTimer = new Timer(ackTimerCb);
            ackTimer.Change(timeout, Timeout.Infinite);
        }

        internal string GUID
        {
            get { return guidValue;  }
        }

        private void ackTimerCb(object state)
        {
            connection.removeAck(this.guidValue);
            invokeHandler(guidValue, "Timeout occurred.");
        }

        internal void wait(int timeout)
        {
            lock (cond)
            {
                if (!isComplete)
                {
                    Monitor.Wait(cond, timeout);
                }
            }
        }

        internal void wait()
        {
            wait(Timeout.Infinite);
            if (ex != null)
                throw ex;
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

        internal void invokeHandler(string guidValue, string error)
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
        private Object mu = new Object();

        private readonly string clientID;
        private readonly string pubPrefix; // Publish prefix set by stan, append our subject.
        private readonly string subRequests; // Subject to send subscription requests.
        private readonly string unsubRequests; // Subject to send unsubscribe requests.
        private readonly string subCloseRequests; // Subject to send subscrption close requests.
        private readonly string closeRequests; // Subject to send close requests.
        private readonly string ackSubject; // publish acks

        private ISubscription ackSubscription;
        private ISubscription hbSubscription;

        private IDictionary<string, AsyncSubscription> subMap = new Dictionary<string, AsyncSubscription>();
        private BlockingDictionary<string, PublishAck> pubAckMap;

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

            pubAckMap = new BlockingDictionary<string, PublishAck>(opts.maxPubAcksInflight);
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
                pubAckMap.Remove(guid, out a, 0);
            }

            return a;
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
                a.invokeHandler(pa.Guid, pa.Error);
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

            StanMsg msg = new StanMsg(mp, sub);

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
            byte[] b = ProtocolSerializer.createPubMsg(clientID, guidValue, subject, data);

            PublishAck a = new PublishAck(this, guidValue, handler, opts.PubAckWait);

            lock (mu)
            {
                if (nc == null)
                    throw new StanConnectionClosedException();

                if (pubAckMap.isAtCapacity())
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

                pubAckMap.Add(guidValue, a);
                localAckSubject = ackSubject;
                localAckTimeout = opts.ackTimeout;
            }

            try
            {
                nc.Publish(subj, localAckSubject, b);
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
                subMap.Remove(sub.Inbox);
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

            UnsubscribeRequest usr = new UnsubscribeRequest();
            usr.ClientID = clientID;
            usr.Subject = subject;
            usr.Inbox = ackInbox;
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

                IConnection lnc = nc;
                nc = null;

                if (lnc == null)
                    return;

                if (lnc.IsClosed())
                    return;

                if (ackSubscription != null)
                {
                    ackSubscription.Unsubscribe();
                    ackSubscription = null;
                }

                if (hbSubscription != null)
                {
                    hbSubscription.Unsubscribe();
                    hbSubscription = null;
                }

                CloseRequest req = new CloseRequest();
                req.ClientID = this.clientID;

                try
                {
                    if (this.closeRequests != null)
                    {
                        reply = lnc.Request(closeRequests, ProtocolSerializer.marshal(req));
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

                if (ncOwned && lnc != null)
                {
                    lnc.Close();
                }
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
            get { return this.ps; }
        }
    }
}
