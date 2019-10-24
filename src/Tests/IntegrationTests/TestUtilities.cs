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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NATS.Client;

namespace IntegrationTests
{
    class RunnableServer : IDisposable
    {
        Process p;
        readonly string executablePath;

        public RunnableServer(string exeName, string args = null)
        {
            cleanupExistingServers(exeName);
            executablePath = exeName + ".exe";
            ProcessStartInfo psInfo = createProcessStartInfo(args);

            try
            {
                p = Process.Start(psInfo);
                for (int i = 1; i <= 20; i++)
                {
                    Thread.Sleep(100 * i);
                    if (isRunning())
                        break;
                }

                if (p.HasExited)
                {
                    throw new Exception("Unable to start process.");
                }

                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                p = null;
                throw new Exception(string.Format("{0} {1} failure with error: {2}", psInfo.FileName, psInfo.Arguments, ex.Message));
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

        private static void cleanupExistingServers(string procname)
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

        private ProcessStartInfo createProcessStartInfo(string args)
        {
            var ps = new ProcessStartInfo(executablePath)
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

        private bool isRunning()
        {
            try
            {
                using (var cn = new ConnectionFactory().CreateConnection())
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

    class NatsServer : RunnableServer
    {
        public NatsServer() : base("nats-server") { }
        public NatsServer(string args) : base("nats-server", args) { }
    }

    class NatsStreamingServer : RunnableServer
    {
        public NatsStreamingServer() : base("nats-streaming-server") { }
        public NatsStreamingServer(string args) : base("nats-streaming-server", args) { }
    }
}
