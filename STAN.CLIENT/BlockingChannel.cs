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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace STAN.Client
{

    // This dictionary class is a capacity bound, threadsafe, dictionary.
    internal sealed class BlockingDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> d = new Dictionary<TKey, TValue>();
        private readonly Object dLock = new Object();
        private readonly Object addLock = new Object();
        private bool finished = false;
        private long maxSize = 1024;

        private bool isAtCapacity()
        {
            lock (dLock)
            {
                return d.Count >= maxSize;
            }
        }

        internal ICollection<TKey> Keys
        {
            get
            {
                lock (dLock)
                {
                    // Take a snapshot under the lock
                    return d.Keys.ToArray();
                }
            }
        }

        internal void waitForSpace()
        {
            lock (addLock)
            {
                while (isAtCapacity())
                {
                    Monitor.Wait(addLock);
                }
            }
        }

        private BlockingDictionary() { }

        internal BlockingDictionary(long maxSize)
        {
            if (maxSize <= 0)
                throw new ArgumentOutOfRangeException("maxSize", maxSize, "maxSize must be greater than 0");

            this.maxSize = maxSize;
        }

        internal bool Remove(TKey key, out TValue value, int timeout)
        {
            bool rv = false;
            bool wasAtCapacity = false;

            value = default(TValue);

            lock (dLock)
            {
                // check and wait if empty and not finished
                while (!finished && d.Count == 0)
                {
                    if (timeout < 0)
                    {
                        Monitor.Wait(dLock);
                    }
                    else
                    {
                        if (timeout > 0)
                        {
                            if (Monitor.Wait(dLock, timeout) == false)
                            {
                                throw new Exception("timeout");
                            }
                        }
                    }
                }

                if (!finished)
                {
                    rv = d.TryGetValue(key, out value);
                }

                if (rv)
                {
                    wasAtCapacity = d.Count >= maxSize;
                    d.Remove(key);
                }
            }

            // wasAtCapacity is updated under a lock
            // so we do not hold both dLock and addLock
            if (wasAtCapacity)
            {
                lock (addLock)
                {
                    Monitor.Pulse(addLock);
                }
            }

            return rv;

        } // get

        // if false, caller should waitForSpace then
        // call again (until true)
        internal bool TryAdd(TKey key, TValue value)
        {
            lock (dLock)
            {
                // if at capacity, do not attempt to add
                if (d.Count >= maxSize)
                {
                    return false;
                }

                d[key] =  value;

                // if the queue count was previously zero, we were
                // waiting, so signal.
                if (d.Count <= 1)
                {
                    Monitor.Pulse(dLock);
                }

                return true;
            }
        }

        internal void close()
        {
            lock (dLock)
            {
                finished = true;
                Monitor.Pulse(dLock);
            }
        }

        internal int Count
        {
            get
            {
                lock (dLock)
                {
                    return d.Count;
                }
            }
        }
    } // class BlockingChannel
}

