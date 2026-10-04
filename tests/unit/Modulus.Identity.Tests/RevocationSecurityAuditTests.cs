using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Xunit;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Modulus.Identity.Tests;

/// <summary><c>/connect/revoke</c> is handled by OpenIddict alone, so its outcome reaches the audit through server event handlers.</summary>
[Trait("Category", "Unit")]
public sealed class RevocationSecurityAuditTests
{
    private readonly RecordingAuditLog _audit = new();

    [Fact]
    public void The_handlers_are_registered_and_the_record_follows_OpenIddicts_own_revocation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Identity:UseDevelopmentCertificates"] = "true" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusOpenIddict(configuration);
        using var provider = services.BuildServiceProvider();

        var handlers = provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.Handlers;

        handlers.Should().Contain(RevocationSecurityAudit.RevokedHandler.Descriptor)
            .And.Contain(RevocationSecurityAudit.RefusedHandler.Descriptor);
        RevocationSecurityAudit.RevokedHandler.Descriptor.Order.Should()
            .BeGreaterThan(OpenIddictServerHandlers.Revocation.RevokeToken.Descriptor.Order);
    }

    [Fact]
    public async Task A_revoked_token_is_recorded_in_its_company_chain_without_the_token()
    {
        var tenant = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(OpenIddictConstants.Claims.Subject, "user-1"),
            new Claim("tid", tenant.ToString()),
        ], "test"));
        principal.SetTokenType(OpenIddictConstants.TokenTypeIdentifiers.RefreshToken);
        var context = new HandleRevocationRequestContext(Transaction(new OpenIddictRequest
        {
            ClientId = "spa",
            Token = "secret-token-value",
        }))
        {
            GenericTokenPrincipal = principal,
        };

        await new RevocationSecurityAudit.RevokedHandler(_audit).HandleAsync(context);

        var record = _audit.Events.Should().ContainSingle().Subject;
        record.Action.Should().Be("token.revoke");
        record.Outcome.Should().Be(SecurityAuditOutcomes.Success);
        record.Actor.Should().Be("user-1");
        record.TenantId.Should().Be(tenant);
        record.Target.Should().Be(OpenIddictConstants.TokenTypeIdentifiers.RefreshToken);
        record.Details!["client"].Should().Be("spa");
        record.Details.Values.Should().NotContain("secret-token-value");
    }

    [Fact]
    public async Task A_refused_revocation_is_recorded_as_denied_and_a_successful_response_is_not_recorded_twice()
    {
        var refused = new ApplyRevocationResponseContext(Transaction(new OpenIddictRequest { ClientId = "spa" }))
        {
            Response = new OpenIddictResponse { Error = OpenIddictConstants.Errors.InvalidClient },
        };
        var succeeded = new ApplyRevocationResponseContext(Transaction(new OpenIddictRequest { ClientId = "spa" }))
        {
            Response = new OpenIddictResponse(),
        };
        var handler = new RevocationSecurityAudit.RefusedHandler(_audit);

        await handler.HandleAsync(refused);
        await handler.HandleAsync(succeeded);

        var record = _audit.Events.Should().ContainSingle().Subject;
        record.Outcome.Should().Be(SecurityAuditOutcomes.Denied);
        record.Details!["error"].Should().Be(OpenIddictConstants.Errors.InvalidClient);
        record.TenantId.Should().BeNull("an unidentified caller is recorded in the host chain");
    }

    [Fact]
    public async Task Without_an_audit_log_the_handlers_do_nothing()
    {
        var context = new HandleRevocationRequestContext(Transaction(new OpenIddictRequest()));

        await new RevocationSecurityAudit.RevokedHandler().Invoking(h => h.HandleAsync(context).AsTask())
            .Should().NotThrowAsync();
    }

    private static OpenIddictServerTransaction Transaction(OpenIddictRequest request) => new()
    {
        Request = request,
        Logger = NullLogger.Instance,
        Options = new OpenIddictServerOptions(),
    };

    private sealed class RecordingAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
