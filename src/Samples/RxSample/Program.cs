using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client.Rx.Ops; //Can be replaced with using System.Reactive.Linq;
using STAN.Client;
using STAN.Client.Rx;

namespace RxSample
{
    class Program
    {
        static void Main(string[] args)
        {
            var clusterId = "test-cluster";
            var clientId = Guid.NewGuid().ToString("N");

            using (var cn = new StanConnectionFactory().CreateConnection(clusterId, clientId))
            {
                var cts = new CancellationTokenSource();

                Task.Run(() =>
                {
                    var temperatures =
                        cn.Observe("temperatures")
                            .Where(m => m.Data?.Any() == true)
                            .Select(m => BitConverter.ToInt32(m.Data, 0));

                    temperatures.Subscribe(t => Console.WriteLine($"{t}C"));

                    temperatures.Subscribe(t => Console.WriteLine($"{(t * 9 / 5) + 32}F"));
                }, cts.Token);

                Task.Run(async () =>
                {
                    var rnd = new Random();

                    while (!cts.IsCancellationRequested)
                    {
                        cn.Publish("temperatures", BitConverter.GetBytes(rnd.Next(-10, 40)));

                        await Task.Delay(1000, cts.Token);
                    }
                }, cts.Token);

                Console.WriteLine("Hit any key to exit");
                Console.ReadKey();
                cts.Cancel();
            }
        }
    }
}