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
        // fields

        private volatile bool _completed;

        private Connection _conn;
        private EventHandler<StanAckHandlerArgs> _handler;
        private TimeSpan _timeout;
        private Timer _timer;
        private Exception _ex;

        // constructors

        internal PublishAck(Connection conn, string guid, EventHandler<StanAckHandlerArgs> handler, long timeout)
        {
            GUID = guid;
            _conn = conn;
            _handler = handler;
            _timeout = TimeSpan.FromMilliseconds(timeout);
            _timer = new Timer(AckTimeout, null, Timeout.Infinite, Timeout.Infinite);
        }

        // auxiliary properties and methods

        private void AckTimeout(object state) => _conn.HandleAck(GUID, "Timeout occurred.", true);

        // public api

        public string GUID { get; }

        public void StartTimeoutMonitor()
        {
            try
            {
                _timer.Change(_timeout, Timeout.InfiniteTimeSpan);
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

        public void Wait()
        {
            SpinWait.SpinUntil(() => _completed);

            if (_ex != null) throw _ex;
        }

        public void Complete(string error, bool dataPublished)
        {
            if (!_completed)
            {
                _timer.Dispose();

                if (dataPublished)
                {
                    error = error?.Trim();

                    if (_handler != null)
                    {
                        try
                        {
                            _handler(this, new StanAckHandlerArgs(GUID, error));
                        }
                        catch { /* ignore user exceptions */ }
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        _ex = new StanException(error);
                    }
                }

                _completed = true;
            }
        }
    }

    public class Connection : IStanConnection, IDisposable
    {
        // fields

        private readonly object _lock = new object();

        private volatile bool _disposed;

        private readonly string _pubPrefix; // Publish prefix set by stan, append our subject.
        private readonly string _subRequests; // Subject to send subscription requests.
        private readonly string _unsubRequests; // Subject to send unsubscribe requests.
        private readonly string _subCloseRequests; // Subject to send subscrption close requests.
        private readonly string _closeRequests; // Subject to send close requests.
        private readonly string _ackSubject; // publish acks

        private ISubscription _ackSubscription;
        private ISubscription _hbSubscription;

        private Dictionary<string, AsyncSubscription> _subs;
        private Dictionary<string, PublishAck> _pubACKs;

        private StanOptions _opts;

        private bool _ncOwned = false;

        // constructors

        private Connection() { }

        internal Connection(string clusterID, string clientID, StanOptions options)
        {
            ClientID = clientID;

            _opts = options != null ? new StanOptions(options) : new StanOptions();

            if (_opts.natsConn == null)
            {
                _ncOwned = true;
                try
                {
                    NATSConnection = new ConnectionFactory().CreateConnection(_opts.NatsURL);
                }
                catch (Exception e)
                {
                    throw new StanConnectionException(e);
                }
            }
            else
            {
                _ncOwned = false;
                NATSConnection = _opts.natsConn;
            }

            // create a heartbeat inbox
            string hbInbox = NewInbox();
            _hbSubscription = NATSConnection.SubscribeAsync(hbInbox, ProcessHeartBeat);

            var resp = new ConnectResponse();
            try
            {
                byte[] data = ProtocolSerializer.marshal(new ConnectRequest
                {
                    ClientID = ClientID,
                    HeartbeatInbox = hbInbox,
                });

                Msg cr = NATSConnection.Request(
                    $"{_opts.discoverPrefix}.{clusterID}",
                    data,
                    _opts.ConnectTimeout
                    );

                ProtocolSerializer.unmarshal(cr.Data, resp);
            }
            catch (NATSTimeoutException)
            {
                throw new StanConnectRequestTimeoutException();
            }
            catch (Exception e)
            {
                throw new StanConnectRequestException(e);
            }

            if (!string.IsNullOrWhiteSpace(resp.Error))
            {
                throw new StanConnectRequestException(resp.Error);
            }

            // capture cluster configuration endpoints to publish and subscribe/unsubscribe
            _pubPrefix = resp.PubPrefix;
            _subRequests = resp.SubRequests;
            _unsubRequests = resp.UnsubRequests;
            _subCloseRequests = resp.SubCloseRequests;
            _closeRequests = resp.CloseRequests;

            // setup the Ack subscription
            _ackSubject = $"{StanConsts.DefaultACKPrefix}.{NewGUID()}";
            _ackSubscription = NATSConnection.SubscribeAsync(_ackSubject, ProcessAck);

            // TODO:  hardcode or options?
            _ackSubscription.SetPendingLimits(1024 * 1024, 32 * 1024 * 1024);

            _subs = new Dictionary<string, AsyncSubscription>();
            _pubACKs = new Dictionary<string, PublishAck>();

            ProtoSer = new ProtocolSerializer();
        }

        // auxiliary propertites and methods

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

        private void ProcessHeartBeat(object sender, MsgHandlerEventArgs args) => NATSConnection.Publish(args.Message.Reply, null);

        private PublishAck RemoveAck(string guid)
        {
            PublishAck ack;

            lock (_lock)
            {
                if (_pubACKs.TryGetValue(guid, out ack))
                {
                    _pubACKs.Remove(guid);
                    Monitor.Pulse(_lock);
                }
            }

            return ack;
        }

        internal void HandleAck(string guid, string error, bool dataPublished) => RemoveAck(guid)?.Complete(error, dataPublished);

        private void ProcessAck(object sender, MsgHandlerEventArgs args)
        {
            var ack = new PubAck();

            try
            {
                ProtocolSerializer.unmarshal(args.Message.Data, ack);
            }
            catch (Exception)
            {
                // TODO:  (cls) handle this...
                return;
            }

            HandleAck(ack.Guid, ack.Error, true);
        }

        public IConnection NATSConnection { get; }

        private bool IsClosed => NATSConnection.IsClosed();

        private static string NewGUID() => NUID.NextGlobal;

        private PublishAck publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler)
        {
            string subj = $"{_pubPrefix}.{subject}";
            string guid = NewGUID();
            byte[] b = ProtocolSerializer.createPubMsg(ClientID, guid, subject, data);

            var ack = new PublishAck(this, guid, handler, _opts.PubAckWait);

            lock (_lock)
            {
                while (_pubACKs.Count >= _opts.maxPubAcksInflight)
                {
                    Monitor.Wait(_lock);
                }
                _pubACKs[ack.GUID] = ack;
            }

            try
            {
                NATSConnection.Publish(subj, _ackSubject, b);
            }
            catch (Exception e)
            {
                HandleAck(guid, null, false);
                throw e is NATSConnectionClosedException ? new StanConnectionClosedException(e) : e;
            }

            ack.StartTimeoutMonitor();

            return ack;
        }

        public void Publish(string subject, byte[] data) => publish(subject, data, null).Wait();

        public string Publish(string subject, byte[] data, EventHandler<StanAckHandlerArgs> handler) => publish(subject, data, handler).GUID;

        public Task<string> PublishAsync(string subject, byte[] data)
        {
            var ack = publish(subject, data, null);

            var t = new Task<string>(() =>
            {
                ack.Wait();
                return ack.GUID;
            });
            t.Start();

            return t;
        }

        private IStanSubscription Subscribe(string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler, StanSubscriptionOptions options)
        {
            var sub = new AsyncSubscription(this, options);

            sub.Subscribe(_subRequests, subject, qgroup, handler);

            lock (_lock)
            {
                // Register the subscription
                _subs[sub.Inbox] = sub;
            }

            return sub;
        }

        internal void Unsubscribe(string subject, string inbox, string ackInbox, bool close)
        {
            lock (_lock)
            {
                _subs.Remove(inbox);
            }

            string requestSubject = _unsubRequests;
            if (close)
            {
                if (string.IsNullOrEmpty(_subCloseRequests))
                {
                    throw new StanNoServerSupport();
                }
                requestSubject = _subCloseRequests;
            }

            byte[] b = ProtocolSerializer.marshal(new UnsubscribeRequest
            {
                ClientID = ClientID,
                Subject = subject,
                Inbox = ackInbox,
            });

            var r = NATSConnection.Request(requestSubject, b, 2000);
            var sr = new SubscriptionResponse();
            ProtocolSerializer.unmarshal(r.Data, sr);
            if (!string.IsNullOrEmpty(sr.Error))
                throw new StanException(sr.Error);
        }

        internal static string NewInbox() => "_INBOX." + NewGUID();

        public IStanSubscription Subscribe(string subject, EventHandler<StanMsgHandlerArgs> handler) => 
            Subscribe(subject, AsyncSubscription.DefaultOptions, handler);

        public IStanSubscription Subscribe(string subject, StanSubscriptionOptions options, EventHandler<StanMsgHandlerArgs> handler)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return Subscribe(subject, null, handler, options);
        }

        public IStanSubscription Subscribe(string subject, string qgroup,EventHandler<StanMsgHandlerArgs> handler) =>
            Subscribe(subject, qgroup, AsyncSubscription.DefaultOptions, handler);

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

            return Subscribe(subject, qgroup, handler, options);
        }

        public void Close()
        {
            if (IsClosed)
                return;

            lock (_lock)
            {
                _ackSubscription?.Unsubscribe();
                _hbSubscription?.Unsubscribe();

                try
                {
                    if (_closeRequests != null)
                    {
                        var data = ProtocolSerializer.marshal(new CloseRequest { ClientID = ClientID });
                        Msg reply = NATSConnection.Request(_closeRequests, data, _opts.CloseTimeout);
                        if (reply != null)
                        {
                            var resp = new CloseResponse();
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

                    if (_ncOwned)
                    {
                        NATSConnection.Dispose();
                    }
                }
                catch (StanBadSubscriptionException)
                {
                    // it's possible we never actually connected.
                    return;
                }
            }
        }

        public void Dispose() => Dispose(true);

        public string ClientID { get; }

        internal ProtocolSerializer ProtoSer { get; }
    }
}