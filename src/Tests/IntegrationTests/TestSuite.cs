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
using System.IO;
using NATS.Client;
using STAN.Client;
using Xunit;

namespace IntegrationTests
{
    public abstract class TestSuite<TContext> : IClassFixture<TContext> where TContext : class
    {
        protected TContext Context { get; }

        protected TestSuite(TContext context)
        {
            Context = context;
        }
    }

    /// <summary>
    /// IANA unassigned port range 11490-11599 has been selected withing the user-ports (1024-49151).
    /// </summary>
    public static class TestSeedPorts
    {
        public const int ClusterTests = 11490; //4pc
        public const int PingTests = 11494; //1pc
        public const int RxTests = 11495; //1pc
    }

    public abstract class TestContext
    {
        private const string TestDataDirPath = "./stan-test-data-eddc7adb06bd4b49a381ccec87fe9f41";

        public StanConnectionFactory StanConnectionFactory { get; } = new StanConnectionFactory();
        public ConnectionFactory NatsConnectionFactory { get; } = new ConnectionFactory();
        public string ClusterId { get; }
        public string ClientId { get; }
        public int DefaultWait => 10000;

        static TestContext()
        {
            try
            {
                if (Directory.Exists(TestDataDirPath))
                    Directory.Delete(TestDataDirPath, true);
            }
            catch (Exception ex)
            {
                if (Directory.Exists(TestDataDirPath))
                    throw new Exception($"Failed while trying to clean test-data at location '{TestDataDirPath}'.", ex);
            }
        }

        protected TestContext(string clusterId = null, string clientId = null)
        {
            ClusterId = clusterId ?? Guid.NewGuid().ToString("N");
            ClientId = clientId ?? Guid.NewGuid().ToString("N");
        }

        public StanOptions GetStanTestOptions(TestServerInfo serverInfo, int timeout = 5000)
        {
            var opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = serverInfo.Url;
            opts.ConnectTimeout = timeout;

            return opts;
        }

        public Options GetNatsTestOptions(TestServerInfo serverInfo)
        {
            var opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = serverInfo.Url;
            opts.ReconnectBufferSize = Options.ReconnectBufferDisabled;

            return opts;
        }

        public IStanConnection GetStanConnection(StanOptions opts, string clusterId = null, string clientId = null)
            => StanConnectionFactory.CreateConnection(clusterId ?? ClusterId, clientId ?? ClientId, opts);

        public IStanConnection GetStanConnection(TestServerInfo serverInfo, string clusterId = null, string clientId = null)
            => StanConnectionFactory.CreateConnection(clusterId ?? ClusterId, clientId ?? ClientId, GetStanTestOptions(serverInfo));

        public IConnection GetNatsConnection(Options opts) => NatsConnectionFactory.CreateConnection(opts);

        public IConnection GetNatsConnection(TestServerInfo serverInfo) => NatsConnectionFactory.CreateConnection(GetNatsTestOptions(serverInfo));

        public string GenerateStorageDirPath() => $"{TestDataDirPath}/{ClusterId}";

        public NatsStreamingServer StartStreamingServerWithEmbedded(TestServerInfo serverInfo, string args = null)
            => NatsStreamingServer.StartWithEmbedded(serverInfo, ClusterId, args);

        public NatsStreamingServer StartStreamingServerWithExternal(TestServerInfo serverInfo, string args = null)
            => NatsStreamingServer.StartWithExternal(serverInfo, ClusterId, args);

        public NatsServer StartNatsServer(TestServerInfo serverInfo, string args = null)
            => NatsServer.Start(serverInfo, args);
    }

    public class BasicTestsContext : TestContext
    {
        public readonly TestServerInfo DefaultServer = new TestServerInfo(Defaults.Port);
    }

    public class ClusterTestsContext : TestContext
    {
        private const int SeedPort = TestSeedPorts.ClusterTests;

        public readonly TestServerInfo Server1 = new TestServerInfo(SeedPort);
        public readonly TestServerInfo Server2 = new TestServerInfo(SeedPort + 1);
        public readonly TestServerInfo ClusterServer1 = new TestServerInfo(SeedPort + 2);
        public readonly TestServerInfo ClusterServer2 = new TestServerInfo(SeedPort + 3);
    }

    public class PingTestsContext : TestContext
    {
        private const int SeedPort = TestSeedPorts.PingTests;

        public readonly TestServerInfo Server1 = new TestServerInfo(SeedPort);
    }

    public class RxTestsContext : TestContext
    {
        private const int SeedPort = TestSeedPorts.RxTests;

        public readonly TestServerInfo Server1 = new TestServerInfo(SeedPort);
    }
}