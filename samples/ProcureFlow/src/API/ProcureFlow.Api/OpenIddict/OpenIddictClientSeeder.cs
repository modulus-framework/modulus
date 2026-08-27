using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace ProcureFlow.Api.OpenIddict;

internal sealed class OpenIddictClientSeeder(
    IServiceProvider serviceProvider,
    IOptionsMonitor<OpenIddictClientOptions> options,
    ILogger<OpenIddictClientSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var client in options.CurrentValue.Clients)
        {
            var existing = await manager.FindByClientIdAsync(client.ClientId, cancellationToken);
            if (existing is not null)
            {
                // Sync the persisted client with the current appsettings so config
                // changes (e.g. enabling password flow) take effect on re-run.
                // appsettings.json is the source of truth for the sample clients.
                var syncDescriptor = BuildDescriptor(client);
                await manager.UpdateAsync(existing, syncDescriptor, cancellationToken);
                logger.LogDebug("OpenIddict client {ClientId} re-synced", client.ClientId);
                continue;
            }

            var descriptor = BuildDescriptor(client);

            await manager.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation("Created OpenIddict client {ClientId}", client.ClientId);
        }
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(OpenIddictClientDescriptor client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientSecret = client.RequireClientSecret ? client.ClientSecret : null,
            DisplayName = client.DisplayName,
            ConsentType = client.ConsentType,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        if (client.AllowAuthorizationCode)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        }

        if (client.AllowPasswordFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
        }

        if (client.AllowRefreshToken)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        }

        if (client.AllowClientCredentials)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        }

        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(uri);
        }

        foreach (var uri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(uri);
        }

        descriptor.Permissions.Add("scp:openid");
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Email);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Roles);
        descriptor.Permissions.Add("scp:modulus");

        return descriptor;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class OpenIddictClientOptions
{
    public List<OpenIddictClientDescriptor> Clients { get; set; } = [];
}

internal sealed class OpenIddictClientDescriptor
{
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ConsentType { get; set; } = OpenIddictConstants.ConsentTypes.Explicit;
    public bool RequireClientSecret { get; set; }
    public bool AllowAuthorizationCode { get; set; }
    public bool AllowPasswordFlow { get; set; }
    public bool AllowRefreshToken { get; set; }
    public bool AllowClientCredentials { get; set; }
    public List<Uri> RedirectUris { get; set; } = [];
    public List<Uri> PostLogoutRedirectUris { get; set; } = [];
}
