# NATS .NET C# Streaming Client

NATS Streaming is an extremely performant, lightweight reliable streaming platform powered by [NATS](https://nats.io).

[![License MIT](https://img.shields.io/badge/License-MIT-blue.svg)](http://opensource.org/licenses/MIT)
[![Build status](https://ci.appveyor.com/api/projects/status/55r49k0apwdh94s5?svg=true)](https://ci.appveyor.com/project/NATS-CI47222/csharp-nats-streaming)
[![API Documentation](https://img.shields.io/badge/doc-Doxygen-brightgreen.svg?style=flat)](http://nats-io.github.io/csharp-nats-streaming)
[![NuGet](https://img.shields.io/nuget/v/STAN.Client.svg?maxAge=2592000)](https://www.nuget.org/packages/STAN.Client)

NATS Streaming provides the following high-level feature set:
- Log based persistence
- At-Least-Once Delivery model, giving reliable message delivery
- Rate matched on a per subscription basis
- Replay/Restart
- Last Value Semantics

## Notes
- Please raise questions/issues via the [Issue Tracker](https://github.com/nats-io/csharp-nats-streaming/issues) or via natsio.slack.com (contact larry@apcera.com or brian@apcera.com for access)

## Installation

For convenience, the NATS streaming client can found on NuGet as [STAN.Client](https://www.nuget.org/packages/STAN.Client/).  

Alternatively, you can build the C# NATS streaming client yourself.  To build, you'll need an environment to build [Visual Studio](https://www.visualstudio.com/en-us/products/visual-studio-community-vs.aspx) and/or .NET core project files.

### .NET core
From the directory the repository has been cloned into, either invoke `buildcore.bat` from the command line, or open the `STANcore.sln` project file in Visual Studio and build from there.

### .NET 4.5
From the directory the repository has been cloned into, either invoke `build45.bat` from the command line, or open the `STANnet45.sln` project file in Visual Studio and build from there.

### API Documentation

[Current API Documentation](http://nats-io.github.io/csharp-nats-streaming)

Doxygen is used for building the API documentation.  To build the API documentation, change directories to `doc` and run the following command:

```
build_doc.bat
```

Doxygen will build the NATS .NET Streaming Client API documentation, placing it in the `doc\STAN.Client\html` directory.
Doxygen is required to be installed and in the PATH.  Version 1.8 is known to work.


## Basic Usage

```csharp
var cf = new StanConnectionFactory();
using (var c = cf.CreateConnection("test-cluster", "appname"))
{
    using (c.Subscribe("foo", (obj, args) =>
    {
        Console.WriteLine(
            System.Text.Encoding.UTF8.GetString(args.Message.Data));
    }))
    {
        c.Publish("foo", System.Text.Encoding.UTF8.GetBytes("hello"));
    }
}
```

### Subscription Start (i.e. Replay) Options

NATS Streaming subscriptions are similar to NATS subscriptions, but clients may start their subscription at an earlier point in the message stream, allowing them to receive messages that were published before this client registered interest.

The options are described with examples below:

```csharp
// Subscribe starting with most recently published value
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartWithLastReceived();
var s = c.Subscribe("foo", (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Receive all stored values in order
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.DeliverAllAvailable();
var s = c.Subscribe("foo", (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Receive messages starting at a specific sequence number
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAtSequence(22);
var s = c.Subscribe("foo", (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Subscribe starting at a specific time
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAtTime(new DateTime(2016, 07, 28, 5, 35, 04, 570));
var s = c.Subscribe("foo", (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Subscribe starting a specific amount of time in the past (e.g. 30 seconds ago)
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAtTimeDelta(new TimeSpan(0, 0, 30));
var s = c.Subscribe("foo", (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});
```

### Durable Subscriptions

Replay of messages offers great flexibility for clients wishing to begin processing at some earlier point in the data stream.
However, some clients just need to pick up where they left off from an earlier session, without having to manually track their position in the stream of messages.
Durable subscriptions allow clients to assign a durable name to a subscription when it is created.
Doing this causes the NATS Streaming server to track the last acknowledged message for that clientID + durable name, so that only messages since the last acknowledged message will be delivered to the client.

```csharp
var cf = new StanConnectionFactory();
var c = cf.CreateConnection("test-cluster", "client-123");

// Subscribe with a durable name
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.DurableName = "my-durable";

var s = c.Subscribe("foo",  opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});
...
// client receives message sequence 1-40
...
// client disconnects for an hour
...
// client reconnects with same clientID "client-123"
c = cf.CreateConnection("test-cluster", "client-123");

// client re-subscribes to "foo" with same durable name "my-durable" (set above)
var s = c.Subscribe("foo",  opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});
...
// client receives messages 41-current
```

### Wildcard Subscriptions

NATS Streaming subscriptions **do not** support wildcards.

## Advanced Usage

### Asynchronous Publishing

The basic publish API (`Publish(subject, payload)`) is synchronous; it does not return control to the caller until the NATS Streaming server has acknowledged receipt of the message. To accomplish this, a unique ID is generated for the message on creation, and the client library waits for a publish acknowledgement from the server with a matching NUID before it returns control to the caller, possibly with an error indicating that the operation was not successful due to some server problem or authorization error.

Advanced users may wish to process these publish acknowledgements manually to achieve higher publish throughput by not waiting on individual acknowledgements during the publish operation. An asynchronous publish API is provided for this purpose:

The NATS streaming .NET client supports async/await usage with a publisher.  The task returned to await upon contains
the GUID of the published message.  The publish API will still block when the maximum outstanding acknowledgements has been
reached to allow flow control in your application.

```csharp
// all in one call.
var guid = await c.PublishAsync("foo", null);

// alternatively, one can work in some application code.
var t = c.PublishAsync("foo", null);

// your application can do work here

guid = await t;
```

A more traditional method is provided as well (an event handler).

```csharp
var cf = new StanConnectionFactory();
var c = cf.CreateConnection("test-cluster", "client-123");

// when the server responds with an acknowledgement, this
// handler will be invoked.
EventHandler<StanAckHandlerArgs> ackHandler = (obj, args) =>
{
    if (!string.IsNullOrEmpty(args.Error))
    {
        Console.WriteLine("Published Msg {0} failed: {1}",
            args.GUID, args.Error);
    }

    // handle success - correlate the send with the guid..
    Console.WriteLine("Published msg {0} was stored on the server.");
};

// returns immediately
string guid = c.Publish("foo", null, ackHandler);
Console.WriteLine("Published msg {0} was stored on the server.", guid);
```

### Message Acknowledgements and Redelivery

NATS Streaming offers At-Least-Once delivery semantics, meaning that once a message has been delivered to an eligible subscriber, if an acknowledgement is not received within the configured timeout interval, NATS Streaming will attempt redelivery of the message.
This timeout interval is specified by the subscription option `AckWait`, which defaults to 30 seconds.

By default, messages are automatically acknowledged by the NATS Streaming client library after the subscriber's message handler is invoked. However, there may be cases in which the subscribing client wishes to accelerate or defer acknowledgement of the message.
To do this, the client must set manual acknowledgement mode on the subscription, and invoke `Ack()` on the `StanMsg`. ex:

```csharp
var sOpts = StanSubscriptionOptions.GetDefaultOptions();
sOpts.ManualAcks = true;
sOpts.AckWait = 60000;
var s = c.Subscribe("foo", sOpts, (obj, args) =>
{
    // ack message before performing I/O intensive operation
    args.Message.Ack();

    // Perform long operation.  Note, the message will not be redelivered
    // in an application crash, unless requested.
});
```

## Rate limiting/matching

A classic problem of publish-subscribe messaging is matching the rate of message producers with the rate of message consumers.
Message producers can often outpace the speed of the subscribers that are consuming their messages.
This mismatch is commonly called a "fast producer/slow consumer" problem, and may result in dramatic resource utilization spikes in the underlying messaging system as it tries to buffer messages until the slow consumer(s) can catch up.

### Publisher rate limiting

NATS Streaming provides a connection option called `MaxPubAcksInflight` that effectively limits the number of unacknowledged messages that a publisher may have in-flight at any given time. When this maximum is reached, further `PublishAsync()` calls will block until the number of unacknowledged messages falls below the specified limit. ex:

```csharp
var cf = new StanConnectionFactory();

var opts = StanOptions.GetDefaultOptions();
opts.MaxPubAcksInFlight = 25;

var c = cf.CreateConnection("test-cluster", "client-123", opts);

EventHandler<StanAckHandlerArgs> ackHandler = (obj, args) =>
{
    // process the ack.
};

for (int i = 0; i < 1000; i++)
{
    // If the server is unable to keep up with the publisher, the number of oustanding acks will eventually
    // reach the max and this call will block
    string guid = c.Publish("foo", null, ackHandler);
}
```

### Subscriber rate limiting

Rate limiting may also be accomplished on the subscriber side, on a per-subscription basis, using a subscription option called `MaxInflight`.
This option specifies the maximum number of outstanding acknowledgements (messages that have been delivered but not acknowledged) that NATS Streaming will allow for a given subscription.
When this limit is reached, NATS Streaming will suspend delivery of messages to this subscription until the number of unacknowledged messages falls below the specified limit. ex:

```csharp
var sOpts = StanSubscriptionOptions.GetDefaultOptions();
sOpts.ManualAcks = true;
sOpts.MaxInflight = 25;
var s = c.Subscribe("foo", sOpts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
   ...
   // Does not ack, or takes a very long time to ack
   ...
   // Message delivery will suspend when the number of unacknowledged messages reaches 25
});

```

## TODO
- [X] Core 1.0 support
- [ ] Rx API
- [ ] Robust Benchmark Testing
- [X] CI (Appveyor)

## License

(The MIT License)

Copyright (c) 2012-2016 Apcera Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to
deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
IN THE SOFTWARE.
