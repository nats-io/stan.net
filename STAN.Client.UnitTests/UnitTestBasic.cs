/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;
using Xunit;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using NATS.Client;
using System.Diagnostics;

namespace STAN.Client.UnitTests
{
    public class UnitTestBasic
    {
        static readonly int DEFAULT_WAIT = 10000;
        static readonly string CLUSTER_ID = "test-cluster";
        static readonly string CLIENT_ID = "me";

        EventHandler<StanMsgHandlerArgs> noopMh = (obj, args) => { /* NOOP */ };

        internal IStanConnection DefaultConnection
        {
            get
            {
                return new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID);
            }
        }

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
            Assert.Throws<StanConnectionException>(() => DefaultConnection);
        }

        [Fact]
        public void TestUnreachable()
        {
            using (new NatsStreamingServer())
            {
                Assert.Throws<StanConnectRequestTimeoutException>(
                    () => new StanConnectionFactory().CreateConnection("invalid", CLIENT_ID));
            }
        }

        [Fact]
        public void TestNatsConnNotClosedOnClose()
        {
            using (new NatsStreamingServer())
            {
                using (IConnection nc = new ConnectionFactory().CreateConnection())
                {
                    var opts = StanOptions.GetDefaultOptions();
                    opts.NatsConn = nc;
                    IStanConnection sc = new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID, opts);
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
                var c = DefaultConnection;
                c.Close();
            }
        }

        [Fact]
        public void TestBasicPublish()
        {
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
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

                AutoResetEvent ev = new AutoResetEvent(true);

                using (var c = new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID, opts))
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
                using (var c = DefaultConnection)
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
                Assert.True(pubGuid.Equals(cbGuid));
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
                using (var c = DefaultConnection)
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
                Assert.True(pubGuid.Equals(cbGuid));
            }
        }

        [Fact]
        public void TestBasicSubscription()
        {
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                            Assert.True("hello".Equals(str));

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
                using (var c = DefaultConnection)
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
                            Assert.True("hello".Equals(str));

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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    Thread.Sleep(500);
                    startTime = DateTime.Now;
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
                    sOpts.StartAt(useUtc ? startTime.ToUniversalTime() : startTime);
                    var s = c.Subscribe("foo", sOpts, eh);
                    Assert.True(ev.WaitOne(DEFAULT_WAIT));
                }
            }

            int seq = 5;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(m.TimeStamp > startTime);
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
                using (var c = DefaultConnection)
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        byte[] payload = BitConverter.GetBytes(i);
                        c.Publish("foo", payload);
                    }

                    Thread.Sleep(500);
                    startTime = DateTime.Now;
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
                    sOpts.StartAt(DateTime.Now - startTime);
                    c.Subscribe("foo", sOpts, eh);

                    Assert.True(ev.WaitOne(DEFAULT_WAIT * 20));
                }
            }

            int seq = 5;
            foreach (StanMsg m in savedMsgs)
            {
                seq++;
                Assert.True(m.Sequence == (ulong)seq);
                Assert.True(m.TimeStamp > startTime);
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID, cOpts))
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
                {
                    Assert.Throws<StanConnectRequestException>(() => DefaultConnection);
                }
            }
        }

        [Fact]
        public void TestClose()
        {
            bool received = false;

            using (new NatsStreamingServer())
            {
                var c = DefaultConnection;
                var s = c.Subscribe("foo", (obj, args) =>
                {
                    received = true;
                });

                c.Close();

                Assert.Throws<StanConnectionClosedException>(()=> c.Publish("foo", null));
                Assert.Throws<StanConnectionClosedException>(()=> c.Publish("foo", null, (obj, args)=> {/* noop */}));
                Assert.Throws<StanConnectionClosedException>(()=> c.Subscribe("foo", noopMh));
                Assert.Throws<StanConnectionClosedException>(()=> c.Subscribe("foo", StanSubscriptionOptions.GetDefaultOptions(), noopMh));
                Assert.Throws<StanConnectionClosedException>(()=> c.Subscribe("foo", "bar", noopMh));
                Assert.Throws<StanConnectionClosedException>(()=> c.Subscribe("foo", "bar", StanSubscriptionOptions.GetDefaultOptions(), noopMh));
            }

            Assert.False(received);
        }

        [Fact]
        public void TestDoubleClose()
        {
            using (new NatsStreamingServer())
            {
                var c = DefaultConnection;
                c.Close();
                c.Close();
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                throw new Exception("Duration is below tolerance.");
            if (duration > (expected + tolerance))
                throw new Exception("Duration is above tolerance.");
        }

        private void testRedelivery(int count, bool useQueueSub)
        {
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
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

                    checkTime("First redelivery", startDelivery, startFirstRedelivery, ackRedeliveryTime, (ackRedeliveryTime) / 2);
                    checkTime("Second redelivery", startFirstRedelivery, startSecondRedelivery, ackRedeliveryTime, (ackRedeliveryTime) / 2);

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
            testRedelivery(100, false);
        }

        [Fact]
        public void TestLowRedeliveryToQueueSubMoreThanOnce()
        {
            testRedelivery(10, false);
        }

        [Fact]
        public void TestHighRedeliveryToQueueSubMoreThanOnce()
        {
            testRedelivery(100, false);
        }

        [Fact]
        public void TestDurableSubscriber()
        {
            int toSend = 100;
            long received = 0;
            List<StanMsg> savedMsgs = new List<StanMsg>();
            object msgGuard = new object();
            AutoResetEvent ev = new AutoResetEvent(false);

            using (new NatsStreamingServer())
            {
                var c = DefaultConnection;

                for (int i = 0; i < toSend; i++)
                {
                    c.Publish("foo", null);
                }

                var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                sOpts.DeliverAllAvailable();
                sOpts.DurableName = "durable-foo";

                var s = c.Subscribe("foo", sOpts, (obj, args) =>
                {
                    var nr = Interlocked.Increment(ref received);
                    if (nr == 10)
                    {
                        Thread.Sleep(500);
                        c.Close();
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

                using (c = DefaultConnection)
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
                    c.Subscribe("foo", sOpts, eh);

                    // check for duplicate durable subscribes
                    Assert.Throws<StanException>(() => c.Subscribe("foo", sOpts, eh));

                    // check that durables with the same name but different subject are OK.
                    c.Subscribe("bar", sOpts, eh).Unsubscribe();

                    Assert.True(ev.WaitOne(DEFAULT_WAIT));

                    // toSend+1 to count the unacked message after closing in the callback above.
                    Assert.True(Interlocked.Read(ref received) == toSend+1);
                    
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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

                    if (nr > 0 && nr % (toSend/2) == 0)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
            // From the server default
            int maxChannels = 100;
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
                {
                    for (int i = 0; i < maxChannels; i++)
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
                var c = DefaultConnection;

                for (int i = 0; i < toSend - 1; i++)
                {
                    c.Publish("foo", null);
                }

                var sOpts = StanSubscriptionOptions.GetDefaultOptions();
                sOpts.ManualAcks = true;
                sOpts.DeliverAllAvailable();
                var s = c.Subscribe("foo", sOpts, (obj, args) =>
                {
                    args.Message.Ack();
                });

                Thread.Sleep(10);
                c.Close();
            }
        }

        [Fact]
        public void TestNatsConn()
        {
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
                {
                    var nc = c.NATSConnection;
                    Assert.True(nc.State == ConnState.CONNECTED);
                    nc.Close();

                    Assert.True(nc.IsClosed());
                    c.Close();
                    Assert.True(c.NATSConnection == null);
                }

                var nc2 = new ConnectionFactory().CreateConnection();

                var opts = StanOptions.GetDefaultOptions();
                opts.NatsConn = nc2;
                var c2 = new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID, opts);
                Assert.True(nc2 == c2.NATSConnection);
                c2.Close();

                Assert.True(nc2.IsClosed() == false);
                nc2.Close();
            }

        }

        [Fact]
        public void TestMaxPubAckInflight()
        {
            using (new NatsStreamingServer())
            {
                AutoResetEvent ev = new AutoResetEvent(false);
                var opts = StanOptions.GetDefaultOptions();
                opts.PubAckWait = 4000;
                opts.MaxPubAcksInFlight = 1;

                using (var c = new StanConnectionFactory().CreateConnection(
                    CLUSTER_ID, CLIENT_ID, opts))
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
                    ev.WaitOne();

                    Assert.True(sw.ElapsedMilliseconds  > 1000);
                }
            }
        }

        private async void testAsyncPublishAPI()
        {
            using (new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
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
            using (var s = new NatsStreamingServer())
            {
                using (var c = DefaultConnection)
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
                using (var c = DefaultConnection)
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
            using (var sc = DefaultConnection)
            {
                int received = 0;
                bool error = false;
                AutoResetEvent ev = new AutoResetEvent(false);

                // Send 1 message.
                sc.Publish(channel, System.Text.Encoding.UTF8.GetBytes("msg"));

                EventHandler<StanMsgHandlerArgs> eh = (obj, args) =>
                {
                    ulong count = (ulong)Interlocked.Increment(ref received);
                    Console.WriteLine("Received {0}, seqno={1}", count, args.Message.Sequence);
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
                using (var c = DefaultConnection)
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
    }
}
