using System.Threading;
using NATS.Client;
using STAN.Client;
using Xunit;

namespace IntegrationTests
{
    public class ClusterTests : TestSuite<ClusterTestsContext>
    {
        public ClusterTests(ClusterTestsContext context) : base(context) { }

        // This method connects to two servers, and attempts to send
        // messages through them for a number of iterations over a timeout.
        // If a message has been received, we know there is connectivity
        // (a route) between the url1 and url2 server endpoints.
        private bool waitForRoute(TestServerInfo serverInfo1, TestServerInfo serverInfo2, int timeout)
        {
            AutoResetEvent ev = new AutoResetEvent(false);
            bool routeEstablished = false;

            // create conn 1
            var opts1 = Context.GetNatsTestOptions(serverInfo1);
            opts1.AllowReconnect = false;

            using (var nc1 = Context.GetNatsConnection(opts1))
            {
                // create conn 2, wait for a message
                var opts2 = Context.GetNatsTestOptions(serverInfo2);
                opts1.AllowReconnect = false;

                using (var nc2 = Context.GetNatsConnection(opts2))
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
            string s1Args = $" -cluster \"{Context.ClusterServer1}\" -routes \"{Context.ClusterServer2}\" --no_advertise=true";
            string s2Args = $" -cluster \"{Context.ClusterServer2}\" -routes \"{Context.ClusterServer1}\" --no_advertise=true";
            using (Context.StartStreamingServerWithEmbedded(Context.Server1, s1Args))
            {
                using (Context.StartNatsServer(Context.Server2, s2Args))
                {
                    Assert.True(waitForRoute(Context.Server1, Context.Server2, 30000), "Route was not established.");

                    // Connect to the routed NATS server, and set ping values
                    // to speed up the test and be resilient to slow CI instances.
                    var so = Context.GetStanTestOptions(Context.Server2);
                    so.PingInterval = 1000;
                    so.PingMaxOutstanding = 120;
                    so.ConnectionLostEventHandler = (obj, args) =>
                    {
                        errStr = args.ConnectionException.Message;
                        ev.Set();
                    };

                    sc1 = Context.GetStanConnection(opts: so);
                    sc1.Publish("foo", null);

                    // Falling out of this block will stop the server
                }

                // Now the NATS server is down and the internal NATS connection in sc1
                // is attempting to reconnect.  It can't find the streaming server's embedded
                // server in the cluster because the servers do not advertise.
                //
                // Create a new connection to the streaming server's embedded NATS server,
                // and publish.  This replaces the sc1 client.
                using (var c = Context.GetStanConnection(Context.Server1))
                    c.Publish("foo", null);

                // now restart the clustered NATS server and let the client reconnect.  Eventually, the
                // nats connection in sc1 reconnects, and we get a client replaced message.
                using (Context.StartNatsServer(Context.Server2, s2Args))
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
            string s1Args = $" -p {Context.Server1.Port} -cluster \"{Context.ClusterServer1}\" -routes \"{Context.ClusterServer2}\" --no_advertise=true";
            string s2Args = $" -p {Context.Server2.Port} -cluster \"{Context.ClusterServer2}\" -routes \"{Context.ClusterServer1}\" --no_advertise=true";
            using (Context.StartStreamingServerWithEmbedded(Context.Server1, s1Args))
            {
                using (Context.StartNatsServer(Context.Server2, s2Args))
                {
                    Assert.True(waitForRoute(Context.Server1, Context.Server2, 30000), "Route was not established.");
                    // Connect to the routed NATS server, and set ping values
                    // to speed up the test and be resilient to slow CI instances.
                    var no = Context.GetNatsTestOptions(Context.Server1);
                    no.Url = Context.Server2.Url;
                    no.MaxReconnect = Options.ReconnectForever;
                    no.ReconnectWait = 250;
                    no.ReconnectedEventHandler = (obj, args) =>
                    {
                        ev.Set();
                    };

                    var so = Context.GetStanTestOptions(Context.Server1);
                    so.NatsConn = Context.GetNatsConnection(no);
                    sc1 = Context.GetStanConnection(opts: so);
                    sc1.Publish("foo", null);
                    // Falling out of this block will stop the server
                }

                // Now the NATS server is down and the internal NATS connection in sc1
                // is attempting to reconnect.  It can't find the streaming server's embedded
                // server in the cluster because the servers do not advertise.
                //
                // Create a new connection to the streaming server's embedded NATS server,
                // and publish.  This replaces the sc1 client.
                using (var c = Context.GetStanConnection(Context.Server1))
                    c.Publish("foo", null);

                // now restart the clustered NATS server and let the client reconnect.  Eventually, the
                // nats connection in sc1 reconnects, and we check for an error on publish.
                using (Context.StartNatsServer(Context.Server2, s2Args))
                {
                    // wait until we are reconnected
                    Assert.True(ev.WaitOne(30000));
                    Assert.Throws<StanException>(() => sc1.Publish("foo", null));
                }
            }
        }
    }
}