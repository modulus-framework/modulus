---
sidebar_position: 1
title: AI Connector
description: Host the external AI platform's read-only connector contract inside a Modulus app.
---

# AI Connector

`Modulus.AI.Connector` hosts the connector contract (version 1) of an external AI platform inside your application. Modulus holds no LLM code and no MCP code. The platform is **read-only**, so the connector exposes only queries. Writes are never exposed.

The connector stays off until you register the application with the platform. The contract is written up in platform-neutral form, so other applications can implement it as well.

## Registration

```csharp
builder.Services.AddModulusAiConnector(builder.Configuration, ai => ai
    .UseIdentityUsers<AppUser>()
    .UseEntityFrameworkCore());     // change journal, change feed, entity source

app.MapModulusAiConnector();        // /_ai/connector/*, after UseAuthentication/UseAuthorization
```

Without a user resolver, every user call is refused. Set `Ai:Connector` for the company and instance settings. Keep `Platform:ApiKey` and `Platform:WebhookSecret` in user secrets or the environment.

## Exposing a query

Mark a read query with `[AiCapability]`. For a single-record lookup, add `[AiResource]`:

```csharp
[AiCapability("Catalog.Product.Get", "Get one product")]
public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;

[AiResource("Catalog.Product", "A product", DeepLink = "/products/{id}")]
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;
```

Calls run through the mediator **as the calling user** in that user's company. `[SecretData]` fields never leave the application. Classified fields follow `IFieldAuthorizer`, which fails closed when none is registered.

## Indexing

To make an entity searchable, mark it with `[AiIndexed("Catalog.Product")]`. It needs:

- a `[AiResource]` lookup for the type;
- a client-generated key (`Guid.CreateVersion7()`). Store-generated keys are not supported, because the id does not exist when the change is recorded.

The platform calls `GET /extract` and `GET /changes` with its API key and an `?appInstanceId=` value. The calls run as the identity in `Ai:Connector:Indexing:Roles` (default `AiIndexer`), so grant that role the read permissions of the indexed entities.

Deletes become tombstones. Change hints are optional: they send a signed notification with an id and timestamp, never data.

## Generated search and calculation

`[AiQueryable("Catalog.Product", description, permission, Fields = [...])]` generates `Search` and `Calculate` operations. Filters are typed, values are bound as parameters (no SQL text is built), and filtering or grouping on a field the caller cannot read returns `DENIED`.

## Personal data

Fields marked `[PersonalInformation]` or `[ProtectedPersonalData]` are declared `Restricted` in the manifest. Set `Ai:Connector:MaskPersonalInformation` to `true` to withhold them unless the user holds the clearance for that field.

## Command-line support

```bash
modulus add-ai
modulus generate-crud Product --module Catalog --ai
```

`add-ai` adds the connector to the API host, test settings and conformance tests. `generate-crud --ai` marks the entity and writes its capability tests.

## Conformance

`Modulus.AI.Connector.Testing` provides `AiFakePlatform` and `AiConnectorConformance`. They check authentication, tenant isolation, field security, query injection, revocation, typed errors and timeouts (a timeout must end as a deny). Generated apps run these checks in `AiConformanceTests`.
