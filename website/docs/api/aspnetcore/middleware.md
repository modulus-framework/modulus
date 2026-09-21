---
sidebar_position: 11
---

# Middleware API

## Available Middleware

Every `UseModulus*` middleware needs its `AddModulus*` registration first:

| Middleware | Registration | Purpose |
|-----------|--------------|---------|
| Rate Limiting | `AddModulusRateLimiting(config)` + `UseModulusRateLimiting()` | Request throttling |
| CORS | `AddModulusCors(config)` + `UseModulusCors()` | Cross-origin requests |
| Security Headers | `AddModulusSecurityHeaders(config)` + `UseModulusSecurityHeaders()` | HTTP security |
| Idempotency | `AddModulusIdempotency(config)` + `UseModulusIdempotency()` | Duplicate request handling |
| Correlation | `AddModulusCorrelation(config)` + `UseModulusCorrelation()` | Request correlation |
| Module pipeline | `UseModulus()` | Tenant resolution + module pipeline |

## Pipeline Order

```csharp
app.UseForwardedHeaders();
app.UseModulusCorrelation();      // 1. Correlation (first)
app.UseModulusSecurityHeaders();  // 2. Security headers
app.UseExceptionHandler();
app.UseModulusCors();             // 3. CORS (before auth)
app.UseAuthentication();
app.UseAuthorization();
app.UseModulusRateLimiting();     // 4. Rate limiting (after auth/tenant)
app.UseModulus();                 // 5. Tenant resolution + module pipeline
app.UseModulusIdempotency();      // 6. Idempotency (after UseModulus so keys are tenant-scoped)

app.MapControllers();
app.MapModulusEndpoints(...);
app.MapModulusHealthChecks();     // /health/live + /health/ready
app.MapModulusDiagnostics(app);   // /health/modules + /health/graph
```

## GlobalExceptionHandler

The framework's `IExceptionHandler` maps to RFC 7807 ProblemDetails
automatically (registered via `AddModulusExceptionHandling()` +
`app.UseExceptionHandler()`):

| Exception | Status |
|-----------|--------|
| `ValidationException` | 400 |
| `NotFoundException` | 404 |
| `UnauthorizedException` | 401 |
| `ForbiddenException` | 403 |
| `ConflictException` / `DbUpdateConcurrencyException` | 409 |
| `FeatureDisabledException` | 404 |
| anything else | 500 |
