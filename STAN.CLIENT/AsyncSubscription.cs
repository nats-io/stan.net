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
        private EventHandler<StanMsgHandlerArgs> handler;
        private DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();

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
            Dispose(false);
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
                        unsubscribe(IsDurable && options.LeaveOpen);
                    }
                    catch (Exception) {  /* ignore */ }

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
            rwLock.EnterWriteLock();
            try
            {
                this.handler += handler;
                this.subject = subject;

                if (sc == null)
                {
                    throw new StanConnectionClosedException();
                }

                // Listen for actual messages.
                inboxSub = sc.NATSConnection.SubscribeAsync(inbox, sc.processMsg);

                SubscriptionRequest sr = new SubscriptionRequest();
                sr.ClientID = sc.ClientID;
                sr.Subject = subject;
                sr.QGroup = (qgroup == null ? "" : qgroup);
                sr.Inbox = inbox;
                sr.MaxInFlight = options.MaxInflight;
                sr.AckWaitInSecs = options.AckWait / 1000;
                sr.StartPosition = options.startAt;
                sr.DurableName = (options.DurableName == null ? "" : options.DurableName);

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

                // TODO:  Configure request timeout?
                Msg m = sc.NATSConnection.Request(subRequestSubject, b, 2000);

                SubscriptionResponse r = new SubscriptionResponse();
                ProtocolSerializer.unmarshal(m.Data, r);

                if (string.IsNullOrWhiteSpace(r.Error) == false)
                {
                    throw new StanException(r.Error);
                }

                ackInbox = r.AckInbox;
            }
            catch
            {
                if (inboxSub != null)
                {
                    try
                    {
                        inboxSub.Unsubscribe();
                    }
                    catch (NATSTimeoutException)
                    {
                        // NOOP - this is unrecoverable.
                    }
                }
                throw;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        // The unsubscribe method handles both the subscripition 
        // unsubscribe and close operations.
        private void unsubscribe(bool close)
        {
            string linbox = null;
            string lAckInbox = null;
            Connection lsc = null;

            rwLock.EnterWriteLock();

            try
            {
                if (sc == null)
                    throw new StanBadSubscriptionException();

                lsc = sc;
                sc = null;

                linbox = inboxSub.Subject;
                inboxSub.Unsubscribe();
                inboxSub = null;

                lAckInbox = ackInbox;
                ackInbox = null;
            }
            catch
            {
                throw;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }

            lsc.unsubscribe(subject, linbox, lAckInbox, close);
        }

        public void Unsubscribe() => unsubscribe(false);

        public void Close() => unsubscribe(true);

        internal void manualAck(StanMsg m)
        {
            if (m == null)
                return;

            rwLock.EnterReadLock();
            
            string localAckSubject = ackInbox;
            bool   localManualAck = options.ManualAcks;
            Connection sc = this.sc;

            rwLock.ExitReadLock();

            if (localManualAck == false)
            {
                throw new StanManualAckException();
            }

            if (sc == null)
            {
                throw new StanBadSubscriptionException();
            }

            byte[] b = ProtocolSerializer.createAck(m.proto);
            sc.NATSConnection.Publish(localAckSubject, b);
        }

        internal void processMsg(MsgProto mp)
        {
            rwLock.EnterReadLock();

            EventHandler<StanMsgHandlerArgs> cb = handler;
            bool isManualAck  = options.ManualAcks;
            string localAckSubject = ackInbox;
            IStanConnection subsSc = sc;
            IConnection localNc = null;

            if (subsSc != null)
            {
                localNc = sc.NATSConnection;
            }

            rwLock.ExitReadLock();

            if (cb != null && subsSc != null)
            {
                StanMsgHandlerArgs args = new StanMsgHandlerArgs(new StanMsg(mp, this));
                cb(this, args);
            }

            if (!isManualAck && localNc != null)
            {
                byte[] b = ProtocolSerializer.createAck(mp);
                try
                {
                    localNc.Publish(localAckSubject, b);
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

        public void Dispose() => Dispose(true);

        internal static StanSubscriptionOptions DefaultOptions => new StanSubscriptionOptions();
    }
}
