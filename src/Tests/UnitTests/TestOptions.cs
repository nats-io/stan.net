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
using STAN.Client;
using Xunit;

namespace UnitTests
{
    public class TestOptions
    {
        private StanOptions GetDefaultOptions() => StanOptions.GetDefaultOptions();
        
        [Fact]
        public void WhenSettingPubAckMessageLimitToZeroItShouldThrow()
        {
            var opts = GetDefaultOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => opts.SetPubAckPendingLimits(0, 1));
        }
        
        [Fact]
        public void WhenSettingPubAckBytesLimitToZeroItShouldThrow()
        {
            var opts = GetDefaultOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => opts.SetPubAckPendingLimits(1, 0));
        }

        [Theory]
        [InlineData(-1, -1)]
        [InlineData(1, 1)]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        public void WhenSettingPubAckLimitsToNegativeOrPositiveValueItShouldUpdateValues(long messageLimit, long bytesLimit)
        {
            var opts = GetDefaultOptions();
            opts.SetPubAckPendingLimits(messageLimit, bytesLimit);

            Assert.Equal(messageLimit, opts.PubAckPendingMessageLimit);
            Assert.Equal(bytesLimit, opts.PubAckPendingBytesLimit);
        }
    }
}