using System.Reflection;
using FluentValidation;
using ProcureFlow.Shared.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Extensions;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Shared.Application;

public static class ApplicationConfiguration
{
    /// <summary>
    /// Wires the app onto <c>Modulus.Mediator</c>'s dispatch engine, with the app's
    /// own <c>Result</c>-returning pipeline behaviors instead of Modulus's built-in
    /// ones (which throw on validation failure — this app returns
    /// <c>Result.Failure</c> instead, so the API's error contract is unchanged).
    /// Registration order (Exception, Logging, Validation) reproduces the exact
    /// effective execution order the app had before: Exception(outermost) →
    /// Logging → Validation → Handler — <c>Modulus.Mediator</c>'s own dispatcher
    /// resolves behaviors via the same <c>GetServices&lt;&gt;().Reverse()</c>
    /// convention the previous custom <c>Dispatcher</c> used.
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        Assembly[] moduleAssemblies)
    {
        services.AddMediator(opts =>
        {
            // All off: the behaviors below replace Modulus's built-ins so the
            // Result-returning (not exception-throwing) contract is preserved.
            opts.EnableLogging = false;
            opts.EnableValidation = false;
            opts.EnableAuthorization = false;
            opts.EnableCaching = false;
            opts.EnableTransaction = false;
        });
        services.AddMediatorHandlers(moduleAssemblies);

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ExceptionHandlingPipelineBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RequestLoggingPipelineBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationPipelineBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationPipelineBehavior<,>));

        services.AddValidatorsFromAssemblies(moduleAssemblies, includeInternalTypes: true);

        return services;
    }
}
