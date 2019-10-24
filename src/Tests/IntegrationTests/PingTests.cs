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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using STAN.Client;
using Xunit;

namespace IntegrationTests
{
    public class PingTests : TestSuite<PingTestsContext>
    {
        public PingTests(PingTestsContext context) : base(context) { }

        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        private void TestPingIntervalFail(int value)
        {
            var opts = StanOptions.GetDefaultOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => { opts.PingInterval = value; });
        }

        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        private void TestPingMaxOutFail(int value)
        {
            var opts = StanOptions.GetDefaultOptions();
            Assert.Throws<ArgumentOutOfRangeException>(() => { opts.PingMaxOutstanding = value; });
        }

        [Fact]
        public void TestPingParameters()
        {
            using (Context.StartStreamingServerWithEmbedded(Context.Server1))
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
            using (Context.StartStreamingServerWithEmbedded(Context.Server1))
            {
                int count = 0;
                int pingIvl = 1000;
                var exceeded = new AutoResetEvent(false);
                using (var nc = Context.GetNatsConnection(Context.Server1))
                {
                    nc.SubscribeAsync(StanConsts.DefaultDiscoverPrefix + "." + Context.ClusterId + ".pings", (obj, args) =>
                    {
                        count++;
                        if (count > StanConsts.DefaultPingMaxOut)
                        {
                            exceeded.Set();
                        }
                    });
                    nc.Flush();

                    var connLostEvent = new AutoResetEvent(false);
                    var opts = Context.GetStanTestOptions(Context.Server1);
                    opts.NatsConn = nc;
                    opts.PingInterval = pingIvl;
                    opts.ConnectionLostEventHandler = (obj, args) => { connLostEvent.Set(); };

                    using (Context.GetStanConnection(opts: opts))
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
            using (Context.StartNatsServer(Context.Server1))
            {
                using (var nss = Context.StartStreamingServerWithExternal(Context.Server1))
                {
                    AutoResetEvent ev = new AutoResetEvent(false);

                    var so = Context.GetStanTestOptions(Context.Server1);
                    so.PingInterval = 200;
                    so.PingMaxOutstanding = 3;
                    so.ConnectionLostEventHandler = (obj, args) =>
                    {
                        ev.Set();
                    };

                    using (var sc = Context.GetStanConnection(so))
                    {
                        nss.Shutdown();
                        Assert.True(ev.WaitOne(20000));
                    }
                }
            }
        }

        [Fact]
        public void TestPingCloseUnlockPubCalls()
        {
            // FIXME - this seems to take too long... no deadlock, but unecessary blocking?
            using (Context.StartNatsServer(Context.Server1))
            {
                using (var nss = Context.StartStreamingServerWithExternal(Context.Server1))
                {
                    var ev = new AutoResetEvent(false);
                    //var so = StanOptions.GetDefaultOptions();
                    //so.PingInterval = 50;
                    //so.PingMaxOutstanding = 10;
                    //so.PubAckWait = 100;

                    using (var sc = Context.GetStanConnection(Context.Server1))
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
    }
}
