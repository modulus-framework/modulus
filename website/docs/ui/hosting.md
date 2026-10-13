---
sidebar_position: 8
---

# Hosting Defaults

`Synvro.Modulus.AspNetCore.Hosting` gives the web host a production baseline: response compression, security headers, a Content Security Policy with a nonce, a health endpoint and optional observability. The generated web host calls it, so you get these without writing code.

## Services and middleware

```csharp
builder.Services.AddModulusHardening();   // compression, security headers, CSP, /health
app.UseModulusHardening();                // before the rest of the pipeline
app.MapModulusHealth();                   // /health
```

| Feature | What it does |
|---|---|
| Compression | Brotli and gzip for text responses |
| Security headers | No MIME sniffing, a referrer policy, same-site framing, and no camera, microphone or location access |
| Content Security Policy | A policy with a per-request nonce. Scripts you write in a view take the nonce: `<script nonce="@CspNonce.Of(Context)">`. |
| Health | `MapModulusHealth()` answers `/health` |
| Asset caching | The versioned asset URLs the MVC head writes (`?v=`) are cached for a year |

## The Content Security Policy

MVC and Razor Pages enforce the policy. Blazor reports violations only, because the Blazor import map is an inline script without a nonce.

If a page needs an inline script, give the script the nonce:

```cshtml
<script nonce="@CspNonce.Of(Context)">
  console.log("allowed: the script carries the request's nonce");
</script>
```

An inline script without the nonce is blocked in MVC and Razor Pages. Move it to a file under `wwwroot`, or add the nonce.

## Observability

`AddModulusObservability()` adds OpenTelemetry traces, metrics and logs. They are sent over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

## Forwarded headers and proxies

Behind a reverse proxy, the app needs the real client address for logging and rate limiting. The generated web host reads `X-Forwarded-For` and `X-Forwarded-Proto` only from the proxies you list in `ForwardedHeaders:KnownProxies` or `ForwardedHeaders:KnownNetworks`. With none configured, only loopback is trusted.

## Localization in the pipeline

`app.UseModulusLocalization()` (MVC and Razor Pages) sets the request culture from `Modulus:Theme:Cultures`. Blazor uses `UseRequestLocalization` with the same cultures. See [Theming and localization](./theming).

## SPA navigation (MVC and Razor Pages)

With `Modulus:Theme:SpaNavigation` set to `true`, links and forms in the shell load through htmx `hx-boost`. The server answers with the page body, the sidebar and the header, so the page's scripts and Alpine state stay loaded. The shell aborts stale navigations, shows a progress bar, restores scroll positions and moves focus to the page. Prefetching on hover is controlled by `SpaPrefetch`.

Page code that must run after each navigation uses `Modulus.page(fn)` or `Modulus.onPage(fn)`.

Before turning it on, read the SPA section of the framework repository's `docs/UPGRADING.md`. It explains what changes for your own scripts.

## Checking the defaults

- Run the samples in Development or publish them. `dotnet run` with the Production environment does not serve the library static assets, so test Production builds with `dotnet publish`.
