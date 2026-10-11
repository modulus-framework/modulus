namespace Modulus.Identity;

using System.Collections.Specialized;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens.Saml2;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>Where a SAML sign-in stands, as the endpoints need it.</summary>
/// <param name="Succeeded">The person is signed in (the Identity cookie was written).</param>
/// <param name="ReturnUrl">The local address to send them to; null on failure.</param>
/// <param name="Denial">A short reason code on failure (never shown to the person, recorded in the audit).</param>
public sealed record SamlSignInResult(bool Succeeded, string? ReturnUrl, string? Denial);

/// <summary>The service-provider side of SAML 2.0 sign-in (web browser SSO profile, SP-initiated).</summary>
public interface ISamlSignInService
{
    /// <summary>The identity providers, for building sign-in buttons.</summary>
    IReadOnlyList<(string Name, string DisplayName)> Providers { get; }

    /// <summary>Starts a sign-in: the address of the identity provider to send the browser to; null when the provider is unknown. <paramref name="returnUrl"/> must be local.</summary>
    Task<Uri?> BeginAsync(string provider, string returnUrl, CancellationToken ct = default);

    /// <summary>Finishes a sign-in with the identity provider's answer (the posted <c>SAMLResponse</c> and <c>RelayState</c>), writing the Identity cookie on success.</summary>
    Task<SamlSignInResult> CompleteAsync(string provider, string? samlResponse, string? relayState, CancellationToken ct = default);

    /// <summary>This service provider's metadata document for a provider's administrator to import.</summary>
    string? Metadata(string provider);
}

internal sealed class SamlSignInService<TContext, TUser>(
    IOptions<SamlOptions> options,
    TContext db,
    ExternalAccountProvisioner<TUser> provisioner,
    SignInManager<TUser> signInManager,
    TimeProvider clock,
    IServiceProvider services,
    ILogger<SamlSignInService<TContext, TUser>> logger)
    : ISamlSignInService
    where TContext : DbContext
    where TUser : ModulusUser, new()
{

    private static readonly string[] s_emailNames =
        ["email", "mail", "emailaddress", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress", "urn:oid:0.9.2342.19200300.100.1.3"];

    private static readonly string[] s_firstNames =
        ["givenName", "firstName", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname", "urn:oid:2.5.4.42"];

    private static readonly string[] s_lastNames =
        ["surname", "sn", "lastName", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname", "urn:oid:2.5.4.4"];

    private SamlOptions Options => options.Value;

    public IReadOnlyList<(string Name, string DisplayName)> Providers
        => [.. Options.Providers.Select(p => (p.Name, p.DisplayName ?? p.Name))];

    public async Task<Uri?> BeginAsync(string provider, string returnUrl, CancellationToken ct = default)
    {
        if (Find(provider) is not { } p)
            return null;
        if (!IsLocal(returnUrl))
            throw new ArgumentException("The return address must be local.", nameof(returnUrl));

        var config = Configure(p);
        var request = new Saml2AuthnRequest(config)
        {
            AssertionConsumerServiceUrl = new Uri(AcsUrl(p)),
            ForceAuthn = p.ForceAuthentication ? true : null,
            NameIdPolicy = new NameIdPolicy { AllowCreate = true },
        };

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<ModulusSamlRequest>().Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var row = new ModulusSamlRequest
        {
            Id = token,
            Provider = p.Name,
            RequestId = request.IdAsString,
            ReturnUrl = returnUrl,
            ExpiresAt = now.AddSeconds(Options.RequestLifetimeSeconds),
        };
        db.Set<ModulusSamlRequest>().Add(row);
        await db.SaveChangesAsync(ct);
        db.Entry(row).State = EntityState.Detached;

        var binding = new Saml2RedirectBinding { RelayState = token };
        binding.Bind(request);
        return binding.RedirectLocation;
    }

    public async Task<SamlSignInResult> CompleteAsync(string provider, string? samlResponse, string? relayState, CancellationToken ct = default)
    {
        if (Find(provider) is not { } p)
            return Deny("unknown-provider");
        if (string.IsNullOrEmpty(samlResponse) || string.IsNullOrEmpty(relayState) || relayState.Length > 64)
            return Deny("missing-response");

        // One answer per request this server made: the row goes first, so a replay (or an answer to a request of another
        // provider, or one nobody asked for) finds nothing.
        var pending = await ConsumeAsync(relayState, ct);
        if (pending is null || !string.Equals(pending.Provider, p.Name, StringComparison.Ordinal))
            return Deny("unknown-request");

        Saml2AuthnResponse response;
        try
        {
            response = Unbind(p, samlResponse, relayState);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The library's messages name what failed in the signature or the conditions: log them, never show them.
            logger.LogWarning(ex, "A SAML response from {Provider} was refused.", p.Name);
            return Deny("invalid-response");
        }

        if (!string.Equals(response.InResponseToAsString, pending.RequestId, StringComparison.Ordinal))
            return Deny("in-response-to");
        if (!string.Equals(response.Issuer, p.EntityId, StringComparison.Ordinal))
            return Deny("issuer");
        if (response.Destination is { } destination && !string.Equals(destination.AbsoluteUri, AcsUrl(p), StringComparison.Ordinal))
            return Deny("destination");

        var person = ReadPerson(p, response);
        if (person is null)
            return Deny("no-subject");

        var rules = new ExternalProvisioningRules(p.TenantId, p.AutoProvision, p.LinkExistingByEmail, p.GroupRoles);
        var outcome = await provisioner.SignInAsync(person, rules, ct);
        if (outcome.User is not { } user)
            return Deny(outcome.Denial ?? "denied", p.TenantId);

        // This path asks for no code, so a second factor the account holds locally cannot be met here: refuse rather than skip it.
        if (await signInManager.UserManager.GetTwoFactorEnabledAsync(user))
            return Deny("second-factor-required", p.TenantId);

        var methods = new List<Claim> { new(TokenPrincipalFactory.MethodClaim, "fed") };
        if (UsedMultiFactor(p, response))
            methods.Add(new Claim(TokenPrincipalFactory.MethodClaim, "mfa"));
        await signInManager.SignInWithClaimsAsync(user, isPersistent: false, methods);

        Audit("signin.saml", SecurityAuditOutcomes.Success, user.TenantId, p.Name, null);
        return new SamlSignInResult(true, pending.ReturnUrl, null);
    }

    public string? Metadata(string provider)
    {
        if (Find(provider) is not { } p)
            return null;

        var acs = AcsUrl(p);
        var certificate = LoadServiceCertificate();
        var keyDescriptor = certificate is null
            ? ""
            : $"""<md:KeyDescriptor use="signing"><ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#"><ds:X509Data><ds:X509Certificate>{Convert.ToBase64String(certificate.RawData)}</ds:X509Certificate></ds:X509Data></ds:KeyInfo></md:KeyDescriptor>""";
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{System.Security.SecurityElement.Escape(Options.EntityId)}">
              <md:SPSSODescriptor AuthnRequestsSigned="{(p.SignRequests ? "true" : "false")}" WantAssertionsSigned="true" protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                {keyDescriptor}
                <md:NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:persistent</md:NameIDFormat>
                <md:AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{System.Security.SecurityElement.Escape(acs)}" index="0" isDefault="true" />
              </md:SPSSODescriptor>
            </md:EntityDescriptor>
            """;
    }

    internal Saml2AuthnResponse Unbind(SamlProviderOptions p, string samlResponse, string relayState)
    {
        var config = Configure(p);
        var http = new ITfoxtec.Identity.Saml2.Http.HttpRequest
        {
            Method = "POST",
            Form = new NameValueCollection { ["SAMLResponse"] = samlResponse, ["RelayState"] = relayState },
            QueryString = string.Empty,
            Query = [],
        };
        var response = new Saml2AuthnResponse(config);
        var binding = new Saml2PostBinding();
        binding.ReadSamlResponse(http, response);
        if (response.Status != Saml2StatusCodes.Success)
            throw new InvalidOperationException($"The identity provider answered {response.Status}.");

        binding.Unbind(http, response);
        return response;
    }

    private ExternalPrincipal? ReadPerson(SamlProviderOptions p, Saml2AuthnResponse response)
    {
        var claims = response.ClaimsIdentity?.Claims.ToList() ?? [];
        string? First(params string?[] names)
            => names.Where(n => !string.IsNullOrEmpty(n))
                .Select(n => claims.FirstOrDefault(c => string.Equals(c.Type, n, StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var nameId = response.NameId?.Value;
        var subject = p.SubjectAttribute is { Length: > 0 } attribute ? First(attribute) : nameId;
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var email = First([p.EmailAttribute, .. s_emailNames]) ?? (nameId is not null && nameId.Contains('@', StringComparison.Ordinal) ? nameId : null);
        var groupAttribute = p.GroupAttribute;
        var groups = claims
            .Where(c => string.Equals(c.Type, groupAttribute, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return new ExternalPrincipal(
            $"saml:{p.Name}",
            subject,
            First(p.UserNameAttribute) ?? email,
            email,
            First([p.FirstNameAttribute, .. s_firstNames]),
            First([p.LastNameAttribute, .. s_lastNames]),
            groups,
            EmailVerified: p.EmailAssertedVerified);
    }

    private static bool UsedMultiFactor(SamlProviderOptions p, Saml2AuthnResponse response)
    {
        if (p.MfaAuthnContexts.Count == 0)
            return false;
        var statement = response.Saml2SecurityToken?.Assertion.Statements.OfType<Saml2AuthenticationStatement>().FirstOrDefault();
        var reference = statement?.AuthenticationContext?.ClassReference?.OriginalString;
        return reference is not null && p.MfaAuthnContexts.Contains(reference, StringComparer.Ordinal);
    }

    private async Task<ModulusSamlRequest?> ConsumeAsync(string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.Set<ModulusSamlRequest>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == token && r.ExpiresAt >= now, ct);
        if (row is null)
            return null;
        return await db.Set<ModulusSamlRequest>().Where(r => r.Id == token).ExecuteDeleteAsync(ct) == 1 ? row : null;
    }

    private Saml2Configuration Configure(SamlProviderOptions p)
    {
        var config = new Saml2Configuration
        {
            Issuer = Options.EntityId,
            SingleSignOnDestination = new Uri(p.SingleSignOnUrl),
            AllowedIssuer = p.EntityId,
            AudienceRestricted = true,
            // Only the pinned certificates sign for this provider; the chain and revocation of a self-issued IdP certificate mean nothing.
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
            SignAuthnRequest = p.SignRequests,
        };
        config.AllowedAudienceUris.Add(Options.EntityId);
        foreach (var certificate in p.SigningCertificates)
            config.SignatureValidationCertificates.Add(LoadCertificate(certificate));
        if (LoadServiceCertificate() is { } own)
        {
            config.SigningCertificate = own;
            config.DecryptionCertificates.Add(own);
        }

        return config;
    }

    private X509Certificate2? LoadServiceCertificate()
        => string.IsNullOrWhiteSpace(Options.CertificateFile)
            ? null
            : X509CertificateLoader.LoadPkcs12FromFile(Options.CertificateFile, Options.CertificatePassword);

    internal static X509Certificate2 LoadCertificate(string value)
    {
        value = value.Trim();
        // A file path, or the Base64 DER that metadata files carry.
        return File.Exists(value)
            ? X509CertificateLoader.LoadCertificateFromFile(value)
            : X509CertificateLoader.LoadCertificate(Convert.FromBase64String(string.Concat(value.Where(c => !char.IsWhiteSpace(c)))));
    }

    private SamlProviderOptions? Find(string name)
        => Options.Providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private string AcsUrl(SamlProviderOptions p)
        => $"{Options.BaseUrl.TrimEnd('/')}{Options.BasePath.TrimEnd('/')}/{p.Name}/acs";

    internal static bool IsLocal(string url) => Modulus.Core.Http.LocalUrl.IsLocal(url);

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private SamlSignInResult Deny(string reason, Guid? tenantId = null)
    {
        Audit("signin.saml.denied", SecurityAuditOutcomes.Denied, tenantId, null, reason);
        return new SamlSignInResult(false, null, reason);
    }

    private void Audit(string action, string outcome, Guid? tenantId, string? provider, string? reason)
        => services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = outcome,
            TenantId = tenantId,
            Details = new Dictionary<string, string?> { ["provider"] = provider, ["reason"] = reason },
        });
}
