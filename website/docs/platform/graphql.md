---
sidebar_position: 3
title: GraphQL
description: One GraphQL schema built from module contributors, with resolvers through the mediator.
---

# GraphQL

`Modulus.GraphQL` is an opt-in package built on **GraphQL.NET** (MIT). HotChocolate is not used, because its ASP.NET Core package depends on a commercially licensed component.

## Schema

```csharp
builder.Services.AddModulusGraphQL(builder.Configuration, typeof(Program).Assembly);
...
app.MapModulusGraphQL();   // /graphql by default
```

The schema is built from every `IGraphQLContributor` (`ConfigureQuery` / `ConfigureMutation`), in registration order. A duplicate field name or an empty `Query` fails at startup. `ExtendGraphType<TGraphType>(...)` lets one module add fields to a type another module owns.

## Resolvers

Resolvers go through the request's `IMediator`, so validation, `[CacheFor]` and transactions apply:

```csharp
Field<ProductGraphType>("product")
    .Argument<NonNullGraphType<GuidGraphType>>("id")
    .ResolveAsync(async ctx =>
    {
        var id = ctx.GetGuidArgument("id");          // malformed id -> VALIDATION_FAILED
        return await ctx.QueryAsync(new GetProductQuery(id));
    });
```

`ctx.LoadBatch(loaderName, key, fetch)` batches an extension field across a request (DataLoader).

Queries run **serially** by default, because resolvers share the request's scoped `DbContext`. Enable `ParallelQueryExecution` only when that is safe.

## Authorization

`MapModulusGraphQL` requires a signed-in user unless `RequireAuthenticatedUser: false`. Individual fields use `.AuthorizeWithPolicy("catalog:products:manage")`, the same `:` permission policies as the REST endpoints.

## Errors

Errors carry `extensions.code` with the same values as the gRPC mapping: `VALIDATION_FAILED` (with `extensions.errors`), `NOT_FOUND`, `UNAUTHENTICATED`, `PERMISSION_DENIED`, `CONFLICT`, `CONCURRENCY_CONFLICT`, `FEATURE_DISABLED`, `CANCELLED` and `INTERNAL`. Exception details appear only with `ExposeExceptionDetails`.

## Limits and defaults

| Setting | Default |
|---------|---------|
| `MaxDepth` | 15 |
| `MaxComplexity` | 1000 (list size estimate 5) |
| `MaxAliases`, `MaxDocumentLength`, `ExecutionTimeout` | Set in `GraphQL` options |
| Introspection and GraphiQL | Off (Development turns them on) |
| CSRF protection | On |
| Form posts, WebSockets, batched requests | Off |

Subscriptions are not provided. Use [Realtime](./realtime) to push events to clients.

## Persisted queries

`services.AddPersistedQueries(...)` registers query texts that clients call by their SHA-256 hash. Set `GraphQL:PersistedQueries:Mode` to `Allowlist` to refuse any other query (`PERSISTED_QUERY_REQUIRED`).

## Command-line support

```bash
modulus generate-graphql Product --module Catalog --bff mobile
```

This writes the graph type and contributor in the module's Presentation project, wires the API host, and adds a test class. With `--bff`, `/graphql` is routed through the chosen BFFs.
