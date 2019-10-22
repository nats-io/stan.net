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
Usage: stan-sub [options] <subject>
Options:
    -url < url >           NATS Streaming server URL(s)
    -cluster < cluster name > NATS Streaming cluster name
    -clientid < client ID >   NATS Streaming client ID
    -verbose                  Verbose mode (affects performance).

Subscription Options:
    -count < num >      # of msgs to receieve    
    -qgroup < name >    Queue group
    -seq < seqno >      Start at seqno
    -all                Deliver all available messages
    -last               Deliver starting with last published message
    -since < duration > Deliver messages in last interval(e.g. 1s, 1hr)
             (for more information: see .NET TimeSpan.Parse documentation)

   --durable < name >   Durable subscriber name
   --unsubscribe        Unsubscribe the durable on exit";

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

        StanSubscriptionOptions sOpts = StanSubscriptionOptions.GetDefaultOptions();
        StanOptions cOpts = StanOptions.GetDefaultOptions();

        public void Run(string[] args)
        {
            parseArgs(args);
            banner();

            var opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = url;

            using (var c = new StanConnectionFactory().CreateConnection(clusterID, clientID, opts))
            {
                TimeSpan elapsed = receiveAsyncSubscriber(c);
              
                Console.Write("Received {0} msgs in {1} seconds ", received, elapsed.TotalSeconds);
                Console.WriteLine("({0} msgs/second).",
                    (int)(received / elapsed.TotalSeconds));

            }
        }

        private TimeSpan receiveAsyncSubscriber(IStanConnection c)
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
                    Console.WriteLine("Received seq # {0}: {1}",
                        args.Message.Sequence,
                        System.Text.Encoding.UTF8.GetString(args.Message.Data));
                }

                if (received >= count)
                {
                    sw.Stop();
                    ev.Set();
                }
            };

            using (var s = c.Subscribe(subject, sOpts, msgHandler))
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
                    args[i].Equals("-last"))
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

            if (parsedArgs.ContainsKey("-durable"))
            {
                sOpts.DurableName = parsedArgs["-durable"];
                Console.WriteLine("Request messages on durable subscription {0}.",
                    sOpts.DurableName);
            }
            if (parsedArgs.ContainsKey("-unsubscribe"))
            {
                Console.WriteLine("Will unsubscribe before exit.");
                unsubscribe = Convert.ToBoolean(parsedArgs["-unsubscribe"]);
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

        public static void Main(string[] args)
        {
            try
            {
                new StanSubscriber().Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Exception: " + ex.Message);
                if (ex.InnerException != null)
                    Console.Error.WriteLine("Inner Exception: " + ex.InnerException.Message);
            }
        }
    }
}
