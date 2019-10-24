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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client;
using STAN.Client;
using Xunit;

namespace IntegrationTests
{
    public class BasicTests
    {
        const int DEFAULT_WAIT = 10000;
        const string CLUSTER_ID = "test-cluster";
        const string CLIENT_ID = "me";

        readonly StanConnectionFactory stanConnectionFactory = new StanConnectionFactory();
        readonly ConnectionFactory natsConnectionFactory = new ConnectionFactory();

        EventHandler<StanMsgHandlerArgs> noopMh = (obj, args) => { /* NOOP */ };

        internal StanOptions getDefaultTestOptions()
        {
            var opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = "nats://127.0.0.1:4222";
            opts.ConnectTimeout = 5000;

            return opts;
        }

        internal IStanConnection getStanConnection(string clusterId = CLUSTER_ID, string clientId = CLIENT_ID, StanOptions opts = null)
            => stanConnectionFactory.CreateConnection(clusterId, clientId, opts ?? getDefaultTestOptions());

        internal IConnection getNatsConnection(Options opts = null) =>
            opts == null
                ? natsConnectionFactory.CreateConnection()
                : natsConnectionFactory.CreateConnection(opts);

        internal static byte[] getPayload(string s)
        {
            if (s == null)
                return null;

            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        [Fact]
        public void TestNoServer()
        {
            // Do not start a streaming server.
            Assert.Throws<StanConnectionException>(() => getStanConnection());
        }

        [Fact]
        public void TestUnreachable()
        {
            bool thrown = false;
            using (new NatsStreamingServer())
            {
                try
                {
                    using(getStanConnection("invalid-cluster")){}
                }
                catch (StanConnectRequestTimeoutException se)
                {
                    thrown = true;
                    Assert.Contains("invalid-cluster", se.Message);
                }
                Assert.True(thrown);
            }
        }

        [Fact]
        public void TestNatsConnNotClosedOnClose()
        {
            using (new NatsStreamingServer())
            {
                using (var nc = getNatsConnection())
                {
                    var opts = StanOptions.GetDefaultOptions();
                    opts.NatsConn = nc;
                    using(var sc = getStanConnection(opts: opts))
                        sc.Close();

                    Assert.True(nc.IsClosed() == false);
                }

            }
        }

        [Fact]
        public void TestBasicConnect()
        {
            using (new NatsStreamingServer())
            {
                using(var c = getStanConnection())
                    c.Close();
            }
        }

        [Fact]
        public void TestBasicPublish()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    c.Publish("foo", getPayload("hello"));
                    c.Publish("foo", null);
                }
            }
        }

        [Fact]
        public void TestBasicPubAcksInFlight()
        {
            using (new NatsStreamingServer())
            {
                var opts = StanOptions.GetDefaultOptions();
                opts.MaxPubAcksInFlight = 2;
                opts.PubAckWait = 10 * 1000;

                using (var c = getStanConnection(opts: opts))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            c.Publish("foo", getPayload("hello"));
                        }
                        catch (StanException)
                        {
                            Assert.True(false, "Timed out on msg " + i);
                        }
                    }
                }
            }
        }

        [Fact]
        public void TestBasicAsyncPublish()
        {
            var ev = new AutoResetEvent(false);
            string cbGuid = null;
            string pubGuid = null;
            string err = null;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    pubGuid = c.Publish("foo", getPayload("hello"), (obj, args) =>
                    {
                        cbGuid = args.GUID;
                        err = args.Error;
                        ev.Set();
                    });
                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }

                Assert.False(string.IsNullOrWhiteSpace(pubGuid));
                Assert.True(string.IsNullOrWhiteSpace(err));
                Assert.Equal(pubGuid, cbGuid);
            }
        }

        [Fact]
        public void TestTimeoutAsyncPublish()
        {
            var ev = new AutoResetEvent(false);
            string cbGuid = null;
            string pubGuid = null;
            string err = null;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    pubGuid = c.Publish("foo", getPayload("hello"), (obj, args) =>
                    {
                        cbGuid = args.GUID;
                        err = args.Error;
                        ev.Set();
                    });

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }

                Assert.False(string.IsNullOrWhiteSpace(pubGuid));
                Assert.True(string.IsNullOrWhiteSpace(err));
                Assert.Equal(pubGuid, cbGuid);
            }
        }

        [Fact]
        public void TestBasicSubscription()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var sub = c.Subscribe("foo", noopMh);
                    sub.Unsubscribe();
                }
            }
        }

        [Fact]
        public void TestBasicQueueSubscription()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var sub = c.Subscribe("foo", "bar", noopMh);
                    sub.Unsubscribe();
                }
            }
        }

        [Fact]
        public void TestBasicPubSub()
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello");
            Exception ex = null;
            Dictionary<ulong, bool> seqDict = new Dictionary<ulong, bool>();
            int count = 10;
            var ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    int subCount = 0;
                    // Test using here for unsubscribe
                    using (c.Subscribe("foo", (obj, args) =>
                    {
                        try
                        {
                            subCount++;

                            Assert.True(args.Message.Sequence > 0);
                            Assert.True(args.Message.Time > 0);
                            Assert.True(args.Message.Data != null);
                            var str = System.Text.Encoding.UTF8.GetString(args.Message.Data);
                            Assert.Equal("hello", str);

                            if (seqDict.ContainsKey(args.Message.Sequence))
                                throw new Exception("Duplicate Sequence found");

                            seqDict[args.Message.Sequence] = true;
                            if (subCount == count)
                                ev.Set();
                        }
                        catch (Exception e)
                        {
                            ex = e;
                        }
                    }))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            c.Publish("foo", payload);
                        }
                        Assert.True(ev.WaitOne(DEFAULT_WAIT));
                    }
                }
            }
            if (ex != null)
                throw ex;
        }

        [Fact]
        public void TestBasicQueuePubSub()
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello");
            Exception ex = null;
            Dictionary<ulong, bool> seqDict = new Dictionary<ulong, bool>();
            int count = 10;
            var ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    int subCount = 0;
                    // Test using here for unsubscribe
                    using (c.Subscribe("foo", "bar", (obj, args) =>
                    {
                        try
                        {
                            subCount++;

                            Assert.True(args.Message.Sequence > 0);
                            Assert.True(args.Message.Time > 0);
                            Assert.True(args.Message.Data != null);
                            var str = System.Text.Encoding.UTF8.GetString(args.Message.Data);
                            Assert.Equal("hello", str);

                            if (seqDict.ContainsKey(args.Message.Sequence))
                                throw new Exception("Duplicate Sequence found");

                            seqDict[args.Message.Sequence] = true;
                            if (subCount == count)
                                ev.Set();
                        }
                        catch (Exception e)
                        {
                            ex = e;
                        }
                    }))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            c.Publish("foo", payload);
                        }
                        Assert.True(ev.WaitOne(DEFAULT_WAIT));
                    }
                }
            }
            if (ex != null)
                throw ex;
        }

        [Fact]
        public void TestSubscriptionStartPositionLast()
        {
            int count = 10;
            Exception ex = null;

            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < count; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.StartWithLastReceived();
                    var sub = c.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        int val = BitConverter.ToInt32(args.Message.Data, 0);
                        if (args.Message.Sequence != (ulong)count)
                        {
                            ex = new Exception(
                                string.Format("Invalid sequence returned {0}",
                                args.Message.Sequence));
                        }
                        ev.Set();
                    });

                    sub.Unsubscribe();

                    ev.WaitOne(DEFAULT_WAIT);

                    if (ex != null)
                        throw ex;
                }
            }
        }

        [Fact]
        public void TestSubscriptionStartAtSequence()
        {
            int count = 10;
            long received = 0;
            long shouldReceive = 5;
            List<StanMsg> savedMsgs = new List<StanMsg>();

            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 1; i <= count; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        savedMsgs.Add(args.Message);
                        if (Interlocked.Increment(ref received) == shouldReceive)
                        {
                            ev.Set();
                        }
                    };

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.StartAt(6);
                    c.Subscribe("foo", sOpts, eh);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }
            }

            int seq = 5;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(BitConverter.ToInt32(m.Data, 0) == seq);
            }

            Assert.True(seq == count,
                string.Format("Received max seq {0}, expected max {1}",
                seq, count));
        }

        private void testSubscriptionStartAtTime(bool useUtc)
        {
            int count = 10;
            long received = 0;
            long shouldReceive = 5;
            List<StanMsg> savedMsgs = new List<StanMsg>();
            DateTime startTime;

            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    Thread.Sleep(500);
                    startTime = useUtc ? DateTime.UtcNow : DateTime.Now;
                    Thread.Sleep(500);

                    for (int i = 6; i <= 10; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        savedMsgs.Add(args.Message);
                        if (Interlocked.Increment(ref received) == shouldReceive)
                        {
                            ev.Set();
                        }
                    };

                    // check for illegal config
                    Thread.Sleep(500);

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.StartAt(startTime);
                    var s = c.Subscribe("foo", sOpts, eh);
                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }
            }

            int seq = 5;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(m.TimeStamp > startTime.ToUniversalTime(), $"Expected {m.TimeStamp} > {startTime}");
                Assert.True(BitConverter.ToInt32(m.Data, 0) == seq);
            }

            Assert.True(seq == count,
                string.Format("Received max seq {0}, expected max {1}",
                seq, count));
        }

        [Fact]
        public void TestSubscriptionStartAtTimeLocal()
        {
            testSubscriptionStartAtTime(false);
        }

        [Fact]
        public void TestSubscriptionStartAtTimeUtc()
        {
            testSubscriptionStartAtTime(true);
        }

        [Fact]
        public void TestSubscriptionStartAtTimeDelta()
        {
            int count = 10;
            long received = 0;
            long shouldReceive = 5;
            List<StanMsg> savedMsgs = new List<StanMsg>();
            DateTime startTime;

            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    Thread.Sleep(500);
                    startTime = DateTime.UtcNow;
                    Thread.Sleep(500);

                    for (int i = 6; i <= 10; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        savedMsgs.Add(args.Message);
                        if (Interlocked.Increment(ref received) == shouldReceive)
                        {
                            ev.Set();
                        }
                    };

                    // check for illegal config
                    Thread.Sleep(500);

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.StartAt(DateTime.UtcNow - startTime);
                    c.Subscribe("foo", sOpts, eh);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 20));
                }
            }

            int seq = 5;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(m.TimeStamp > startTime, $"Expected {m.TimeStamp} > {startTime}");
                Assert.True(BitConverter.ToInt32(m.Data, 0) == seq);
            }

            Assert.True(seq == count,
                string.Format("Received max seq {0}, expected max {1}",
                seq, count));
        }

        [Fact]
        public void TestSubscriptionStartAtWithEmptyStore()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var opts = StanSubscriptionOptions.GetDefaultOptions();

                    opts.StartAt(DateTime.Now);
                    c.Subscribe("foo", opts, noopMh).Unsubscribe();

                    opts.StartAt(0);
                    c.Subscribe("foo", opts, noopMh).Unsubscribe();


                    IStanSubscription s;
                    opts.StartWithLastReceived();
                    s = c.Subscribe("foo", opts, noopMh);
                    s.Unsubscribe();

                    // success
                    s = c.Subscribe("foo", noopMh);
                    s.Unsubscribe();
                }
            }
        }

        [Fact]
        public void TestSubscriptionStartAtFirst()
        {
            long received = 0;
            int count = 10;
            long shouldReceive = 10;
            List<StanMsg> savedMsgs = new List<StanMsg>();

            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 1; i <= count; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        savedMsgs.Add(args.Message);
                        if (Interlocked.Increment(ref received) == shouldReceive)
                        {
                            ev.Set();
                        }
                    };

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.DeliverAllAvailable();
                    c.Subscribe("foo", sOpts, eh);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }
            }

            int seq = 0;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(BitConverter.ToInt32(m.Data, 0) == seq);
            }

            Assert.True(seq == count,
                string.Format("Received max seq {0}, expected max {1}",
                seq, count));
        }

        [Fact]
        public void TestUnsubscribe()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    bool received = false;

                    EventHandler<StanMsgHandlerArgs> mh = (obj, args) =>
                    {
                        received = true;
                    };


                    // create a noop subscriber
                    c.Subscribe("foo", noopMh);

                    // success
                    var s = c.Subscribe("foo", mh);
                    s.Unsubscribe();
                    Assert.Throws<StanBadSubscriptionException>(() => s.Unsubscribe());

                    for (int i = 0; i < 10; i++)
                    {
                        c.Publish("foo", null);
                    }

                    Thread.Sleep(250);

                    Assert.False(received);
                }
            }
        }

        [Fact]
        public void TestUnsubscribeWhileConnClosing()
        {
            using (new NatsStreamingServer())
            {
                var cOpts = StanOptions.GetDefaultOptions();
                cOpts.PubAckWait = 50;
                using (var c = getStanConnection(opts: cOpts))
                {
                    AutoResetEvent ev = new AutoResetEvent(false);

                    var s = c.Subscribe("foo", noopMh);

                    new Task(() =>
                    {
                        Thread.Sleep(50);
                        c.Close();
                        ev.Set();
                    }).Start();

                    s.Unsubscribe();
                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }
            }
        }

        [Fact]
        public void TestSubscribeShrink()
        {
            int count = 1000;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    List<IStanSubscription> subs = new List<IStanSubscription>();

                    for (int i = 0; i < count; i++)
                    {
                        subs.Add(c.Subscribe("foo", noopMh));
                    }

                    foreach (var s in subs)
                    {
                        s.Unsubscribe();
                    }
                }
            }
        }

        [Fact]
        public void TestDupClientID()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    Assert.Throws<StanConnectRequestException>(() =>
                    {
                        using (getStanConnection()) {}
                    });
                }
            }
        }

        [Fact]
        public void TestClose()
        {
            bool received = false;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var s = c.Subscribe("foo", (obj, args) => { received = true; });

                    c.Close();

                    Assert.Throws<StanConnectionClosedException>(() => c.Publish("foo", null));
                    Assert.Throws<StanConnectionClosedException>(() => c.Publish("foo", null, (obj, args) => { /* noop */ }));
                    Assert.Throws<StanConnectionClosedException>(() => c.Subscribe("foo", noopMh));
                    Assert.Throws<StanConnectionClosedException>(() => c.Subscribe("foo", StanSubscriptionOptions.GetDefaultOptions(), noopMh));
                    Assert.Throws<StanConnectionClosedException>(() => c.Subscribe("foo", "bar", noopMh));
                    Assert.Throws<StanConnectionClosedException>(() => c.Subscribe("foo", "bar", StanSubscriptionOptions.GetDefaultOptions(), noopMh));
                }
            }

            Assert.False(received);
        }

        [Fact]
        public void TestCloseWithAcksInFlight()
        {
            bool okMessage = false;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    EventHandler<StanAckHandlerArgs> ah = (o, a) =>
                    {
                        if (a.Error != null)
                        {
                            okMessage = a.Error.Contains("Closed");
                        }
                        else
                        {
                            // Stack up the event handlers
                            Thread.Sleep(1000);
                        }
                    };

                    for (int i = 0; i < 25; i++)
                    {
                        c.Publish("foo", null, ah);
                    }

                    c.Close();
                }

                Assert.True(okMessage);
            }
        }

        [Fact]
        public void TestDoubleClose()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    c.Close();
                    c.Close();
                }
            }
        }

        [Fact]
        public void TestManualAck()
        {
            int toSend = 100;
            AutoResetEvent evAllReceived = new AutoResetEvent(false);
            AutoResetEvent evFirstSetReceived = new AutoResetEvent(false);
            Exception thrownEx = null;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    long nr;

                    for (int i = 0; i < toSend; i++)
                    {
                        c.Publish("foo", null);
                    }

                    // Test we get an exception manually acking an auto ack.
                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.DeliverAllAvailable();
                    sOpts.ManualAcks = true;
                    sOpts.MaxInflight = 10;

                    long received = 0;
                    thrownEx = null;
                    List<StanMsg> msgs = new List<StanMsg>();

                    evAllReceived.Reset();

                    var s = c.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        nr = Interlocked.Increment(ref received);

                        if (nr <= 10)
                        {
                            // ack these later
                            msgs.Add(args.Message);
                            if (nr == 10)
                            {
                                evFirstSetReceived.Set();
                            }
                        }
                        else if (nr > 10)
                        {
                            try
                            {
                                args.Message.Ack();
                            }
                            catch (Exception e)
                            {
                                thrownEx = e;
                            }

                            if (nr > toSend)
                            {
                                evAllReceived.Set();
                            }
                        }
                    });
                    Assert.True(evFirstSetReceived.WaitOne(DEFAULT_WAIT));
                    Assert.True(thrownEx == null);

                    // Wait a bit longer for other messages which would be an error.
                    Thread.Sleep(250);

                    Assert.True(Interlocked.Read(ref received) == 10);

                    // Now make sure we get the rest of them. So ack the ones we have so far.
                    foreach (var m in msgs)
                    {
                        m.Ack();
                    }

                    evAllReceived.WaitOne(DEFAULT_WAIT);

                    s.Unsubscribe();

                    nr = Interlocked.Read(ref received);
                    Assert.True(nr >= toSend, string.Format("Received: {0}, expected {1}", nr, toSend));
                }
            }
        }

        [Fact]
        public void TestManualAckInAutoAckMode()
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            Exception thrownEx = null;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {

                    var s = c.Subscribe("foo", (obj, args) =>
                    {
                        try
                        {
                            args.Message.Ack();
                        }
                        catch (Exception e)
                        {
                            thrownEx = e;
                            ev.Set();
                        }
                    });

                    c.Publish("foo", null);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                    s.Unsubscribe();
                    Assert.IsAssignableFrom<StanManualAckException>(thrownEx);
                }
            }
        }

        [Fact]
        public void TestRedelivery()
        {
            int toSend = 100;
            AutoResetEvent evAllReceived = new AutoResetEvent(false);
            AutoResetEvent evFirstSetReceived = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    long nr;

                    for (int i = 0; i < toSend; i++)
                    {
                        c.Publish("foo", null);
                    }

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    // make sure we get an error from an invalid Ack wait
                    Assert.Throws<ArgumentOutOfRangeException>(() => { sOpts.AckWait = 500; });

                    int ackRedeliverTime = 1000;

                    sOpts.DeliverAllAvailable();
                    sOpts.ManualAcks = true;
                    sOpts.MaxInflight = toSend + 1;
                    sOpts.AckWait = ackRedeliverTime;

                    long received = 0;

                    var s = c.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        nr = Interlocked.Increment(ref received);

                        if (nr == toSend)
                        {
                            evFirstSetReceived.Set();
                        }
                        else if (nr == 2 * toSend)
                        {
                            evAllReceived.Set();
                        }
                    });
                    Assert.True(evFirstSetReceived.WaitOne(DEFAULT_WAIT));
                    Assert.True(Interlocked.Read(ref received) == toSend);
                    Assert.True(evAllReceived.WaitOne(DEFAULT_WAIT));
                    Assert.True(Interlocked.Read(ref received) == 2 * toSend);
                }
            }
        }

        [Fact]
        public void TestRedeliveryHonorMaxInFlight()
        {
            int toSend = 100;
            bool redelivered = false;
            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < toSend; i++)
                    {
                        c.Publish("foo", null);
                    }

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    // make sure we get an error from an invalid Ack wait
                    Assert.Throws<ArgumentOutOfRangeException>(() => { sOpts.AckWait = 500; });

                    sOpts.DeliverAllAvailable();
                    sOpts.ManualAcks = true;
                    sOpts.MaxInflight = toSend;
                    sOpts.AckWait = 2000;

                    long received = 0;

                    var s = c.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        if (args.Message.Redelivered)
                            redelivered = true;

                        Interlocked.Increment(ref received);
                    });
                    Thread.Sleep(1000);
                    Assert.True(redelivered == false);
                    Assert.True(Interlocked.Read(ref received) == toSend);
                }
            }
        }

        private void checkTime(string label, DateTime time1, DateTime time2, int expectedMillis, int toleranceMillis)
        {
            if (time1 == DateTime.MinValue)
                throw new Exception("time1 was not set");
            if (time2 == DateTime.MinValue)
                throw new Exception("time2 was not set");

            TimeSpan expected = new TimeSpan(0, 0, 0, 0, expectedMillis);
            TimeSpan tolerance = new TimeSpan(0, 0, 0, 0, toleranceMillis);
            TimeSpan duration = time2 - time1;

            if (duration < (expected - tolerance))
                throw new Exception(string.Format("Duration {0} is below tolerance {1}.", duration, (expected-tolerance)));
            if (duration > (expected + tolerance))
                throw new Exception(string.Format("Duration {0} is above tolerance {1}.", duration, (expected+tolerance)));
        }

        private void testRedelivery(int count, bool useQueueSub)
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    int toSend = count;
                    AutoResetEvent ev = new AutoResetEvent(false);
                    long acked = 0;
                    bool secondRedelivery = false;
                    long firstDeliveryCount = 0;
                    long firstRedeliveryCount = 0;
                    DateTime startDelivery = DateTime.MinValue;
                    DateTime startFirstRedelivery = DateTime.MinValue;
                    DateTime startSecondRedelivery = DateTime.MinValue;

                    int ackRedeliveryTime = 1000;

                    EventHandler<StanMsgHandlerArgs> recvEh = (obj, args) =>
                    {
                        var m = args.Message;
                        if (m.Redelivered)
                        {
                            if (secondRedelivery)
                            {
                                if (startSecondRedelivery == DateTime.MinValue)
                                    startSecondRedelivery = DateTime.Now;

                                long acks = Interlocked.Increment(ref acked);
                                if (acks <= toSend)
                                {
                                    m.Ack();
                                    if (acks == toSend)
                                        ev.Set();
                                }
                            }
                            else
                            {
                                if (startFirstRedelivery == DateTime.MinValue)
                                    startFirstRedelivery = DateTime.Now;

                                if (Interlocked.Increment(ref firstRedeliveryCount) == toSend)
                                    secondRedelivery = true;
                            }
                        }
                        else
                        {
                            if (startDelivery == DateTime.MinValue)
                                startDelivery = DateTime.Now;

                            Interlocked.Increment(ref firstDeliveryCount);
                        }
                    };

                    IStanSubscription s = null;
                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.AckWait = ackRedeliveryTime;
                    sOpts.ManualAcks = true;

                    if (useQueueSub)
                    {
                        s = c.Subscribe("foo", "bar", sOpts, recvEh);
                    }
                    else
                    {
                        s = c.Subscribe("foo", sOpts, recvEh);
                    }

                    for (int i = 0; i < toSend; i++)
                    {
                        c.Publish("foo", null);
                    }

                    // If this succeeds, it means that we got all messages first delivered,
                    // and then at least 2 * toSend messages received as redelivered.
                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 10));

                    Thread.Sleep(ackRedeliveryTime + 100);

                    checkTime("First redelivery", startDelivery, startFirstRedelivery, ackRedeliveryTime, (int)(ackRedeliveryTime*.80));
                    checkTime("Second redelivery", startFirstRedelivery, startSecondRedelivery, ackRedeliveryTime, (int)(ackRedeliveryTime * .80));

                    Assert.True(Interlocked.Read(ref firstDeliveryCount) == toSend);
                    Assert.True(Interlocked.Read(ref firstRedeliveryCount) == toSend);
                    Assert.True(Interlocked.Read(ref acked) == toSend);
                }
            }
        }

        [Fact]
        public void TestLowRedeliveryToSubMoreThanOnce()
        {
            testRedelivery(10, false);
        }

        [Fact]
        public void TestHighRedeliveryToSubMoreThanOnce()
        {
            testRedelivery(20, false);
        }

        [Fact]
        public void TestLowRedeliveryToQueueSubMoreThanOnce()
        {
            testRedelivery(10, false);
        }

        [Fact]
        public void TestHighRedeliveryToQueueSubMoreThanOnce()
        {
            testRedelivery(20, false);
        }

        [Fact]
        public void TestDurableSubscriber()
        {
            int toSend = 100;
            long received = 0;
            List<StanMsg> savedMsgs = new List<StanMsg>();
            object msgGuard = new object();
            AutoResetEvent ev = new AutoResetEvent(false);

            var sOpts = StanSubscriptionOptions.GetDefaultOptions();
            sOpts.DeliverAllAvailable();
            sOpts.DurableName = "durable-foo";

            using (new NatsStreamingServer())
            {
                using (var c1 = getStanConnection())
                {
                    for (int i = 0; i < toSend; i++)
                    {
                        c1.Publish("foo", null);
                    }

                    var s = c1.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        var nr = Interlocked.Increment(ref received);
                        if (nr == 10)
                        {
                            Thread.Sleep(500);
                            c1.Close();
                            ev.Set();
                        }
                        else
                        {
                            lock (msgGuard)
                            {
                                savedMsgs.Add(args.Message);
                            }
                        }
                    });

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                    Assert.True(Interlocked.Read(ref received) == 10);
                }

                using (var c2 = getStanConnection())
                {
                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        lock (msgGuard)
                        {
                            savedMsgs.Add(args.Message);
                        }

                        if (Interlocked.Increment(ref received) == toSend)
                        {
                            ev.Set();
                        }

                    };
                    c2.Subscribe("foo", sOpts, eh);

                    // check for duplicate durable subscribes
                    Assert.Throws<StanException>(() => c2.Subscribe("foo", sOpts, eh));

                    // check that durables with the same name but different subject are OK.
                    c2.Subscribe("bar", sOpts, eh).Unsubscribe();

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));

                    // toSend+1 to count the unacked message after closing in the callback above.
                    Assert.True(Interlocked.Read(ref received) == toSend + 1);

                    lock (msgGuard)
                    {
                        Assert.True(savedMsgs.Count == toSend);
                        ulong seqExpected = 1;
                        foreach (var m in savedMsgs)
                        {
                            Assert.True(m.Sequence == seqExpected);
                            seqExpected++;
                        }
                    }
                }
            }
        }

        [Fact]
        public void TestPubMultiQueueSub()
        {
            AutoResetEvent ev = new AutoResetEvent(false);

            long received = 0;
            long s1Received = 0;
            long s2Received = 0;
            long toSend = 1000;

            bool unknownSubscription = false;
            bool detectedDuplicate = false;

            ConcurrentDictionary<ulong, bool> msgMap = new ConcurrentDictionary<ulong, bool>();
            IStanSubscription s1 = null, s2 = null;

            EventHandler<StanMsgHandlerArgs> mh = (obj, args) =>
            {
                if (msgMap.ContainsKey(args.Message.Sequence))
                    detectedDuplicate = true;

                msgMap[args.Message.Sequence] = true;

                if (args.Message.Subscription == s1)
                    Interlocked.Increment(ref s1Received);
                else if (args.Message.Subscription == s2)
                    Interlocked.Increment(ref s2Received);
                else
                    unknownSubscription = true;

                if (Interlocked.Increment(ref received) == toSend)
                    ev.Set();
            };

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    s1 = c.Subscribe("foo", "bar", mh);
                    s2 = c.Subscribe("foo", "bar", mh);

                    for (int i = 0; i < toSend; i++)
                        c.Publish("foo", null);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }

                Assert.False(unknownSubscription);
                Assert.False(detectedDuplicate);
                Assert.True(Interlocked.Read(ref received) == toSend);

                var s1r = Interlocked.Read(ref s1Received);
                var s2r = Interlocked.Read(ref s2Received);

                long v = (long)(toSend * 0.25);
                long expected = toSend / 2;

                var d1 = Math.Abs(expected - s1r);
                var d2 = Math.Abs(expected - s2r);
                Assert.True(d1 > v || d2 < v);
            }
        }

        [Fact]
        public void TestPubMultiQueueSubWithSlowSubscriber()
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            AutoResetEvent subBlock = new AutoResetEvent(false);

            long received = 0;
            long s1Received = 0;
            long s2Received = 0;
            long toSend = 1000;

            bool unknownSubscription = false;
            bool detectedDuplicate = false;

            ConcurrentDictionary<ulong, bool> msgMap = new ConcurrentDictionary<ulong, bool>();
            IStanSubscription s1 = null, s2 = null;

            EventHandler<StanMsgHandlerArgs> mh = (obj, args) =>
            {
                if (msgMap.ContainsKey(args.Message.Sequence))
                    detectedDuplicate = true;

                msgMap[args.Message.Sequence] = true;

                if (args.Message.Subscription == s1)
                {
                    Interlocked.Increment(ref s1Received);
                }
                else if (args.Message.Subscription == s2)
                {
                    // block this subscriber
                    Interlocked.Increment(ref s2Received);
                    subBlock.WaitOne(500);
                }
                else
                {
                    unknownSubscription = true;
                }

                if (Interlocked.Increment(ref received) == toSend)
                    ev.Set();
            };

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    s1 = c.Subscribe("foo", "bar", mh);
                    s2 = c.Subscribe("foo", "bar", mh);

                    for (int i = 0; i < toSend; i++)
                        c.Publish("foo", null);

                    subBlock.Set();
                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 2));

                    s1.Unsubscribe();
                    s2.Unsubscribe();
                }

                Assert.False(unknownSubscription);
                Assert.False(detectedDuplicate);
                Assert.True(Interlocked.Read(ref received) == toSend);

                var s1r = Interlocked.Read(ref s1Received);
                var s2r = Interlocked.Read(ref s2Received);

                // We have no guarantee that s2 received only 1 or 2 messages, but it should
                // not have received more than half
                Assert.True(s2r < (toSend / 2));
                Assert.True(s1r == (toSend - s2r));
            }
        }

        [Fact]
        public void TestPubMultiQueueSubWithRedelivery()
        {
            AutoResetEvent ev = new AutoResetEvent(false);

            long received = 0;
            long toSend = 50;

            IStanSubscription s1 = null, s2 = null;

            EventHandler<StanMsgHandlerArgs> mh = (obj, args) =>
            {
                if (args.Message.Subscription == s1)
                {
                    args.Message.Ack();
                    if (Interlocked.Increment(ref received) == toSend)
                        ev.Set();
                }
                // Do not ack s2
            };

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.ManualAcks = true;

                    s1 = c.Subscribe("foo", "bar", sOpts, mh);

                    sOpts.AckWait = 1000;
                    s2 = c.Subscribe("foo", "bar", sOpts, mh);

                    for (int i = 0; i < toSend; i++)
                        c.Publish("foo", null);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 2));

                    s1.Unsubscribe();
                    s2.Unsubscribe();
                }

                Assert.True(Interlocked.Read(ref received) == toSend);
            }
        }

        [Fact]
        public void TestPubMultiQueueSubWithDelayRedelivery()
        {
            AutoResetEvent ev = new AutoResetEvent(false);

            long ackcount = 0;
            long toSend = 100;

            IStanSubscription s1 = null, s2 = null;

            EventHandler<StanMsgHandlerArgs> mh = (obj, args) =>
            {
                if (args.Message.Subscription == s1)
                {
                    args.Message.Ack();

                    // if we've acked everything, signal
                    long nr = Interlocked.Increment(ref ackcount);
                    if (nr == toSend)
                        ev.Set();

                    if (nr > 0 && nr % (toSend / 2) == 0)
                    {
                        // This depends on the internal algorithm where the
                        // best resend subscriber is the one with the least number
                        // of outstanding acks.
                        //
                        // Sleep to allow the acks to back up, so s2 will look
                        // like a better subscriber to send messages to.
                        Thread.Sleep(200);
                    }
                }
                // Do not ack s2
            };

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.ManualAcks = true;

                    s1 = c.Subscribe("foo", "bar", sOpts, mh);

                    sOpts.AckWait = 1000;
                    s2 = c.Subscribe("foo", "bar", sOpts, mh);

                    for (int i = 0; i < toSend; i++)
                        c.Publish("foo", null);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 3));

                    s1.Unsubscribe();
                    s2.Unsubscribe();
                }

                Assert.True(Interlocked.Read(ref ackcount) == toSend);
            }
        }

        [Fact]
        public void TestRedeliveredFlag()
        {
            int toSend = 10;
            long received = 0;
            ConcurrentDictionary<ulong, StanMsg> msgMap = new ConcurrentDictionary<ulong, StanMsg>();
            object msgsLock = new object();
            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < toSend; i++)
                    {
                        c.Publish("foo", null);
                    }

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.DeliverAllAvailable();
                    sOpts.ManualAcks = true;
                    sOpts.AckWait = 1000;

                    var s = c.Subscribe("foo", sOpts, (obj, args) =>
                    {
                        var m = args.Message;
                        lock (msgsLock)
                        {
                            msgMap[m.Sequence] = m;
                        }

                        // only ack odd numbers
                        if (m.Sequence % 2 != 0)
                        {
                            m.Ack();
                        }

                        if (Interlocked.Increment(ref received) == toSend)
                            ev.Set();
                    });

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                    // wait for redelivery
                    Thread.Sleep(1500);
                }

                foreach (var m in msgMap.Values)
                {
                    Assert.True(m.Redelivered == (m.Sequence % 2 == 0));
                }

            }
        }

        // TestNoDuplicatesOnSubscriberStart tests that a subscriber does not
        // receive duplicate when requesting a replay while messages are being
        // published on it's subject.
        [Fact]
        public void TestNoDuplicatesOnSubscriberStart()
        {
            int batch = 100;
            AutoResetEvent pubBatch = new AutoResetEvent(false);
            AutoResetEvent ev = new AutoResetEvent(false);
            long received = 0;
            long sent = 0;

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                    {
                        if (Interlocked.Increment(ref received) == Interlocked.Read(ref sent))
                            ev.Set();
                    };

                    new Task(() =>
                    {
                        // publish until the receiver starts, then one additional batch.
                        // This primes NATS Streaming with messages, and gives us a point to stop
                        // when the subscriber has started processing messages.
                        while (Interlocked.Read(ref received) == 0)
                        {
                            for (int i = 0; i < batch; i++)
                            {
                                Interlocked.Increment(ref sent);
                                c.Publish("foo", null, (obj, args) => { });
                            }
                            pubBatch.Set();
                        }
                    }).Start();

                    pubBatch.WaitOne(DEFAULT_WAIT);

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.DeliverAllAvailable();
                    using (var s = c.Subscribe("foo", sOpts, eh))
                    {
                        ev.WaitOne(DEFAULT_WAIT);
                        // wait to see if any duplicate messages are sent
                        Thread.Sleep(250);
                    }
                }
                Assert.True(Interlocked.Read(ref received) == Interlocked.Read(ref sent));
            }
        }

        [Fact]
        public void TestMaxChannels()
        {
            using (new NatsStreamingServer(" -mc 5"))
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < 5; i++)
                    {
                        c.Publish(string.Format("chan-{0}", i), null);
                    }
                    Assert.Throws<StanException>(() => c.Publish("MAX_CHAN", null));
                }
            }
        }

        [Fact]
        public void TestRaceAckOnClose()
        {
            int toSend = 100;
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < toSend - 1; i++)
                    {
                        c.Publish("foo", null);
                    }

                    var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                    sOpts.ManualAcks = true;
                    sOpts.DeliverAllAvailable();
                    var s = c.Subscribe("foo", sOpts, (obj, args) => { args.Message.Ack(); });

                    Thread.Sleep(10);
                    c.Close();
                }
            }
        }

        [Fact]
        public void TestNatsConn()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var nc = c.NATSConnection;
                    Assert.True(nc.State == ConnState.CONNECTED);
                    nc.Close();

                    Assert.True(nc.IsClosed());
                    c.Close();
                    Assert.True(c.NATSConnection == null);
                }

                using (var nc2 = getNatsConnection())
                {
                    var opts = StanOptions.GetDefaultOptions();
                    opts.NatsConn = nc2;
                    using (var c2 = getStanConnection(opts: opts))
                    {
                        Assert.True(nc2 == c2.NATSConnection);
                        c2.Close();
                    }

                    Assert.True(nc2.IsClosed() == false);
                    nc2.Close();
                }
            }
        }

        [Fact(Skip = "HANGS SOMETIMES")]
        public void TestMaxPubAckInflight()
        {
            using (new NatsStreamingServer())
            {
                AutoResetEvent ev = new AutoResetEvent(false);
                var opts = StanOptions.GetDefaultOptions();
                opts.PubAckWait = 4000;
                opts.MaxPubAcksInFlight = 1;

                using (var c = getStanConnection(opts: opts))
                {
                    var sw = Stopwatch.StartNew();

                    c.Publish("foo", null, (obj, args) =>
                    {
                        // Block the ack handler for 2 seconds.  This should
                        // Block the following send for at least 2 seconds.
                        Thread.Sleep(2000);
                        ev.Set();
                    });

                    c.Publish("foo", null, (obj, args) => { });
                    Assert.True(ev.WaitOne(10000));
                    sw.Stop();
                    Assert.True(sw.ElapsedMilliseconds > 1000);
                }
            }
        }

        private async void testAsyncPublishAPI()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    var guid = await c.PublishAsync("foo", null);
                    Assert.False(string.IsNullOrWhiteSpace(guid));
                }
            }
        }

        [Fact]
        public void TestAsyncPublishAPI()
        {
            testAsyncPublishAPI();
        }

        private async void testAsyncPublishAPIParallel()
        {
            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    // make sure it simply works without blocking.
                    Stopwatch sw = Stopwatch.StartNew();
                    Task<string> t = c.PublishAsync("foo", null);
                    Thread.Sleep(500);
                    sw.Stop();
                    Assert.True(sw.ElapsedMilliseconds < 600);

                    sw.Restart();
                    var guid = await t;
                    sw.Stop();
                    Assert.False(sw.ElapsedMilliseconds > 100);
                    Assert.False(string.IsNullOrWhiteSpace(guid));
                }
            }
        }

        [Fact]
        public void TestAsyncPublishAPIParallel()
        {
            testAsyncPublishAPIParallel();
        }

        [Fact]
        public void TestAsyncPublishAPIMultiple()
        {
            testAsyncPublishAPIMultiple();
        }

        private void testAsyncPublishAPIMultiple()
        {
            List<Task<string>> pubs = new List<Task<string>>();

            using (new NatsStreamingServer())
            {
                using (var c = getStanConnection())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        pubs.Add(c.PublishAsync("foo", null));
                    }
                    Task.WaitAll(pubs.ToArray());
                }
            }
        }

        private void testSubscriberClose(string channel, bool useQG)
        {
            using (var sc = getStanConnection())
            {
                int received = 0;
                bool error = false;
                AutoResetEvent ev = new AutoResetEvent(false);

                // Send 1 message.
                sc.Publish(channel, System.Text.Encoding.UTF8.GetBytes("msg"));

                EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                {
                    ulong count = (ulong)Interlocked.Increment(ref received);
                    if (count != args.Message.Sequence)
                        error = true;

                    ev.Set();
                };

                StanSubscriptionOptions so = StanSubscriptionOptions.GetDefaultOptions();
                so.DeliverAllAvailable();
                so.DurableName = "dur";

                IStanSubscription sub;
                if (useQG)
                    sub = sc.Subscribe(channel, "group", so, eh);
                else
                    sub = sc.Subscribe(channel, so, eh);

                // wait for the first message
                Assert.True(ev.WaitOne(DEFAULT_WAIT));
                Assert.False(error, "invalid message seq received.");

                // Wait a bit to reduce risk of server processing unsubscribe before ACK
                Thread.Sleep(500);

                try
                {
                    sub.Close();
                }
                catch (StanNoServerSupport)
                {
                    // older server; just unsubscribe and 
                    // we are done.
                    sub.Unsubscribe();
                    return;
                }

                // send the second message
                sc.Publish(channel, System.Text.Encoding.UTF8.GetBytes("msg"));

                // restart the durable
                ev.Reset();
                if (useQG)
                    sub = sc.Subscribe(channel, "group", so, eh);
                else
                    sub = sc.Subscribe(channel, so, eh);

                // wait for the second message
                Assert.True(ev.WaitOne(10000));
                Assert.False(error, "invalid message seq received.");

                sub.Unsubscribe();
            }
        }

        [Fact]
        public void TestSubscriberClose()
        {
            using (new NatsStreamingServer())
            {
                StanOptions so = StanOptions.GetDefaultOptions();
                using (var c = getStanConnection())
                {
                    var sub = c.Subscribe("foo", (obj, args) => { });
                    try
                    {
                        sub.Close();
                    }
                    catch (StanNoServerSupport)
                    {
                        // noop, this is OK;
                    }

                    var qsub = c.Subscribe("foo", "group", (obj, args) => { });
                    try
                    {
                        qsub.Close();
                    }
                    catch (StanNoServerSupport)
                    {
                        // noop, this is OK;
                    }
                }

                testSubscriberClose("dursub", false);
                testSubscriberClose("durqueuesub", true);
            }
        }

        private void TestPingIntervalFail(int value)
        {
            var opts = StanOptions.GetDefaultOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => { opts.PingInterval = value; });
        }

        private void TestPingMaxOutFail(int value)
        {
            var opts = StanOptions.GetDefaultOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => { opts.PingMaxOutstanding = value; });
        }

        [Fact]
        public void TestPingParameters()
        {
            using (new NatsStreamingServer())
            {
                TestPingIntervalFail(-1);
                TestPingIntervalFail(0);
                TestPingMaxOutFail(-1);
                TestPingMaxOutFail(0);
                TestPingMaxOutFail(1);
            }
        }

        [Fact]
        public void TestPingsNatsConnGone()
        {
            using (new NatsStreamingServer())
            {
                int count = 0;
                int pingIvl = 1000;
                var exceeded = new AutoResetEvent(false);
                using (var nc = getNatsConnection())
                {
                    nc.SubscribeAsync(StanConsts.DefaultDiscoverPrefix + "." + CLUSTER_ID + ".pings", (obj, args) =>
                    {
                        count++;
                        if (count > StanConsts.DefaultPingMaxOut)
                        {
                            exceeded.Set();
                        }
                    });
                    nc.Flush();

                    var connLostEvent = new AutoResetEvent(false);
                    var opts = StanOptions.GetDefaultOptions();
                    opts.NatsConn = nc;
                    opts.PingInterval = pingIvl;
                    opts.ConnectionLostEventHandler = (obj, args) => { connLostEvent.Set(); };

                    using (getStanConnection(opts: opts))
                    {
                        // wait for pings, give us an extra ping just in case.
                        Assert.True(exceeded.WaitOne(60000 + pingIvl * (StanConsts.DefaultPingMaxOut + 2)));

                        // Close the NATS connection, wait for the error handler to fire (with 10s of slack).
                        nc.Close();
                        Assert.True(connLostEvent.WaitOne(120000 + (pingIvl * StanConsts.DefaultPingMaxOut)));
                    }
                }
            }
        }

        [Fact]
        public void TestPingStreamingServerGone()
        {
            using (new NatsServer())
            {
                using (var nss = new NatsStreamingServer(" -ns nats://127.0.0.1:4222"))
                {
                    AutoResetEvent ev = new AutoResetEvent(false);

                    StanOptions so = StanOptions.GetDefaultOptions();
                    so.PingInterval = 200;
                    so.PingMaxOutstanding = 3;
                    so.ConnectionLostEventHandler = (obj, args) =>
                    {
                        ev.Set();
                    };

                    using (var sc = getStanConnection(opts: so))
                    {
                        nss.Shutdown();
                        Assert.True(ev.WaitOne(20000));
                    }
                }
            }
        }

        [Fact]
        public void TestConnErrHandlerNotCalledOnNormalClose()
        {
            using (new NatsStreamingServer())
            {
                var ev = new AutoResetEvent(false);
                var so = StanOptions.GetDefaultOptions();
                so.PingInterval = 100;
                so.PingMaxOutstanding = 3;
                so.ConnectionLostEventHandler = (obj, args) =>
                {
                    ev.Set();
                };

                using(var sc = getStanConnection(opts: so))
                    sc.Close();

                // ensure handler is not called
                Assert.False(ev.WaitOne(2000));
            }
        }

        // This method connects to two servers, and attempts to send
        // messages through them for a number of iterations over a timeout.
        // If a message has been received, we know there is connectivity
        // (a route) between the url1 and url2 server endpoints.
        private bool waitForRoute(string url1, string url2, int timeout)
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            bool routeEstablished = false;

            // create conn 1
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.AllowReconnect = false;
            opts.Url = url1;
            using (var nc1 = getNatsConnection(opts))
            {

                // create conn 2, wait for a message
                opts.Url = url2;
                using (var nc2 = getNatsConnection(opts))
                {
                    nc2.SubscribeAsync("routecheck", (obj, args) => { ev.Set(); });
                    nc2.Flush();

                    for (int i = 0; i < 10 && routeEstablished == false; i++)
                    {
                        nc1.Publish("routecheck", null);
                        nc1.Flush();
                        routeEstablished = ev.WaitOne(timeout / 10);
                    }

                    nc1.Close();
                    nc2.Close();
                }
            }

            return routeEstablished;
        }

        // This will test a ping response error, with the error
        // being that a client has been replaced.
        //
        // 1) Cluster the embedded NATS server in the streaming server
        //    with an external CORE nats server but do not advertise so
        //    core NATS clients will only reconnect to the server they are
        //    configured with.
        // 2) Create a STAN client on the external server
        // 3) Kill the external server.  The streaming server knows of the client,
        //    who will attempt to reconnect to the killed server, effectively 
        //    "pausing" the client.
        // 4) Connect another client with the same ID to the running embedded 
        //    NATS server.
        // 5) Restart the external server.  The original client will reconnect, and
        //    we check that it gets a ping response that it has been replaced.
        [Fact]
        public void TestPingResponseError()
        {
            IStanConnection sc1;
            string errStr = "";

            var ev = new AutoResetEvent(false);

            // Create a NATS streaming server with an embedded NATS server
            // clustered with an external NATS server.
            string s1Args = " -p 4222 -cluster \"nats://127.0.0.1:6222\" -routes \"nats://127.0.0.1:6333\" --no_advertise=true";
            string s2Args = " -p 4333 -cluster \"nats://127.0.0.1:6333\" -routes \"nats://127.0.0.1:6222\" --no_advertise=true";
            using (new NatsStreamingServer(s1Args))
            {
                using (new NatsServer(s2Args))
                {
                    Assert.True(waitForRoute("nats://127.0.0.1:4222", "nats://127.0.0.1:4333", 30000), 
                        "Route was not established.");

                    // Connect to the routed NATS server, and set ping values
                    // to speed up the test and be resilient to slow CI instances.
                    var so = StanOptions.GetDefaultOptions();
                    so.NatsURL = "nats://127.0.0.1:4333";
                    so.PingInterval = 1000;
                    so.PingMaxOutstanding = 120;
                    so.ConnectionLostEventHandler = (obj, args) =>
                    {
                        errStr = args.ConnectionException.Message;
                        ev.Set();
                    };

                    sc1 = getStanConnection(opts: so);
                    sc1.Publish("foo", null);

                    // Falling out of this block will stop the server
                }

                // Now the NATS server is down and the internal NATS connection in sc1
                // is attempting to reconnect.  It can't find the streaming server's embedded
                // server in the cluster because the servers do not advertise.
                //
                // Create a new connection to the streaming server's embedded NATS server,
                // and publish.  This replaces the sc1 client.
                using(var c = getStanConnection())
                    c.Publish("foo", null);

                // now restart the clustered NATS server and let the client reconnect.  Eventually, the
                // nats connection in sc1 reconnects, and we get a client replaced message.
                using (new NatsServer(s2Args))
                {
                    // ensure handler on the first conn is called
                    Assert.True(ev.WaitOne(30000));
                    Assert.Contains("replaced", errStr);
                }
            }
        }

        // See TestPingResponseError above for general structure, except here we 
        // test for errors in publish.
        [Fact]
        public void TestPubFailsOnClientReplaced()
        {
            IStanConnection sc1;

            var ev = new AutoResetEvent(false);

            // Create a NATS streaming server with an embedded NATS server
            // clustered with an external NATS server.
            string s1Args = " -p 4222 -m 8222 -cluster \"nats://127.0.0.1:6222\" -routes \"nats://127.0.0.1:6333\" --no_advertise=true";
            string s2Args = " -p 4333 -m 8333 -cluster \"nats://127.0.0.1:6333\" -routes \"nats://127.0.0.1:6222\" --no_advertise=true";
            using (new NatsStreamingServer(s1Args))
            {
                using (new NatsServer(s2Args))
                {
                    Assert.True(waitForRoute("nats://127.0.0.1:4222", "nats://127.0.0.1:4333", 30000),
                        "Route was not established.");
                    // Connect to the routed NATS server, and set ping values
                    // to speed up the test and be resilient to slow CI instances.
                    var no = ConnectionFactory.GetDefaultOptions();
                    no.Url = "nats://127.0.0.1:4333";
                    no.MaxReconnect = Options.ReconnectForever;
                    no.ReconnectWait = 250;
                    no.ReconnectedEventHandler = (obj, args) =>
                    {
                        ev.Set();
                    };

                    var so = StanOptions.GetDefaultOptions();
                    so.NatsConn = getNatsConnection(no);
                    sc1 = getStanConnection(opts: so);
                    sc1.Publish("foo", null);
                    // Falling out of this block will stop the server
                }

                // Now the NATS server is down and the internal NATS connection in sc1
                // is attempting to reconnect.  It can't find the streaming server's embedded
                // server in the cluster because the servers do not advertise.
                //
                // Create a new connection to the streaming server's embedded NATS server,
                // and publish.  This replaces the sc1 client.
                using(var c = getStanConnection())
                    c.Publish("foo", null);

                // now restart the clustered NATS server and let the client reconnect.  Eventually, the
                // nats connection in sc1 reconnects, and we check for an error on publish.
                using (new NatsServer(s2Args))
                {
                    // wait until we are reconnected
                    Assert.True(ev.WaitOne(30000));
                    Assert.Throws<StanException>(() => sc1.Publish("foo", null));
                }
            }
        }

        [Fact]
        public void TestPingCloseUnlockPubCalls()
        {
            // FIXME - this seems to take too long... no deadlock, but unecessary blocking?
            using (new NatsServer())
            {
                string nssArgs = " -ns tcp://127.0.0.1:4222";
                using (var nss = new NatsStreamingServer(nssArgs))
                {
                    var ev = new AutoResetEvent(false);
                    //var so = StanOptions.GetDefaultOptions();
                    //so.PingInterval = 50;
                    //so.PingMaxOutstanding = 10;
                    //so.PubAckWait = 100;

                    using (var sc = getStanConnection())
                    {
                        int total = 10;
                        long count = 0;
                        EventHandler<StanAckHandlerArgs> ah = (obj, args) =>
                        {
                            if (Interlocked.Increment(ref count) == (total / 2) - 1)
                            {
                                ev.Set();
                            }
                        };

                        nss.Shutdown();

                        List<Task<string>> pubs = new List<Task<string>>();
                        for (int i = 0; i < total / 2; i++)
                        {
                            pubs.Add(Task.Run<string>(() => sc.Publish("foo", null, ah)));
                            pubs.Add(sc.PublishAsync("foo", null));
                        }

                        foreach (Task t in pubs)
                        {
                            try
                            {
                                t.Wait();
                            }
                            catch (Exception)
                            {
                                Interlocked.Increment(ref count);
                            }
                        }

                        int check = 0;
                        while (Interlocked.Read(ref count) != total && check < 40)
                        {
                            ev.WaitOne(500);
                            check++;
                        }

                        Assert.True(count == total);
                    }
                }
            }
        }

        /// <summary>
        /// Test methods exposed to facilitate user application unit testing.
        /// </summary>
        [Fact]
        public void TestUnitTestMethods()
        {
            var mhArgs = new StanMsgHandlerArgs(
                System.Text.Encoding.UTF8.GetBytes("N"),
                true, "foo", 10000, 999999, null);

            EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
            {
                var m = args.Message;
                Assert.True(m != null);
                Assert.True(m.Data[0] == (byte)'N');
                Assert.True(m.Redelivered == true);
                Assert.Equal("foo", m.Subject);
                Assert.Equal(10000, m.Time);
                Assert.True(999999 == m.Sequence);
            };
            eh(this, mhArgs);

            StanMsg msg = new StanMsg(
                System.Text.Encoding.UTF8.GetBytes("N"),
                true, "foo", 10000, 999999, null);
            Assert.True(msg != null);
            Assert.True(msg.Data[0] == (byte)'N');
            Assert.True(msg.Redelivered == true);
            Assert.Equal("foo", msg.Subject);
            Assert.Equal(10000, msg.Time);
            Assert.True(999999 == msg.Sequence);

            string guid = "abcdefg";
            string error = "stan: invalid subject";

            var ahArgs = new StanAckHandlerArgs(guid, error);
            EventHandler<StanAckHandlerArgs> ah = (obj, args) =>
            {
                Assert.Equal(error, args.Error);
                Assert.Equal(guid, args.GUID);
            };
            ah(this, ahArgs);

            var clArgs = new StanConnLostHandlerArgs(null, new Exception("error"));
            EventHandler<StanConnLostHandlerArgs> sclh = (obj, args) =>
            {
                Assert.Equal("error", args.ConnectionException.Message);
            };
            sclh(this, clArgs);
        }

        [Fact]
        public void TestMultipleNatsUrl()
        {

            using (new NatsStreamingServer())
            {
                var opts = StanOptions.GetDefaultOptions();
                opts.NatsURL = "nats://127.0.0.1:4222,nats://127.0.0.1:4222";
                opts.ConnectTimeout = 5000;
                using (var c = getStanConnection(opts: opts))
                {
                    c.Close();
                }
            }
        }

        [Fact]
        public void TestPublishReconnectDeadlock()
        {
            IStanConnection stanConn;
            IConnection natsConn;
            AutoResetEvent disconnected = new AutoResetEvent(false);
            AutoResetEvent reconnected = new AutoResetEvent(false);

            using (var server = new NatsStreamingServer("-st FILE -dir ."))
            {
                var nOpts = ConnectionFactory.GetDefaultOptions();
                nOpts.Url = "nats://127.0.0.1:4222";
                nOpts.MaxReconnect = Options.ReconnectForever;
                nOpts.ReconnectedEventHandler = (obj, args) =>
                {
                    reconnected.Set();
                };

                nOpts.DisconnectedEventHandler = (obj, args) =>
                {
                    disconnected.Set();
                };


                var opts = StanOptions.GetDefaultOptions();
                opts.NatsConn = natsConn = getNatsConnection(nOpts);

                // make sure we don't time out on pings if this takes awhile.
                opts.PingInterval = 60000;
                opts.PingMaxOutstanding = 10;

                // timeout faster on the publish acks.
                opts.PubAckWait = 250;

                // connect and publish one message.
                stanConn = getStanConnection(opts: opts);
                stanConn.Publish("foo", null);
            }
            // server will shutdown here.

            // Wait until we're disconnected.
            Assert.True(disconnected.WaitOne(10000));

            // ensure we can't publish messages with any of the expected
            // connections and won't deadlock.
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    stanConn.Publish("foo", null);
                }
                catch (StanTimeoutException) { }
                catch (StanConnectionClosedException) { }
                catch (StanConnectionException) { }
            }

            // start the server again...
            using (var server = new NatsStreamingServer("-st FILE -dir ."))
            {
                // Wait until we're reconnected.
                Assert.True(reconnected.WaitOne(10000));

                // ensure we can publish a few messages.
                for (int i = 0; i < 4; i++)
                {
                    stanConn.Publish("foo", null);
                }
            }

            natsConn.Close();
            stanConn.Close();
            natsConn.Dispose();
            stanConn.Dispose();
        }


        [Fact]
        public void TestPublishReconnectDeadlockThreaded()
        {
            IStanConnection stanConn;
            IConnection natsConn;
            AutoResetEvent publishOK = new AutoResetEvent(false);
            AutoResetEvent publishFail = new AutoResetEvent(false);
            Task pubTask = null;

            using (var server = new NatsStreamingServer("-st FILE -dir ."))
            {
                var nOpts = ConnectionFactory.GetDefaultOptions();
                nOpts.Url = "nats://127.0.0.1:4222";
                nOpts.MaxReconnect = Options.ReconnectForever;
                var opts = StanOptions.GetDefaultOptions();
                opts.NatsConn = natsConn = getNatsConnection(nOpts);

                // make sure we don't time out on pings if this takes awhile.
                opts.PingInterval = 60000;
                opts.PingMaxOutstanding = 10;

                // timeout faster on the publish acks.
                opts.PubAckWait = 250;

                // connect and publish one message.
                stanConn = getStanConnection(opts: opts);

                long finished = 0;
                pubTask = new Task(() =>
                {
                    while (Interlocked.Read(ref finished) == 0)
                    {
                        try
                        {
                            stanConn.Publish("foo", null);
                            publishOK.Set();
                        }
                        catch
                        {
                            /// Either a timeout or connection exception....
                            publishFail.Set();
                        }
                        Thread.Sleep(50);

                    }
                });
                pubTask.Start();

                // Make sure we have published
                Assert.True(publishOK.WaitOne(10000));

            }
            // server will shutdown here.

            // Wait until we've failed publishing...
            Assert.True(publishFail.WaitOne(10000));

            // start the server again...
            using (var server = new NatsStreamingServer("-st FILE -dir ."))
            {
                // Make sure we can publish again.
                publishOK.Reset();
                Assert.True(publishOK.WaitOne(10000));

                // Make sure we're done with failures.
                publishFail.Reset();
                Assert.False(publishFail.WaitOne(500));
            }

            natsConn.Close();
            stanConn.Close();

            natsConn.Dispose();
            stanConn.Dispose();
        }
    }
}
