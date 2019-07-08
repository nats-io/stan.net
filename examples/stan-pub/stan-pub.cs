using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using STAN.Client;
using System.Threading;

namespace STAN.Example.Publish
{
    class StanPublisher
    {
        static readonly string usageText =
@"Usage: stan-pub
    -url <url> NATS Streaming server URL(s)
    -cluster <cluster name> NATS Streaming cluster name
    -clientid <client ID> NATS Streaming client ID
    -subject <subject> subject to publish on, defaults to foo.
    -message <message payload>  Text to send in the messages.
    -async Asynchronous publish mode
    -verbose verbose mode (affects performance).
";
        Dictionary<string, string> parsedArgs = new Dictionary<string, string>();

        int count = 10000;
        string url = StanConsts.DefaultNatsURL;
        string subject   = "foo";
        string clientID  = "cs-publisher";
        string clusterID = "test-cluster";
        byte[] payload   = Encoding.UTF8.GetBytes("hello");
        bool verbose = false;
        bool async = false;
       
        StanOptions cOpts = StanOptions.GetDefaultOptions();

        public void Run(string[] args)
        {
            Stopwatch sw = null;
            long acksProcessed = 0;

            parseArgs(args);
            banner();

            cOpts.NatsURL = url;
            using (var c = new StanConnectionFactory().CreateConnection(clusterID, clientID, cOpts))
            {
                sw = Stopwatch.StartNew();

                if (async)
                {
                    AutoResetEvent ev = new AutoResetEvent(false);

                    for (int i = 0; i < count; i++)
                    {
                        string guid = c.Publish(subject, payload, (obj, pubArgs) =>
                        {
                            if (verbose)
                            {
                                Console.WriteLine("Recieved ack for message {0}", pubArgs.GUID);
                            }
                            if (!string.IsNullOrEmpty(pubArgs.Error))
                            {
                                Console.WriteLine("Error processing message {0}", pubArgs.GUID);
                            }

                            if (Interlocked.Increment(ref acksProcessed) == count)
                                ev.Set();
                        });

                        if (verbose)
                            Console.WriteLine("Published message with guid: {0}", guid);
                    }

                    ev.WaitOne();

                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        c.Publish(subject, payload);
                        if (verbose)
                            Console.WriteLine("Published message.");
                    }
                }

                sw.Stop();

                Console.Write("Published {0} msgs with acknowledgements in {1} seconds ", count, sw.Elapsed.TotalSeconds);
                Console.WriteLine("({0} msgs/second).",
                    (int)(count / sw.Elapsed.TotalSeconds));
            }
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
                if (args[i].Equals("-verbose") || args[i].Equals("-async"))
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

            if (parsedArgs.ContainsKey("-cluster"))
                clusterID = parsedArgs["-cluster"];

            if (parsedArgs.ContainsKey("-clientid"))
                clientID = parsedArgs["-clientid"];

            if (parsedArgs.ContainsKey("-count"))
                count = Convert.ToInt32(parsedArgs["-count"]);

            if (parsedArgs.ContainsKey("-server"))
                url = parsedArgs["-server"];

            if (parsedArgs.ContainsKey("-subject"))
                subject = parsedArgs["-subject"];

            if (parsedArgs.ContainsKey("-message"))
                payload = Encoding.UTF8.GetBytes(parsedArgs["-message"]);

            if (parsedArgs.ContainsKey("-verbose"))
                verbose = true;

            if (parsedArgs.ContainsKey("-async"))
                async = true;
        }

        private void banner()
        {
            Console.WriteLine("Connecting to cluster '{0}' as client '{1}'.",
                clusterID, clientID);
            Console.WriteLine("Publishing {0} messages on subject {1}",
                count, subject);
            Console.WriteLine("  Url: {0}", url);
            Console.WriteLine("  Payload is {0} bytes.",
                payload != null ? payload.Length : 0);
            Console.WriteLine("  Publish Mode is {0}.", async ? "Asynchronous" : "Synchronous (blocking)");
        }

        public static void Main(string[] args)
        {
            try
            {
                new StanPublisher().Run(args);
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
