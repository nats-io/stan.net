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
using NATS.Client.Rx;

namespace STAN.Client.Rx
{
    public static class RxExtensions
    {
        /// <summary>
        /// Subscribes to the passed subject and returns a hot observable. Hence unless you
        /// subscribe to the observable, no message will be handled and old messages will
        /// not be delivered, only new messages.
        /// </summary>
        /// <param name="cn"></param>
        /// <param name="subject"></param>
        /// <returns></returns>
        public static INATSObservable<StanMsg> Observe(this IStanConnection cn, string subject)
            => StanObservableSubscription.Create(cn ?? throw new ArgumentNullException(nameof(cn)), subject);
    }
}