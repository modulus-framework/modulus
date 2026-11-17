---
sidebar_position: 5
---

# URNs and rolling upgrades

Every message type has a wire identity: a URN. It is what other services see when they receive the message.

## The URN

```csharp
[MessageUrn("urn:orders:order-submitted:v1")]
public sealed record OrderSubmitted(Guid OrderId);
```

- The URN is the identity of a released message. Renaming the class does not change it.
- A type without the attribute gets a URN derived from its full name. That still works, but renaming the class changes what other services receive.
- Two types with the same URN are refused when the bus is built.
- Breaking a message contract needs a new URN, such as `:v2`. Both versions can be consumed at once while producers migrate.
- The contract check (in the repository's test suite) fails the build when a recorded URN disappears.

Receivers accept both the URN and the .NET name, so a message sent before a change still decodes.

## Analyzer MOD0006

The analyzer flags a message type that is sent, published or scheduled from source without `[MessageUrn]`. It runs only in projects that turn it on:

```xml
<PropertyGroup>
  <ModulusRequireMessageUrn>true</ModulusRequireMessageUrn>
</PropertyGroup>
```

The `Synvro.Modulus.AspNetCore` package carries the analyzer and the property setup. Turn it on for the services whose messages are released.

## Choosing the name a bus sends

The name each message is sent under is chosen per bus, as in MassTransit:

```csharp
services.AddModulusMessaging(b =>
{
    b.UseMessageNameFormatter(type => "acme:" + type.Name);
});
```

The default is the URN. Two buses in one process can send different names. The choice travels with each message, so the transports and the outbox all write the same name.

## Rolling upgrade

When services still expect the .NET name while others send the URN, upgrade in this order:

1. Deploy every consumer on a build that reads both forms. Receivers already do.
2. Turn on `UseLegacyWireTypeNames()` on the senders that still have consumers on an older build.
3. When no older consumer is left, remove `UseLegacyWireTypeNames()` and deploy the senders again.

The setting is per bus, and it must be set at startup, before the first message is sent. Remove it as soon as it is no longer needed: it is a bridge for one release, not a permanent setting.
