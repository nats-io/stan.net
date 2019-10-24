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
    public abstract class TestSuite<TSuiteContext> : IClassFixture<TSuiteContext> where TSuiteContext : class
    {
        protected TSuiteContext Context { get; }

        protected TestSuite(TSuiteContext context)
        {
            Context = context;
        }
    }

    /// <summary>
    /// IANA unassigned port range 11490-11599 has been selected withing the user-ports (1024-49151).
    /// </summary>
    public static class TestSeedPorts
    {
        public const int DefaultSuiteNormalServers = 4222; //2pc
        public const int DefaultSuiteNormalClusterServers = 6222; //2pc
    }

    public abstract class SuiteContext
    {
        private const string TestDataDirPath = "./stan-test-data-eddc7adb06bd4b49a381ccec87fe9f41";

        public StanConnectionFactory StanConnectionFactory { get; } = new StanConnectionFactory();
        public ConnectionFactory NatsConnectionFactory { get; } = new ConnectionFactory();
        public string ClusterId { get; }
        public string ClientId { get; }
        public int DefaultWait => 10000;

        static SuiteContext()
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

        protected SuiteContext(string clusterId = null, string clientId = null)
        {
            ClusterId = clusterId ?? Guid.NewGuid().ToString("N");
            ClientId = clientId ?? Guid.NewGuid().ToString("N");
        }

        public StanOptions GetTestOptions(int port = 4222, int timeout = 5000)
        {
            var opts = StanOptions.GetDefaultOptions();
            opts.NatsURL = $"nats://127.0.0.1:{port}";
            opts.ConnectTimeout = timeout;

            return opts;
        }

        public IStanConnection GetStanConnection(string clusterId = null, string clientId = null, StanOptions opts = null)
            => StanConnectionFactory.CreateConnection(clusterId ?? ClusterId, clientId ?? ClientId, opts ?? GetTestOptions());

        public IConnection GetNatsConnection(Options opts = null) =>
            opts == null
                ? NatsConnectionFactory.CreateConnection()
                : NatsConnectionFactory.CreateConnection(opts);

        public string GenerateStorageDirPath() => $"{TestDataDirPath}/{ClusterId}";

        public NatsStreamingServer StartStreamingServer(string args = null)
            => new NatsStreamingServer(ClusterId, args);

        public NatsServer StartNatsServer(string args = null)
            => new NatsServer(args);

        //public Options GetTestOptionsWithDefaultTimeout(int? port = null)
        //{
        //    var opts = ConnectionFactory.GetDefaultOptions();

        //    if (port.HasValue)
        //        opts.Url = $"nats://localhost:{port.Value}";

        //    return opts;
        //}

        //public Options GetTestOptions(int? port = null)
        //{
        //    var opts = GetTestOptionsWithDefaultTimeout(port);
        //    opts.Timeout = 10000;

        //    return opts;
        //}

        //public IConnection OpenConnection(int? port = null)
        //{
        //    var opts = GetTestOptions(port);

        //    return ConnectionFactory.CreateConnection(opts);
        //}

        //public IEncodedConnection OpenEncodedConnectionWithDefaultTimeout(int? port = null)
        //{
        //    var opts = GetTestOptionsWithDefaultTimeout(port);

        //    return ConnectionFactory.CreateEncodedConnection(opts);
        //}
    }

    public class BasicTestsContext : SuiteContext
    {
        private const int SeedPortNormalServers = TestSeedPorts.DefaultSuiteNormalServers;

        public readonly TestServerInfo DefaultServer = new TestServerInfo(Defaults.Port);

        public readonly TestServerInfo Server1 = new TestServerInfo(SeedPortNormalServers);
        public readonly TestServerInfo Server2 = new TestServerInfo(SeedPortNormalServers + 1);

        private const int SeedPortClusterServers = TestSeedPorts.DefaultSuiteNormalClusterServers;

        public readonly TestServerInfo ClusterServer1 = new TestServerInfo(SeedPortClusterServers);
        public readonly TestServerInfo ClusterServer2 = new TestServerInfo(SeedPortClusterServers + 1);
    }
}