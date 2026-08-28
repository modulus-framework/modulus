---
sidebar_position: 13
---

# Templates

The CLI uses Scriban templates embedded as resources.

## Template Structure

```
cli/Templates/
├── app/                          # App scaffolding
│   ├── api.csproj.sbn
│   ├── Program.sbn
│   ├── HostModule.sbn
│   ├── appsettings.json.sbn
│   └── ...
├── module/                       # 4-layer module
│   ├── domain.csproj.sbn
│   ├── application.csproj.sbn
│   ├── infrastructure.csproj.sbn
│   ├── presentation.csproj.sbn
│   ├── Domain/
│   ├── Application/
│   ├── Infrastructure/
│   └── Presentation/
└── shared/                       # Shared kernel
    ├── shared.domain.csproj.sbn
    ├── shared.application.csproj.sbn
    ├── shared.infrastructure.csproj.sbn
    └── shared.presentation.csproj.sbn
```

## Template Syntax

Templates use [Scriban](https://github.com/scriban/scriban) syntax:

```handlebars
public sealed class {{ entity_name }}Controller : ControllerBase
{
    private readonly IMediator _mediator;

    public {{ entity_name }}Controller(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<List<{{ entity_name }}Dto>>> GetAll()
        => await _mediator.QueryAsync(new GetAll{{ entity_name }}s());
}
```

## Customizing Templates

To customize generated code:

1. Extract templates from the CLI package
2. Modify the `.sbn` files
3. Place them in your project's `Templates/` directory
4. The CLI will use local templates when available

## See Also

- [Architecture](../architecture/clean-architecture) — Generated structure
