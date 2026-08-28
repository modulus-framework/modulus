---
sidebar_position: 11
---

# Middleware API

## Available Middleware

| Middleware | Method | Purpose |
|-----------|--------|---------|
| Rate Limiting | `UseModulusRateLimiting()` | Request throttling |
| CORS | `UseModulusCors()` | Cross-origin requests |
| Security Headers | `UseModulusSecurityHeaders()` | HTTP security |
| Idempotency | `UseModulusIdempotency()` | Duplicate request handling |
| Correlation | `UseModulusCorrelation()` | Request correlation |
| Module Lifecycle | `UseModulus()` | Module validation |

## Pipeline Order

```csharp
app.UseModulusCorrelation();      // 1. Correlation (first)
app.UseModulusSecurityHeaders();  // 2. Security headers
app.UseModulusCors();             // 3. CORS
app.UseModulusRateLimiting();     // 4. Rate limiting
app.UseModulusIdempotency();      // 5. Idempotency
app.UseModulus();                 // 6. Module validation

app.MapControllers();
app.MapModulusHealthChecks();
app.MapModulusDiagnostics(app);
```

## GlobalExceptionHandler

RFC 7807 ProblemDetails mapping:

```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred",
            Detail = exception?.Message
        };
        await context.Response.WriteAsJsonAsync(problem);
    });
});
```
