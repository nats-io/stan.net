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
using System.IO;
using System.Linq;
using System.Threading;
using NATS.Client;

namespace IntegrationTests
{
    class RunnableServer : IDisposable
    {
        Process p;
        string executablePath;

        public void init(string exeName, string args)
        {
            TestUtilities.CleanupExistingServers(exeName);
            executablePath = exeName + ".exe";
            ProcessStartInfo psInfo = createProcessStartInfo(args);
            try
            {
                p = Process.Start(psInfo);
                for (int i = 1; i <= 20; i++)
                {
                    Thread.Sleep(100 * i);
                    if (IsRunning())
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

        public RunnableServer(string exeName)
        {
            init(exeName, null);
        }

        public RunnableServer(string exeName, string args)
        {
            init(exeName, args);
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

        public bool IsRunning()
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

            p = null;
        }

        void IDisposable.Dispose()
        {
            Shutdown();
        }
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

    class TestUtilities
    {
        object mu = new object();

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

        internal static void CleanupExistingServers(string procname)
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
    }
}
