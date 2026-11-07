---
sidebar_position: 2
title: gRPC
description: Expose module services over gRPC, with status mapping, health checks and resilient clients.
---

# gRPC

`Modulus.Grpc` is an opt-in package (Grpc.AspNetCore, Apache-2.0). It works as a second API surface in a modular monolith and for service-to-service calls in microservices. A gRPC call runs through the normal pipeline, so correlation, tenant resolution, authentication and `:` permission policies apply unchanged.

## Server

```csharp
builder.Services.AddModulusGrpc(builder.Configuration);
...
app.MapModulusGrpc(typeof(Program).Assembly);   // services, then health and reflection
```

`Grpc` settings (`ModulusGrpcOptions`) control detailed errors, reflection (off by default), health checks and message sizes. Health is backed by every `IModuleHealthCheck`.

### Ports

Kestrel cannot serve HTTP/2 without TLS on an `Http1AndHttp2` endpoint. A plain-HTTP host therefore needs a separate `Http2` endpoint for gRPC (for example `Kestrel:Endpoints:Grpc` on `http://localhost:5189` in Development). With TLS, one endpoint serves both.

Set `Identity:Issuer` to the public URL on any host reachable under more than one address. Otherwise a token issued on one endpoint is rejected on the other.

## Errors

Every failure becomes a standard gRPC status with a `google.rpc.ErrorInfo` detail (`domain` = `modulus`):

| Exception | Status |
|-----------|--------|
| `ValidationException` | `InvalidArgument` (with `BadRequest` field violations) |
| `NotFoundException` | `NotFound` |
| `UnauthorizedException` / `ForbiddenException` | `Unauthenticated` / `PermissionDenied` |
| `ConflictException`, EF concurrency | `Aborted` |
| Anything else | `Internal` (no exception text unless detailed errors are on) |

Clients read the details with `GetValidationErrors()`, `GetErrorReason()` and `GetErrorMetadata()` on `RpcException`.

## Client

```csharp
builder.Services.AddModulusGrpcClient<CatalogClient>(new Uri("https://catalog.internal"))
    .PropagateTenant()
    .ForwardAccessToken();
```

The client retries only `Unavailable` (3 attempts with jittered back-off), propagates the call deadline and cancellation, sets a default 30-second deadline and sends `X-Correlation-ID`.

## grpc-web

Set `Grpc:EnableGrpcWeb` and call `app.UseModulusGrpcWeb()` for browser clients. The CORS policy must allow `x-grpc-web`, `x-user-agent` and `grpc-timeout`, and expose `grpc-status`, `grpc-message` and `grpc-status-details-bin`.

## Command-line support

```bash
modulus generate-grpc Product --module Catalog --bff mobile
```

This writes the `.proto` file and the service in the module's Presentation project, the API host wiring, and a test class. With `--bff`, the contract is added to each BFF as a client. Existing files are never overwritten.
