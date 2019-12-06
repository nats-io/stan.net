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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using NATS.Client;
using STAN.Client;

namespace IntegrationTests
{
    public abstract class RunnableServer : IDisposable
    {
        Process p;

        protected RunnableServer(string exeName, string args, Func<bool> isRunning)
        {
            var psInfo = createProcessStartInfo($"{exeName}.exe", args);

            try
            {
                p = Process.Start(psInfo);
                for (int i = 1; i <= 20; i++)
                {
                    Thread.Sleep(100 * i);
                    if (isRunning())
                        break;
                }

                if (p == null || p.HasExited)
                    throw new Exception("Unable to start process.");

                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                p = null;
                throw new Exception($"{psInfo.FileName} {psInfo.Arguments} failure with error: {ex.Message}");
            }
        }

        private static void stopProcess(Process p)
        {
            try
            {
                var successfullyClosed = p.CloseMainWindow() || p.WaitForExit(100);
                if (!successfullyClosed)
                    p.Kill();
                p.Close();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        protected static void CleanupExistingServers(string procname)
        {
            Func<Process[]> getProcesses = () => Process.GetProcessesByName(procname);

            var processes = getProcesses();
            if (!processes.Any())
                return;

            foreach (var proc in processes)
                stopProcess(proc);

            // Let the OS cleanup.
            for (int i = 0; i < 10; i++)
            {
                processes = getProcesses();
                if (!processes.Any())
                    break;

                Thread.Sleep(i * 250);
            }

            Thread.Sleep(500);
        }

        private ProcessStartInfo createProcessStartInfo(string exe, string args)
        {
            var ps = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                Arguments = args,
                WorkingDirectory = Environment.CurrentDirectory,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardError = true
            };

            return ps;
        }

        protected static bool IsRunning(TestServerInfo serverInfo)
        {
            try
            {
                var opts = ConnectionFactory.GetDefaultOptions();
                opts.Url = serverInfo.Url;
                opts.ReconnectBufferSize = Options.ReconnectBufferDisabled;

                using (var cn = new ConnectionFactory().CreateConnection(opts))
                {
                    cn.Close();

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Shutdown()
        {
            if (p == null)
                return;

            stopProcess(p);

            p = null;
        }

        void IDisposable.Dispose() => Shutdown();
    }

    public class NatsServer : RunnableServer
    {
        private const string ProcName = "nats-server";

        static NatsServer() => RunnableServer.CleanupExistingServers(ProcName);

        private NatsServer(string args, Func<bool> isRunning) : base(ProcName, args, isRunning) { }

        public static NatsServer Start(TestServerInfo serverInfo, string additionalArgs = null)
        {
            var args = $"-a {serverInfo.Address} -p {serverInfo.Port} {additionalArgs}";

            return new NatsServer(args, () => IsRunning(serverInfo));
        }
    }

    public class NatsStreamingServer : RunnableServer
    {
        private const string ProcName = "nats-streaming-server";

        static NatsStreamingServer() => RunnableServer.CleanupExistingServers(ProcName);

        private NatsStreamingServer(string args, Func<bool> isRunning) : base(ProcName, args, isRunning) { }

        public static NatsStreamingServer StartWithEmbedded(TestServerInfo serverInfo, string clusterId, string additionalArgs = null)
        {
            var args = $"-cid {clusterId} -a {serverInfo.Address} -p {serverInfo.Port} {additionalArgs}";

            return new NatsStreamingServer(args, () => IsRunning(serverInfo));
        }

        public static NatsStreamingServer StartWithExternal(TestServerInfo serverInfo, string clusterId, string additionalArgs = null)
        {
            var args = $"-cid {clusterId} -ns tcp://{serverInfo.Address}:{serverInfo.Port} {additionalArgs}";

            return new NatsStreamingServer(args, () => IsRunning(serverInfo));
        }
    }

    public sealed class TestSync : IDisposable
    {
        private SemaphoreSlim semaphore;
        private readonly int initialNumOfActors;

        private static readonly TimeSpan WaitTs = TimeSpan.FromMilliseconds(1000);

        private TestSync(int numOfActors)
        {
            initialNumOfActors = numOfActors;
            semaphore = new SemaphoreSlim(0, numOfActors);
        }

        public static TestSync SingleActor() => new TestSync(1);

        public static TestSync TwoActors() => new TestSync(2);

        public static TestSync FourActors() => new TestSync(4);

        private void Wait(int aquireCount, TimeSpan ts)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                ts = TimeSpan.FromMilliseconds(-1);
            }

            using (var cts = new CancellationTokenSource(ts))
            {
                for (var c = 0; c < aquireCount; c++)
                    semaphore.Wait(cts.Token);
            }
        }

        public void WaitForAll(TimeSpan? ts = null) => Wait(initialNumOfActors, ts ?? WaitTs);

        public void WaitForOne(TimeSpan? ts = null) => Wait(1, ts ?? WaitTs);

        public void SignalComplete() => semaphore.Release(1);

        public void Dispose()
        {
            semaphore?.Dispose();
            semaphore = null;
        }
    }

    public sealed class SamplePayload : IEquatable<SamplePayload>
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        public readonly byte[] Data;
        public readonly string Text;

        private SamplePayload(string text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Data = Enc.GetBytes(text);
        }

        private SamplePayload(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Text = Enc.GetString(data);
        }

        public static SamplePayload Random() => new SamplePayload(Guid.NewGuid().ToString("N"));

        public static implicit operator SamplePayload(StanMsg m) => new SamplePayload(m.Data);

        public static implicit operator byte[](SamplePayload p) => p.Data;

        public bool Equals(SamplePayload other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.Data.SequenceEqual(Data);
        }

        public override bool Equals(object obj) => Equals(obj as SamplePayload);

        public override int GetHashCode() => Data.GetHashCode();

        public override string ToString() => Text;

        public static bool operator ==(SamplePayload left, SamplePayload right) => Equals(left, right);

        public static bool operator !=(SamplePayload left, SamplePayload right) => !Equals(left, right);
    }
}
