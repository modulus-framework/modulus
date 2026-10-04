namespace Modulus.AI.Connector;

using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modulus.AI.Connector.Capabilities;
using Modulus.AI.Connector.Data;
using Modulus.AI.Connector.Execution;
using Modulus.AI.Connector.Indexing;
using Modulus.AI.Connector.Revocation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;

/// <summary>Chooses the capabilities the connector exposes and how it finds the envelope's user.</summary>
public sealed class AiConnectorBuilder
{
    internal AiConnectorBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection.</summary>
    public IServiceCollection Services { get; }

    internal List<Type> Types { get; } = [];

    /// <summary>
    /// Adds a query carrying <see cref="Core.Abstractions.Ai.AiCapabilityAttribute"/> or
    /// <see cref="Core.Abstractions.Ai.AiResourceAttribute"/>. Queries whose handlers are registered with
    /// <c>AddMediatorHandlers</c> are found without this.
    /// </summary>
    public AiConnectorBuilder AddCapability<TQuery>()
    {
        Types.Add(typeof(TQuery));
        return this;
    }

    /// <summary>Adds every annotated query of <paramref name="assemblies"/>.</summary>
    public AiConnectorBuilder AddCapabilitiesFrom(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
            Types.AddRange(assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }));
        return this;
    }

    /// <summary>Finds the envelope's user with <typeparamref name="TResolver"/>.</summary>
    public AiConnectorBuilder UseUserResolver<TResolver>()
        where TResolver : class, IAiConnectorUserResolver
    {
        Services.RemoveAll<IAiConnectorUserResolver>();
        Services.AddScoped<IAiConnectorUserResolver, TResolver>();
        return this;
    }

    /// <summary>
    /// Finds the envelope's user among ASP.NET Core Identity accounts (by id, e-mail or user name, per
    /// <see cref="ModulusAiConnectorOptions.Users"/>); a locked-out account, or one <paramref name="isActive"/> rejects,
    /// is refused. Accounts are looked up in the host context (they belong to no company; with multi-tenancy on, a
    /// request that has not selected one would otherwise see none), and the call then enters the instance's company.
    /// </summary>
    public AiConnectorBuilder UseIdentityUsers<TUser>(Func<TUser, bool>? isActive = null)
        where TUser : IdentityUser<Guid>
    {
        Services.RemoveAll<IAiConnectorUserResolver>();
        Services.AddScoped<IAiConnectorUserResolver>(sp =>
            new IdentityAiConnectorUserResolver<TUser>(sp.GetRequiredService<UserManager<TUser>>(), isActive, sp.GetService<ICurrentTenant>()));
        return this;
    }
}

/// <summary>Registers the AI connector.</summary>
public static class AiConnectorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI platform connector (settings <c>Ai:Connector</c>, <see cref="ModulusAiConnectorOptions"/>): the
    /// capability registry (annotated queries handled through <c>AddMediatorHandlers</c>, plus those
    /// <paramref name="configure"/> adds), the two authentication schemes and policies, envelope verification and the
    /// revocation signals. Every user is refused until <paramref name="configure"/> picks a user resolver. Map the
    /// endpoints with <c>MapModulusAiConnector()</c>. Call it after the modules are registered.
    /// </summary>
    public static IServiceCollection AddModulusAiConnector(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AiConnectorBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new AiConnectorBuilder(services);
        configure?.Invoke(builder);
        if (services.Any(d => d.ServiceType == typeof(AiConnectorMarker)))
            throw new InvalidOperationException("AddModulusAiConnector was called twice.");
        services.AddSingleton<AiConnectorMarker>();

        services.AddOptions<ModulusAiConnectorOptions>()
            .Bind(configuration.GetSection(ModulusAiConnectorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModulusAiConnectorOptions>, AiConnectorOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecurityAuditLog>(NullSecurityAuditLog.Instance);
        services.TryAddScoped<IAiConnectorUserResolver, DenyAllAiConnectorUserResolver>();

        // The candidates are read when the registry is first resolved, so handlers registered after this call count.
        var explicitTypes = builder.Types;
        services.AddSingleton(sp =>
        {
            // The entity types an entity source reads, for [AiIndexed] and [AiQueryable]; read from a throwaway scope
            // because the source is scoped (it uses the request's data contexts).
            using var scope = sp.CreateScope();
            var entityTypes = scope.ServiceProvider.GetService<IAiEntitySource>()?.EntityTypes;
            return AiCapabilityRegistry.Build(
                [.. explicitTypes, .. HandledRequestTypes(services)],
                sp.GetRequiredService<IOptions<ModulusAiConnectorOptions>>().Value,
                entityTypes);
        });
        services.AddSingleton<AiManifestBuilder>();
        services.AddScoped<AiResultProjector>();
        services.AddScoped<AiConnectorService>();

        services.AddSingleton<AiPlatformKeyProvider>();
        services.AddSingleton<AiEnvelopeReplayCache>();
        services.AddSingleton<AiEnvelopeValidator>();
        services.AddHttpClient(AiPlatformKeyProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddSingleton<AiRevocationQueue>();
        services.AddHostedService<AiRevocationDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccessChangeObserver, AiRevocationObserver>());
        services.AddHttpClient(AiRevocationDispatcher.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));

        services.AddHostedService<AiChangeHintService>();
        services.AddHttpClient(AiChangeHintService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));

        services.AddHostedService<AiConnectorStartupCheck>();

        services.AddAuthentication()
            .AddScheme<AiConnectorAuthenticationOptions, AiConnectorAuthenticationHandler>(
                AiConnectorDefaults.AuthenticationScheme, o => o.RequireEnvelope = true)
            .AddScheme<AiConnectorAuthenticationOptions, AiConnectorAuthenticationHandler>(
                AiConnectorDefaults.ServiceAuthenticationScheme, o => o.RequireEnvelope = false);
        services.AddAuthorization(o =>
        {
            o.AddPolicy(AiConnectorDefaults.Policy, p => p
                .AddAuthenticationSchemes(AiConnectorDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AiConnectorDefaults.AppInstanceClaim));
            o.AddPolicy(AiConnectorDefaults.ServicePolicy, p => p
                .AddAuthenticationSchemes(AiConnectorDefaults.ServiceAuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AiConnectorDefaults.ServiceClaim));
        });
        return services;
    }

    // The request types of every registered mediator handler: queries become candidates, and commands are included so
    // an annotated command is refused instead of silently ignored.
    private static IEnumerable<Type> HandledRequestTypes(IServiceCollection services)
        => services
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && !t.ContainsGenericParameters
                && (t.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                    || t.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(t => t.GetGenericArguments()[0]);

    private sealed class AiConnectorMarker;
}

/// <summary>Refuses settings that would let the connector run unsafely or not at all.</summary>
internal sealed class AiConnectorOptionsValidator : IValidateOptions<ModulusAiConnectorOptions>
{
    public ValidateOptionsResult Validate(string? name, ModulusAiConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (options.ApiKeyHashes.Count is < 1 or > 2)
            failures.Add("Ai:Connector:ApiKeyHashes needs one or two SHA-256 hashes (two only while rotating).");
        if (options.ApiKeyHashes.Any(h => h is null || h.Length != 64 || !h.All(Uri.IsHexDigit)))
            failures.Add("Ai:Connector:ApiKeyHashes must hold hex SHA-256 hashes (AiApiKeys.Hash), never the keys.");
        if (options.Instances.Count == 0)
            failures.Add("Ai:Connector:Instances needs at least one app instance.");
        if (options.Instances.Any(i => string.IsNullOrWhiteSpace(i.AppInstanceId) || string.IsNullOrWhiteSpace(i.PlatformTenantId)))
            failures.Add("Every Ai:Connector:Instances entry needs AppInstanceId and PlatformTenantId.");
        if (options.Instances.GroupBy(i => i.AppInstanceId, StringComparer.Ordinal).Any(g => g.Count() > 1))
            failures.Add("Ai:Connector:Instances lists an AppInstanceId twice.");
        if (string.IsNullOrWhiteSpace(options.Platform.Issuer))
            failures.Add("Ai:Connector:Platform:Issuer is required.");
        if (string.IsNullOrWhiteSpace(options.Platform.JwksUrl) && string.IsNullOrWhiteSpace(options.Platform.SigningKeys))
            failures.Add("Ai:Connector:Platform needs JwksUrl or SigningKeys.");
        if (!string.IsNullOrWhiteSpace(options.Platform.JwksUrl)
            && (!Uri.TryCreate(options.Platform.JwksUrl, UriKind.Absolute, out var jwks) || jwks.Scheme is not ("https" or "http")))
            failures.Add("Ai:Connector:Platform:JwksUrl must be an absolute http(s) URL.");
        if (!Uri.TryCreate(options.Platform.BaseUrl, UriKind.Absolute, out var platform) || platform.Scheme is not ("https" or "http"))
            failures.Add("Ai:Connector:Platform:BaseUrl must be an absolute http(s) URL (revocation signals are required).");
        if (string.IsNullOrWhiteSpace(options.Platform.ApiKey))
            failures.Add("Ai:Connector:Platform:ApiKey is required (revocation signals are required).");
        if (!options.PathPrefix.StartsWith('/'))
            failures.Add("Ai:Connector:PathPrefix must start with '/'.");
        if (options.CallTimeout <= TimeSpan.Zero)
            failures.Add("Ai:Connector:CallTimeout must be positive.");
        if (options.ScopeTtl <= TimeSpan.Zero || options.ScopeTtl > TimeSpan.FromMinutes(5))
            failures.Add("Ai:Connector:ScopeTtl must be positive and at most 5 minutes.");
        if (options.Platform.MaxEnvelopeLifetime <= TimeSpan.Zero || options.Platform.RevocationMaxBackoff <= TimeSpan.Zero)
            failures.Add("Ai:Connector:Platform:MaxEnvelopeLifetime and RevocationMaxBackoff must be positive.");
        if (options.Indexing.ChangesSettleDelay < TimeSpan.Zero)
            failures.Add("Ai:Connector:Indexing:ChangesSettleDelay must not be negative.");
        if (options.Indexing.ChangeHints.Enabled)
        {
            if (!AiChangeHintSigner.TryReadSecret(options.Platform.WebhookSecret, out _))
                failures.Add("Ai:Connector:Platform:WebhookSecret must be a base64 secret (whsec_...) when change hints are on.");
            if (options.Indexing.ChangeHints.Interval <= TimeSpan.Zero)
                failures.Add("Ai:Connector:Indexing:ChangeHints:Interval must be positive.");
            if (!options.Indexing.ChangeHints.Path.StartsWith('/'))
                failures.Add("Ai:Connector:Indexing:ChangeHints:Path must start with '/'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Builds the capability registry and the manifest at startup, so a malformed declaration stops the host instead of
/// the first platform call, and warns while every user is refused.
/// </summary>
internal sealed class AiConnectorStartupCheck(
    IServiceProvider services,
    IOptions<ModulusAiConnectorOptions> options,
    ILogger<AiConnectorStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Task.CompletedTask;

        var manifest = services.GetRequiredService<AiManifestBuilder>().Manifest;
        logger.LogInformation("AI connector: {Capabilities} capabilities, {Resources} resource types, manifest {Fingerprint}.",
            manifest.Capabilities.Count, manifest.ResourceTypes.Count, manifest.Fingerprint);

        using var scope = services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IAiConnectorUserResolver>() is DenyAllAiConnectorUserResolver)
            logger.LogWarning("AI connector: no user resolver is configured, so every user call is refused. Call UseIdentityUsers or UseUserResolver.");

        var indexed = services.GetRequiredService<AiCapabilityRegistry>().Indexed.ToList();
        if (indexed.Count > 0 && options.Value.Indexing.Roles.All(string.IsNullOrWhiteSpace) && options.Value.Indexing.ServiceUserId is null)
            logger.LogWarning("AI connector: {Count} indexed resource types, but the indexing identity has no role or user, so extraction will be denied. Set Ai:Connector:Indexing:Roles and grant them.",
                indexed.Count);
        if (indexed.Count > 0 && scope.ServiceProvider.GetService<IAiChangeFeed>() is null)
            logger.LogWarning("AI connector: indexed resource types exist but no change feed is registered, so /changes answers 404.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
