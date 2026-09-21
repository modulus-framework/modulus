---
sidebar_position: 10
---

# API Versioning

Modulus wires `Asp.Versioning` for both REPR endpoints and MVC controllers.

## Setup

```csharp
builder.Services.AddModulusApiVersioning(builder.Configuration);
```

## Configuration

```json
{
  "ApiVersioning": {
    "DefaultVersion": "1.0",
    "AssumeDefaultVersionWhenUnspecified": true,
    "ReportApiVersions": true,
    "ReadFromQueryString": true,
    "ReadFromHeader": true,
    "ReadFromUrlSegment": true,
    "HeaderName": "X-Api-Version",
    "QueryStringParameter": "api-version"
  }
}
```

Versions are read from the query string (`?api-version=`), a header
(`X-Api-Version`), and URL segments (`/v1/…`) whenever routes declare one.
Unversioned requests assume the default version; `api-supported-versions` /
`api-deprecated-versions` response headers are reported.

## See Also

- [OpenAPI](openapi) — Versioned API explorer integration
