---
sidebar_position: 14
---

# Templates

The CLI uses Scriban templates embedded as assembly resources
(`TemplateEngine` loads them via `GetManifestResourceStream` — there is no
filesystem override; customizing output means building a custom CLI).

## Template Structure

```
cli/Templates/
├── app/                          # App scaffolding
│   ├── api.csproj.sbn
│   ├── Program.sbn
│   ├── appsettings.json.sbn
│   └── ...
├── module/                       # 4-layer module
│   ├── Domain/
│   ├── Application/              # handlers, Dtos/, IntegrationEvents/
│   ├── Infrastructure/           # DbContext, repository, module composition root
│   └── Presentation/             # Endpoint.sbn (REPR endpoints)
└── shared/                       # Shared kernel
    ├── shared.domain.csproj.sbn
    ├── shared.application.csproj.sbn
    ├── shared.infrastructure.csproj.sbn
    └── shared.presentation.csproj.sbn
```

## Template Syntax

Templates use [Scriban](https://github.com/scriban/scriban) syntax:

```scriban
public sealed class Get{{ entity_plural }}Endpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<{{ entity_name }}Dto>>
{
    public override void Configure()
    {
        Get("/api/{{ module_name_lower }}/{{ route_name }}");
        RequireAuthorization();
    }
}
```

## See Also

- [Architecture](../architecture/clean-architecture) — Generated structure
