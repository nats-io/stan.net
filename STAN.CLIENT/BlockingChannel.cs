/*******************************************************************************
 * Copyright (c) 2015-2016 Apcera Inc. All rights reserved. This program and the accompanying
 * materials are made available under the terms of the MIT License (MIT) which accompanies this
 * distribution, and is available at http://opensource.org/licenses/MIT
 *******************************************************************************/
using System;
using System.Collections.Generic;
using System.Threading;

namespace STAN.Client
{

    // This dictionary class is a capacity bound, threadsafe, dictionary.
    internal sealed class BlockingDictionary<TKey, TValue>
    {
        IDictionary<TKey, TValue> d = new Dictionary<TKey, TValue>();
        Object dLock   = new Object();
        Object addLock = new Object();
        bool finished = false;
        long maxSize = 1024;
        bool atCapacity = false;

        internal bool isAtCapacity()
        {
            lock (dLock)
            {
                return d.Count >= maxSize;
            }
        }

        internal void waitForSpace()
        {
            lock (addLock)
            {
                lock (dLock)
                {
                    atCapacity = d.Count >= maxSize;
                }

                if (atCapacity)
                {
                    Monitor.Wait(addLock);
                }
            }
        }

        internal void notifySpaceAvailable()
        {
            lock (addLock)
            {
                if (atCapacity)
                {
                    atCapacity = false;
                    Monitor.Pulse(addLock);
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

            value = default(TValue);

            lock (dLock)
            {
                if (!finished)
                {
                    if (d.Count > 0)
                    {
                        rv = d.TryGetValue(key, out value);
                    }
                    else
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
                }

                // we waited..
                if (!finished)
                {
                    rv = d.TryGetValue(key, out value);
                }

                if (rv)
                    d.Remove(key);
            }

            notifySpaceAvailable();

            return rv;

        } // get

        internal void Add(TKey key, TValue value)
        {
            lock (dLock)
            {
                
                d[key] =  value;

                // if the queue count was previously zero, we were
                // waiting, so signal.
                if (d.Count <= 1)
                {
                    Monitor.Pulse(dLock);
                }
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

