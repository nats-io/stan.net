// Copyright 2015-2019 The NATS Authors
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
    public class QueueSubOptionsExampleTest : TestSuite<BasicTestsContext>
    {
        public QueueSubOptionsExampleTest(BasicTestsContext context) : base(context) { }

        EventHandler<StanMsgHandlerArgs> noopMh = (obj, args) => { /* NOOP */ };

        static byte[] getPayload(string s)
        {
            return (s == null) ? null : System.Text.Encoding.UTF8.GetBytes(s);
        }

        [Fact]
        public void TestBasicQueuePubSubWithSubOptionsExpectFailDueToDurableName()
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello");
            Exception ex = null;
            ConcurrentDictionary<ulong, bool> seqDict = new ConcurrentDictionary<ulong, bool>();
            int count = 10;
            var ev = new AutoResetEvent(false);

            using (Context.StartStreamingServerWithEmbedded(Context.DefaultServer))
            {
                using (var c = Context.GetStanConnection(Context.DefaultServer, null, clientId: "client1"))
                using (var otherConnection = Context.GetStanConnection(Context.DefaultServer, null, clientId: "clientId2"))
                {
                    int subCount = 0;
                    
                    var subOptions = StanSubscriptionOptions.GetDefaultOptions();
                    subOptions.DurableName = "name1Durable"; //adding 'durable name' makes it duplicate sequence??? probably need to dig in carefully
                    var otherSubOptions = StanSubscriptionOptions.GetDefaultOptions();
                    otherSubOptions.DurableName = "name2Durable";
                    using (c.Subscribe("foo", "bar", subOptions, (obj, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref subCount);

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
                    using (otherConnection.Subscribe("foo", "bar", otherSubOptions, (obj, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref subCount);

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
                        Assert.True(ev.WaitOne(Context.DefaultWait));
                    }
                }
            }
            if (ex != null)
                throw ex;
        }

        [Fact]
        public void TestBasicQueuePubSubWithSubOptionsExpectPass()
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello");
            Exception ex = null;
            ConcurrentDictionary<ulong, bool> seqDict = new ConcurrentDictionary<ulong, bool>();
            int count = 10;
            var ev = new AutoResetEvent(false);

            using (Context.StartStreamingServerWithEmbedded(Context.DefaultServer))
            {
                using (var c = Context.GetStanConnection(Context.DefaultServer, null, clientId: "client1"))
                using (var otherConnection = Context.GetStanConnection(Context.DefaultServer, null, clientId: "clientId2"))
                {
                    int subCount = 0;
                    // Test using here for unsubscribe
                    var subOptions = StanSubscriptionOptions.GetDefaultOptions();
                    //subOptions.DurableName = "name1Durable";
                    var otherSubOptions = StanSubscriptionOptions.GetDefaultOptions();
                    //otherSubOptions.DurableName = "name2Durable";
                    using (c.Subscribe("foo", "bar", subOptions, (obj, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref subCount);

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
                    using (otherConnection.Subscribe("foo", "bar", otherSubOptions, (obj, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref subCount);

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
                        Assert.True(ev.WaitOne(Context.DefaultWait));
                    }
                }
            }
            if (ex != null)
                throw ex;
        }

    }
}
