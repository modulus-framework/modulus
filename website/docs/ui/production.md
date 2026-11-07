---
title: "Hosting and production"
description: "The hardening defaults (compression, security headers, a Content-Security-Policy with a nonce), the health endpoint, OpenTelemetry, and what to check before you publish."
sidebar_position: 11
---

The hosting package adds the defaults a public site needs. Add them once, in `Program.cs`, and they apply to every page.

```csharp title="Program.cs"
builder.Services.AddModulusHardening();
builder.Services.AddModulusObservability();

var app = builder.Build();

app.UseModulusHardening();
app.MapModulusHealth();
```

`AddModulusHardening` and `UseModulusHardening` are both needed: the first registers the services, and the second adds the middleware, in the order you call it.

## What hardening adds

- **Compression.** Brotli and gzip for text responses, so the pages and scripts are small.
- **Security headers.** No MIME sniffing, a referrer policy, same-site framing only, and no camera, microphone or location access.
- **A Content-Security-Policy with a nonce.** Each request gets a nonce, which the layout puts on the scripts it writes. Read it in your own views with `CspNonce.Of(Context)`.
- **Asset caching.** The versioned asset URLs that the MVC head writes (with `?v=`) are cached for a year, so returning visitors load the assets from the browser cache.

### Report or enforce the policy

The policy starts in report-only mode, so a violation is logged rather than blocked. That lets you find a script the policy would stop before it breaks a page. When the logs are clean, turn enforcement on:

```csharp title="Program.cs"
builder.Services.AddModulusHardening(options => options.CspEnforce = true);
```

MVC and Razor Pages enforce the policy through the nonce. In Blazor the policy is reported rather than enforced, because Blazor's import map is an inline script without a nonce.

## The health endpoint

`MapModulusHealth()` answers `GET /health`. Point your load balancer or container platform at it. It takes an optional path if you want another address:

```csharp
app.MapModulusHealth("/healthz");
```

## Observability

`AddModulusObservability()` adds OpenTelemetry traces, metrics and logs. Nothing leaves the process until you set an endpoint. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to your collector, and the telemetry is sent over OTLP.

## Localization

If you list `Cultures` in the theme options, call `app.UseModulusLocalization()` for MVC and Razor Pages, or `UseRequestLocalization` with the same cultures for Blazor. See [Theming and layout](./theming.md#languages).

## Sign-in and menus

The menu can hide items from people who do not have a policy: set `RequiredPolicy` on a `MenuItemDefinition`. The samples sign in with a cookie against in-memory users with the password `modulus`, and they can require sign-in on every page with `Samples:RequireSignIn`. Your app needs its own user store and sign-in before you publish.

## Before you publish

- **Run in Production, not only Development.** The library's static assets are served by `dotnet run` only in the Development environment. A published app carries them in `wwwroot`, so publish with `dotnet publish` and run the output.
- **Turn on the enforced policy.** See above.
- **Check the health endpoint** from outside the host.
- **Use your own tile server** if you show maps. The public tile service is for light use only. See [Plugins](./plugins.md).
- **Sanitize HTML** from rich-text editors on the server, with `RichText.Sanitize`, before you store or show it.
- **Sign-in and users.** The samples are not a user store. Add yours, and set the policies on the menu.

## Known limits

- Ant Design Blazor 1.6 ships Ant Design 4 styling. Modulus adds a refresh layer on top, so it looks like Ant Design 5 but is not identical to it.
- The brand colour in dark mode comes from a generated stylesheet. `tools/antd-drift` in the repository fails when it no longer matches the pinned Ant Design version, so an upgrade is checked before it ships.
- Authorization is wired for menus only; page-level authorization is yours to add.
