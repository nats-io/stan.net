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
using NATS.Client;
using System;
using System.Threading;

namespace STAN.Client
{
    class AsyncSubscription : IStanSubscription
    {
        private StanSubscriptionOptions options;
        private string inbox = null;
        private string subject = null;
        private Connection sc = null;
        private string ackInbox = null;
        private NATS.Client.IAsyncSubscription inboxSub = null;

        private volatile bool _disposed;

        internal AsyncSubscription(Connection stanConnection, StanSubscriptionOptions opts)
        {
            // TODO: Complete member initialization
            options = new StanSubscriptionOptions(opts);
            inbox = Connection.newInbox();
            sc = stanConnection;
        }

        /// <summary>	
        /// Releases unmanaged resources and performs other cleanup operations	
        /// </summary>
        ~AsyncSubscription()
        {
            Dispose(false, false, false);
        }

        private void Dispose(bool disposing, bool close, bool throwEx)
        {
            if (!_disposed)
            {
                _disposed = true;

                if (disposing)
                {
                    // Dispose all managed resources.

                    try
                    {
                        // handles both the subscripition unsubscribe and close operations.
                        inboxSub.Unsubscribe();
                        sc.unsubscribe(subject, inboxSub.Subject, ackInbox, close);
                    }
                    catch
                    {
                        if (throwEx)
                            throw;
                    }

                    GC.SuppressFinalize(this);
                }
                // Clean up unmanaged resources here.
            }
        }

        internal bool IsDurable => !string.IsNullOrEmpty(options.DurableName);

        internal string Inbox => inbox;

        internal static long convertTimeSpan(TimeSpan ts) => ts.Ticks * 100;

        // in STAN, much of this code is in the connection module.
        internal void subscribe(string subRequestSubject, string subject, string qgroup, EventHandler<StanMsgHandlerArgs> handler)
        {
            this.subject = subject;

            // Listen for actual messages.
            inboxSub = sc.NATSConnection.SubscribeAsync(Inbox, (sender, args) =>
            {
                if (!_disposed)
                {
                    var msg = new MsgProto();
                    ProtocolSerializer.unmarshal(args.Message.Data, msg);

                    handler(this, new StanMsgHandlerArgs(new StanMsg(msg, this)));

                    if (!options.ManualAcks)
                    {
                        try
                        {
                            ack(msg);
                        }
                        catch (Exception)
                        {
                            /*
                             * Ignore - subscriber could have closed the connection 
                             * or there's been a connection error.  The server will 
                             * resend the unacknowledged messages.
                             */
                        }
                    }
                }
            });

            try
            {
                SubscriptionRequest sr = new SubscriptionRequest();
                sr.ClientID = sc.ClientID;
                sr.Subject = subject;
                sr.QGroup = qgroup ?? string.Empty;
                sr.Inbox = inbox;
                sr.MaxInFlight = options.MaxInflight;
                sr.AckWaitInSecs = options.AckWait / 1000;
                sr.StartPosition = options.startAt;
                sr.DurableName = options.DurableName ?? string.Empty;

                // Conditionals
                switch (sr.StartPosition)
                {
                    case StartPosition.TimeDeltaStart:
                        sr.StartTimeDelta = convertTimeSpan(
                            options.useStartTimeDelta ?
                                options.startTimeDelta :
                                (DateTime.UtcNow - options.startTime));
                        break;
                    case StartPosition.SequenceStart:
                        sr.StartSequence = options.startSequence;
                        break;
                }

                byte[] b = ProtocolSerializer.marshal(sr);

                // TODO: Configure request timeout?
                Msg m = sc.NATSConnection.Request(subRequestSubject, b, 2000);

                SubscriptionResponse r = new SubscriptionResponse();
                ProtocolSerializer.unmarshal(m.Data, r);
                if (!string.IsNullOrWhiteSpace(r.Error))
                {
                    throw new StanException(r.Error);
                }

                ackInbox = r.AckInbox;
            }
            catch
            {
                inboxSub.Dispose();
                throw;
            }
        }

        public void Unsubscribe() => Dispose(true, false, true);

        public void Close() => Dispose(true, true, true);

        internal void manualAck(StanMsg m)
        {
            if (!options.ManualAcks)
            {
                throw new StanManualAckException();
            }

            ack(m.proto);
        }

        private void ack(MsgProto msg) => sc.NATSConnection.Publish(ackInbox, ProtocolSerializer.createAck(msg));

        public void Dispose() => Dispose(true, IsDurable && options.leaveOpen, false);

        internal static StanSubscriptionOptions DefaultOptions => new StanSubscriptionOptions();
    }
}
