# NATS .NET C# Streaming Client

NATS Streaming is an extremely performant, lightweight reliable streaming platform powered by [NATS](https://nats.io).

[![License Apache 2.0](https://img.shields.io/badge/License-Apache2-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)
[![Build status](https://ci.appveyor.com/api/projects/status/55r49k0apwdh94s5?svg=true)](https://ci.appveyor.com/project/NATS-CI47222/csharp-nats-streaming)
[![API Documentation](https://img.shields.io/badge/doc-Doxygen-brightgreen.svg?style=flat)](http://nats-io.github.io/stan.net)
[![NuGet](https://img.shields.io/nuget/v/STAN.Client.svg?maxAge=2592000)](https://www.nuget.org/packages/STAN.Client)

NATS Streaming provides the following high-level feature set:
- Log based persistence
- At-Least-Once Delivery model, giving reliable message delivery
- Rate matched on a per subscription basis
- Replay/Restart
- Last Value Semantics

## Notes
- Please raise issues or ask questions via the [Issue Tracker](https://github.com/nats-io/csharp-nats-streaming/issues).  For general discussion, visit our slack channel.  Requests to join can be made [here](https://join.slack.com/t/natsio/shared_invite/enQtMzE2NDkxNDI2NTE1LTc5ZDEzYTkwYWZkYWQ5YjY1MzBjMWZmYzA5OGQxMzlkMGQzMjYxNGM3MWYxMjNiYmNjNzIwMTVjMWE2ZDgxZGM).

## Installation

For convenience, the NATS streaming client can found on NuGet as [STAN.Client](https://www.nuget.org/packages/STAN.Client/).  

Alternatively, you can build the C# NATS streaming client yourself.  To build, you'll need an environment to build [Visual Studio](https://www.visualstudio.com/en-us/products/visual-studio-community-vs.aspx) and/or .NET core project files.

### .NET core

From the directory the repository has been cloned into, either invoke `buildcore.bat` from the command line, or open the `STANcore.sln` project file in Visual Studio and build from there.

### .NET 4.5

From the directory the repository has been cloned into, either invoke `build45.bat` from the command line, or open the `STANnet45.sln` project file in Visual Studio and build from there.

### API Documentation

[Current API Documentation](http://nats-io.github.io/stan.net)

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
var s = c.Subscribe("foo", opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Receive all stored values in order
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.DeliverAllAvailable();
var s = c.Subscribe("foo", opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Receive messages starting at a specific sequence number
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAt(22);
var s = c.Subscribe("foo", opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Subscribe starting at a specific time
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAt(new DateTime(2016, 07, 28, 5, 35, 04, 570));
var s = c.Subscribe("foo", opts, (obj, args) =>
{
    Console.WriteLine("Received a message: {0}",
        System.Text.Encoding.UTF8.GetString(args.Message.Data));
});

// Subscribe starting a specific amount of time in the past (e.g. 30 seconds ago)
var opts = StanSubscriptionOptions.GetDefaultOptions();
opts.StartAt(new TimeSpan(0, 0, 30));
var s = c.Subscribe("foo", opts, (obj, args) =>
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

### Connection Status

The fact that the NATS Streaming server and clients are not directly connected poses a challenge when it comes to know if a client is still valid.
When a client disconnects, the streaming server is not notified, hence the importance of calling `Close()`. The server sends heartbeats
to the client's private inbox and if it misses a certain number of responses, it will consider the client's connection lost and remove it
from its state.

Before version `0.6.0`, the client library was not sending PINGs to the streaming server to detect connection failure. This was problematic
especially if an application was never sending data (had only subscriptions for instance). Picture the case where a client connects to a
NATS Server which has a route to a NATS Streaming server (either connecting to a standalone NATS Server or the server it embeds). If the
connection between the streaming server and the client's NATS Server is broken, the client's NATS connection would still be ok, yet, no
communication with the streaming server is possible. This is why relying on `Conn.NatsConn()` to check the status is not helpful.

Starting version `0.6.0` of this library and server `0.10.0`, the client library will now send PINGs at regular intervals (default is 5 seconds)
and will close the streaming connection after a certain number of PINGs have been sent without any response (default is 3). When that
happens, a callback - if one is registered - will be invoked to notify the user that the connection is permanently lost, and the reason
for the failure.

Here is how you would specify your own PING values and the callback:

```csharp
    // Send PINGs every 10 seconds, and fail after 5 PINGs without any response.
    StanOptions so = StanOptions.GetDefaultOptions();
    so.PingInterval = 10000;
    so.PingMaxOutstanding = 5;
    so.ConnectionLostEventHandler = (obj, args) =>
    {
        // handle the case where a connection has been lost
    }

    var sc = new StanConnectionFactory().CreateConnection(CLUSTER_ID, CLIENT_ID, so);
```

Note that the only way to be notified is to set the callback. If the callback is not set, PINGs are still sent and the connection
will be closed if needed, but the application won't know if it has only subscriptions.

When the connection is lost, your application would have to re-create it and all subscriptions if any.

When no NATS connection is provided, the library creates its own NATS connection and will now set the reconnect attempts to
"infinite", which was not the case before. It should therefore be possible for the library to always reconnect, but this
does not mean that the streaming connection will not be closed, even if you set a very high threshold for the PINGs max out
value. Keep in mind that while the client is disconnected, the server is sending heartbeats to the clients too, and when not
getting any response, it will remove that client from its state. When the communication is restored,
the PINGs sent to the server will allow to detect this condition and report to the client that the connection is now closed.

Also, while a client is "disconnected" from the server, another application with connectivity to the streaming server may
connect and uses the same client ID. The server, when detecting the duplicate client ID, will try to contact the first client
to know if it should reject the connect request of the second client. Since the communication between the server and the
first client is broken, the server will not get a response and therefore will replace the first client with the second one.

Prior to client `0.6.0` and server `0.10.0`, if the communication between the first client and server were to be restored,
and the application would send messages, the server would accept those because the published messages client ID would be
valid, although the client is not. With client at `0.6.0+` and server `0.10.0+`, additional information is sent with each
message to allow the server to reject messages from a client that has been replaced by another client.

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

### Enabling TLS

Establishing secure connections for use in the NATS streaming client is
relatively straightforward.  Create a core NATS connection with TLS enabled then
configure the streaming client to use that secure connection through options.

Here is example code to establish a secure connection:

```csharp
using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using NATS.Client;
using STAN.Client;

namespace NATSStreamingExamples
{
    class StanTLSExample
    {
        // This is an override for convenience.  NEVER blindly ignore TLS
        // errors in production code.
        static bool verifyServerCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            Console.WriteLine("WARN: Ignoring TLS Certificate validation error: {0}", sslPolicyErrors);
            return true;  // throw verification out the window for this example.
        }

        // This method returns an established secure core NATS connection
        // for the streaming client to use.
        static IConnection createSecureNATSConnection()
        {
            // get the default NATS options
            var natsOptions = ConnectionFactory.GetDefaultOptions();

            // Setup NATS to be "secure", then load and add a certificate into the NATS options.
            // See https://github.com/nats-io/csharp-nats#tls for additional information.
            natsOptions.Secure = true;
            natsOptions.AddCertificate(new X509Certificate2("client.pfx", "password"));
            natsOptions.TLSRemoteCertificationValidationCallback += verifyServerCert;

            return new ConnectionFactory().CreateConnection(natsOptions);
        }

        // To use TLS with a NATS streaming client, create a secure core NATS
        // connection to pass into the NATS streaming client.
        static void Main(string[] args)
        {
            // Get default STAN options, then create and assign a secure
            // core NATS connection for the streaming client to use.
            var stanOptions = StanOptions.GetDefaultOptions();
            stanOptions.NatsConn = createSecureNATSConnection();

            // Finally create a STAN logical connection using the
            // secure NATS connection we just created.
            var connection = new StanConnectionFactory().CreateConnection("test-cluster", "test-client-001", stanOptions);
        }
    }
}
```

### Unit Testing Applications

Testing in a distributed environment is difficult at best, so to facilitate
unit testing, the NATS streaming client library has provided some constructors
for objects normally created by the NATS client library.  These are not 
recommended for use outside of a test environment.

There are constructors available for `StanMsgHandlerArgs`,
`StanMsg`, `StanAckHandlerArgs`, and `StanConnLostHandlerArgs`.  See the
documentation or code for more information.

Let's look at message handling as an example.  You have two options, either
generate the `StanMsgHandlerArgs` and call your event handler directly, or
construct a `StanMsg` to test with.

```csharp

// Application code to process a message
void processMessageEventHandler(object o, StanMsgHandlerArgs args)
{
    // Your application code does something here.
}
```

Testing would look something like this:

```csharp
[Fact]
public void TestProcessMessageEventHandler()
{
    // Create a test args object
    var mhArgs = new StanMsgHandlerArgs(
        System.Text.Encoding.UTF8.GetBytes("NATS"),
        true, "foo", 10000, 1234, null);

    processMessageEventHandler(this, mhArgs);

    // Check the results here
}
```

Alternatively, you can create a `StanMsg` directly.

```csharp
[Fact]
public void TestHandleDataMessage()
{
    StanMsg msg = new StanMsg(
        System.Text.Encoding.UTF8.GetBytes("DATA"),
        false, "bar", 0, 1234, null);

    // Call your application code that handles a data message.
    handleDataMessage(msg);
}
```

While these faciliate unit testing, we always recommend testing
your application code end to end.

## TODO

- [ ] Rx API
- [ ] Robust Benchmark Testing
