---
sidebar_position: 1
---

# API Reference

This section documents the key interfaces and classes in the Modulus framework.

## Core

| Type | Namespace | Description |
|------|-----------|-------------|
| `IModule` | `Modulus.Core.Abstractions` | Module contract |
| `ModulusModule` | `Modulus.Core.Abstractions` | Module base class |
| `ModulusBuilder` | `Modulus.Core` | Explicit module registration (`AddModule<T>()`) |
| `AggregateRoot<TId>` | `Modulus.Core.Abstractions.Domain` | DDD aggregate root |
| `ValueObject` | `Modulus.Core.Abstractions.Domain` | DDD value object |
| `IDomainEvent` | `Modulus.Core.Abstractions.Domain` | Domain event marker |
| `ICurrentTenant` | `Modulus.Core.Abstractions.Tenancy` | Current tenant |
| `ICurrentUser` | `Modulus.Core.Abstractions.Users` | Current user |

## Data

| Type | Namespace | Description |
|------|-----------|-------------|
| `IRepository<T>` | `Modulus.Data.Abstractions` | Write repository |
| `IReadRepository<T>` | `Modulus.Data.Abstractions` | Read repository |
| `ModuleDbContext` | `Modulus.EntityFrameworkCore` | Module base DbContext |
| `EfRepository<T>` | `Modulus.EntityFrameworkCore` | EF Core repository |
| `IUnitOfWork` | `Modulus.EntityFrameworkCore` | Unit of work |

## Mediator

| Type | Namespace | Description |
|------|-----------|-------------|
| `IMediator` | `Modulus.Mediator` | Mediator interface |
| `ICommand<T>` | `Modulus.Mediator` | Command marker |
| `IQuery<T>` | `Modulus.Mediator` | Query marker |
| `IPipelineBehavior<TReq,TResp>` | `Modulus.Mediator` | Pipeline behavior |

## Events

| Type | Namespace | Description |
|------|-----------|-------------|
| `IIntegrationEvent` | `Modulus.Events.Abstractions` | Integration event marker |
| `IModuleBus` | `Modulus.Events.Abstractions` | Event bus |
| `OutboxMessage` | `Modulus.Outbox.Abstractions` | Outbox row |

## ASP.NET Core

| Type | Namespace | Description |
|------|-----------|-------------|
| `Endpoint<TReq,TResp>` | `Modulus.AspNetCore.Endpoints` | REPR endpoint |
| `ModulusHub` | `Modulus.AspNetCore.SignalR` | SignalR hub base |
| `ModulusWebAppFactory<T>` | `Modulus.Testing` | Test harness |

See individual sections for detailed API documentation.
