---
sidebar_position: 11
---

# OpenAPI

`AddModulusOpenApi` replaces a bare `AddOpenApi()` with a hardened document
(config-driven info + JWT Bearer scheme). The JSON is still served via
`app.MapOpenApi()`.

## Setup

```csharp
builder.Services.AddModulusOpenApi(builder.Configuration);
// ...
app.MapOpenApi();
```

## Configuration

```json
{
  "OpenApi": {
    "DocumentName": "v1",
    "Title": "MyApp API",
    "Version": "v1",
    "Description": "MyApp HTTP API",
    "IncludeBearerSecurity": true,
    "ContactName": null,
    "ContactEmail": null,
    "ContactUrl": null,
    "LicenseName": null,
    "LicenseUrl": null
  }
}
```

A document transformer stamps the info block (title/version/description/
contact/license) and registers a reusable JWT **Bearer** security scheme; an
operation transformer adds the Bearer requirement to operations carrying
`[Authorize]` (skipping `[AllowAnonymous]`), so API UIs show a padlock only
where it applies.

## See Also

- [API Versioning](versioning) — Per-version documents
