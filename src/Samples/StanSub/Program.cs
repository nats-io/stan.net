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
using System.Diagnostics;
using System.Threading;
using STAN.Client;

namespace StanSub
{
    class StanSubscriber
    {
        static readonly string usageText =
@"
Usage: stan-sub [options]
Options:
    -url < url >           NATS Streaming server URL(s)
    -cluster < cluster name > NATS Streaming cluster name
    -clientid < client ID >   NATS Streaming client ID
    -verbose                  Verbose mode (affects performance).

Subscription Options:
    -count < num >      # of msgs to receieve    
    -qgroup < name >    Queue group
    -subject <subject > Subscribe subject
    -seq < seqno >      Start at seqno
    -all                Deliver all available messages
    -last               Deliver starting with last published message
    -since < duration > Deliver messages in last interval(e.g. 1s, 1hr)
             (for more information: see .NET TimeSpan.Parse documentation)

    -maxinflight        Maximum # of acknowledgements in flight
    -durable < name >   Durable subscriber name
    -unsubscribe        Unsubscribe the durable on exit";

        Dictionary<string, string> parsedArgs = new Dictionary<string, string>();

        int count = 10000;
        string url = StanConsts.DefaultNatsURL;
        string subject = "foo";
        int received = 0;
        bool verbose = false;
        string clientID = "cs-subscriber";
        string clusterID = "test-cluster";
        string qGroup = null;
        bool unsubscribe = false;
        IStanConnection c = null;
        IStanSubscription s = null;

        StanSubscriptionOptions sOpts = StanSubscriptionOptions.GetDefaultOptions();
        StanOptions cOpts = StanOptions.GetDefaultOptions();

        public void Run(string[] args)
        {
            parseArgs(args);
            banner();

            var opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = url;

            Console.CancelKeyPress += controlCHandler;

            using (c = new StanConnectionFactory().CreateConnection(clusterID, clientID, opts))
            {
                TimeSpan elapsed = ReceiveMessages();
              
                Console.Write("Received {0} msgs in {1} seconds ", received, elapsed.TotalSeconds);
                Console.WriteLine("({0} msgs/second).",
                    (int)(received / elapsed.TotalSeconds));
            }
        }

        // Convenience method to create a queue or standard subscriber
        private IStanSubscription CreateSubscriber(IStanConnection c, EventHandler<StanMsgHandlerArgs> msgHandler)
        {
            if (qGroup != null)
            {
                return c.Subscribe(subject, qGroup, sOpts, msgHandler);
            }
            return c.Subscribe(subject, sOpts, msgHandler);
        }

        // Receive expected messages and return the elapsed time from first
        // message to last message.
        private TimeSpan ReceiveMessages()
        {
            Stopwatch sw = new Stopwatch();
            AutoResetEvent ev = new AutoResetEvent(false);

            EventHandler<StanMsgHandlerArgs> msgHandler = (sender, args) =>
            {
                if (received == 0)
                    sw.Start();

                received++;

                if (verbose)
                {
                    Console.WriteLine("Received seq # {0}",
                        args.Message.Sequence);
                }

                if (received >= count)
                {
                    sw.Stop();
                    ev.Set();
                }
            };

            using (s = CreateSubscriber(c, msgHandler))
            {
                ev.WaitOne();
            }

            return sw.Elapsed;
        }

        private void usage()
        {
            Console.Error.WriteLine(usageText);
            Environment.Exit(-1);
        }

        private void parseArgs(string[] args)
        {
            if (args == null)
                return;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-verbose") ||
                    args[i].Equals("-all") ||
                    args[i].Equals("-last") ||
                    args[i].Equals("-unsubscribe"))
                {
                    parsedArgs.Add(args[i], "true");
                }
                else
                {
                    if (i + 1 == args.Length)
                        usage();

                    parsedArgs.Add(args[i], args[i + 1]);
                    i++;
                }
            }

            if (parsedArgs.ContainsKey("-clientid"))
                clientID = parsedArgs["-clientid"];

            if (parsedArgs.ContainsKey("-cluster"))
                clusterID = parsedArgs["-cluster"];

            if (parsedArgs.ContainsKey("-count"))
                count = Convert.ToInt32(parsedArgs["-count"]);

            if (parsedArgs.ContainsKey("-server"))
                url = parsedArgs["-server"];

            if (parsedArgs.ContainsKey("-subject"))
                subject = parsedArgs["-subject"];

            if (parsedArgs.ContainsKey("-qgroup"))
                qGroup = parsedArgs["-qgroup"];

            if (parsedArgs.ContainsKey("-seq"))
            {
                sOpts.StartAt(Convert.ToUInt64(parsedArgs["-seq"]));
            }

            if (parsedArgs.ContainsKey("-all"))
            {
                Console.WriteLine("Requesting all messages.");
                sOpts.DeliverAllAvailable();
            }

            if (parsedArgs.ContainsKey("-last"))
            {
                Console.WriteLine("Requesting last message.");
                sOpts.StartWithLastReceived();
            }

            if (parsedArgs.ContainsKey("-since"))
            {
                TimeSpan ts = TimeSpan.Parse(parsedArgs["-since"]);
                Console.WriteLine("Request messages starting from {0} ago.", ts);
                sOpts.StartAt(ts);
            }

            // override the default
            sOpts.MaxInflight = 64;
            if (parsedArgs.ContainsKey("-maxinflight"))
            {
                sOpts.MaxInflight = Convert.ToInt32(parsedArgs["-maxinflight"]);
            }

            if (parsedArgs.ContainsKey("-durable"))
            {
                sOpts.DurableName = parsedArgs["-durable"];
                Console.WriteLine("Request messages on durable subscription {0}.",
                    sOpts.DurableName);
            }
            if (parsedArgs.ContainsKey("-unsubscribe"))
            {
                Console.WriteLine("Will unsubscribe before exit.");
                unsubscribe = true;
            }

            if (parsedArgs.ContainsKey("-verbose"))
                verbose = true;
        }

        private void banner()
        {
            Console.WriteLine("Connecting to cluster '{0}' as client '{1}'.",
                clusterID, clientID);
            Console.WriteLine("Receiving {0} messages on subject {1}",
                count, subject);
            Console.WriteLine("  Url: {0}", url);
        }

        protected void cleanup()
        {
            if (sOpts.DurableName != null && unsubscribe)
            {
                s?.Unsubscribe();
            }
            c?.Close();
        }

        protected void controlCHandler(object sender, ConsoleCancelEventArgs args)
        {
            // Gracefully clean up for future runs of this sample application
            Console.WriteLine("\nReceived an interrupt, unsubscribing and closing connection...");
            cleanup();
        }

        public static void Main(string[] args)
        {
            StanSubscriber app = null;
            try
            {
                app = new StanSubscriber();
                app.Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Exception: " + ex.Message);
                if (ex.InnerException != null)
                    Console.Error.WriteLine("Inner Exception: " + ex.InnerException.Message);
            }
            finally
            {
                app?.cleanup();
            }
        }
    }
}
