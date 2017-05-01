/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;
using System.Threading;
using System.Diagnostics;
#if NET45
using System.Reflection;
#endif
using NATS.Client;

using System.IO;

namespace STAN.Client.UnitTests
{
    class NatsStreamingServer : IDisposable
    {
        bool debug = false;
        Process p;

        private bool isNatsServerRunning()
        {
            try
            {
                IConnection c = new NATS.Client.ConnectionFactory().CreateConnection();
                c.Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void init(bool shouldDebug)
        {
            UnitTestUtilities.CleanupExistingServers();

            debug = shouldDebug;
            ProcessStartInfo psInfo = createProcessStartInfo();
            p = Process.Start(psInfo);
            for (int i = 1; i <= 20; i++)
            {
                Thread.Sleep(100*i);
                if (isNatsServerRunning())
                    break;
            }

            if (p.HasExited)
            {
                throw new Exception("Server failure with exit code: " + p.ExitCode);
            }
            // Allow the Nats streaming server to setup.
            Thread.Sleep(1000);
        }

        public NatsStreamingServer()
        {
            init(false);
        }

        public NatsStreamingServer(bool shouldDebug)
        {
            init(shouldDebug);
        }

        private void addArgument(ProcessStartInfo psInfo, string arg)
        {
            if (psInfo.Arguments == null)
            {
                psInfo.Arguments = arg;
            }
            else
            {
                string args = psInfo.Arguments;
                args += arg;
                psInfo.Arguments = args;
            }
        }

        public NatsStreamingServer(int port)
        {
            ProcessStartInfo psInfo = createProcessStartInfo();

            addArgument(psInfo, "-p " + port);

            this.p = Process.Start(psInfo);
        }

        public NatsStreamingServer(string args)
        {
            ProcessStartInfo psInfo = this.createProcessStartInfo();
            addArgument(psInfo, args);
            p = Process.Start(psInfo);
        }

        private ProcessStartInfo createProcessStartInfo()
        {
            string nss = "nats-streaming-server.exe";
            ProcessStartInfo psInfo = new ProcessStartInfo(nss);

            if (debug)
            {
                psInfo.Arguments = " -SDV -DV";
            }
            else
            {
#if NET45
                psInfo.WindowStyle = ProcessWindowStyle.Hidden;
#else
                psInfo.CreateNoWindow = false;
                psInfo.RedirectStandardError = true;
#endif
            }

            psInfo.WorkingDirectory = UnitTestUtilities.GetConfigDir();

            return psInfo;
        }

        public void Shutdown()
        {
            if (p == null)
                return;

            try
            {
                p.Kill();
                p.WaitForExit(60000);
            }
            catch (Exception) { }

            p = null;
        }

        void IDisposable.Dispose()
        {
            Shutdown();
        }
    }

    class UnitTestUtilities
    {
        object mu = new object();
        static NatsStreamingServer defaultServer = null;

        static internal string GetConfigDir()
        {
#if NET45
            string baseDir = Assembly.GetExecutingAssembly().CodeBase;
            return baseDir + "\\NATSUnitTests\\config";
#else
            return AppContext.BaseDirectory +
                string.Format("{0}..{0}..{0}..{0}",
                Path.DirectorySeparatorChar);
#endif
        }

        public void StartDefaultServer()
        {
            lock (mu)
            {
                if (defaultServer == null)
                {
                    defaultServer = new NatsStreamingServer();
                }
            }
        }

        public void StopDefaultServer()
        {
            lock (mu)
            {
                try
                {
                    defaultServer.Shutdown();
                }
                catch (Exception) { }

                defaultServer = null;
            }
        }

        public void bounceDefaultServer(int delayMillis)
        {
            StopDefaultServer();
            Thread.Sleep(delayMillis);
            StartDefaultServer();
        }

        internal static void CleanupExistingServers()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("nats-streaming-server");

                foreach (Process proc in procs)
                {
                    proc.Kill();
                }
            }
            catch (Exception) { } // ignore
        }
    }
}
