using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using FluentAssertions;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens.Saml2;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// SAML 2.0 sign-in end to end on a real host: the browser leaves for a (simulated) identity provider that signs responses with
/// ITfoxtec, comes back to the assertion consumer, and ends with the Identity cookie and an authorization-code token.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SamlHttpTests : OidcHostTestBase
{
    private const string SpEntityId = "https://erp.example.test/saml";
    private const string OktaEntity = "https://idp.example.test/okta";
    private const string EntraEntity = "https://sts.example.test/entra";
    private const string AcsOkta = "https://localhost/saml/okta/acs";
    private const string AcsEntra = "https://localhost/saml/entra/acs";

    private static readonly X509Certificate2 s_oktaKey = NewCertificate("okta");
    private static readonly X509Certificate2 s_entraKey = NewCertificate("entra");
    private static readonly X509Certificate2 s_strangerKey = NewCertificate("stranger");

    private static X509Certificate2 NewCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, "x"), "x", X509KeyStorageFlags.Exportable);
    }

    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:Saml:Enabled"] = "true";
        settings["Identity:Saml:EntityId"] = SpEntityId;
        settings["Identity:Saml:BaseUrl"] = "https://localhost";
        settings["Identity:Saml:Providers:0:Name"] = "okta";
        settings["Identity:Saml:Providers:0:EntityId"] = OktaEntity;
        settings["Identity:Saml:Providers:0:SingleSignOnUrl"] = "https://idp.example.test/sso";
        settings["Identity:Saml:Providers:0:SigningCertificates:0"] = Convert.ToBase64String(s_oktaKey.RawData);
        settings["Identity:Saml:Providers:0:GroupRoles:erp-planners"] = "Planner";
        settings["Identity:Saml:Providers:1:Name"] = "entra";
        settings["Identity:Saml:Providers:1:EntityId"] = EntraEntity;
        settings["Identity:Saml:Providers:1:SingleSignOnUrl"] = "https://sts.example.test/sso";
        settings["Identity:Saml:Providers:1:SigningCertificates:0"] = Convert.ToBase64String(s_entraKey.RawData);
        settings["Identity:Saml:Providers:1:MfaAuthnContexts:0"] = "urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport";
    }

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddModulusSaml<ModulusIdentityDbContext, ModulusUser>(Configuration);

    protected override void ConfigureEndpoints(Microsoft.AspNetCore.Builder.WebApplication app) => app.MapModulusSaml();

    [Fact]
    public async Task A_person_signs_in_at_the_identity_provider_and_gets_an_account_and_tokens()
    {
        var (startResponse, requestId, relay) = await BeginAsync("okta", "/connect/authorize?client_id=spa");
        var start = startResponse.Headers;

        // The browser is sent to the identity provider with an authentication request that names this server.
        start.Location!.GetLeftPart(UriPartial.Path).Should().Be("https://idp.example.test/sso");
        var request = AuthnRequestXml(start.Location);
        request.Should().Contain(SpEntityId).And.Contain(AcsOkta);

        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "okta-subject-1",
            ("email", "pat@example.test"), ("givenName", "Pat"), ("surname", "Planner"), ("groups", "erp-planners"));
        var done = await PostAsync("okta", response, relay);

        done.StatusCode.Should().Be(HttpStatusCode.Redirect, await done.Content.ReadAsStringAsync());
        done.Headers.Location!.ToString().Should().Be("/connect/authorize?client_id=spa");
        var cookie = CookieOf(done);

        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var pat = await users.FindByEmailAsync("pat@example.test");
        pat.Should().NotBeNull();
        pat!.FirstName.Should().Be("Pat");
        pat.EmailConfirmed.Should().BeTrue();
        (await users.GetRolesAsync(pat)).Should().Equal("Planner");
        (await users.HasPasswordAsync(pat)).Should().BeFalse("the identity provider is the only way in");

        // The cookie is a normal Identity sign-in: the authorization endpoint issues a code and the token says how the person signed in.
        var jwt = Jwt(await RedeemAsync(cookie));
        Amr(jwt).Should().Equal("fed");
        jwt.GetClaim("sub").Value.Should().Be(pat.Id.ToString());
    }

    [Fact]
    public async Task The_same_person_signing_in_again_reuses_the_account_and_roles_follow_the_groups()
    {
        await SignInAsync("okta", OktaEntity, s_oktaKey, AcsOkta, "okta-subject-1", ("email", "pat@example.test"), ("groups", "erp-planners"));
        await SignInAsync("okta", OktaEntity, s_oktaKey, AcsOkta, "okta-subject-1", ("email", "pat@example.test"));

        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        (await users.Users.CountAsync(u => u.Email == "pat@example.test")).Should().Be(1);
        (await users.GetRolesAsync((await users.FindByEmailAsync("pat@example.test"))!)).Should().BeEmpty("the group was left");
    }

    [Fact]
    public async Task A_response_is_accepted_once()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "s1", ("email", "a@example.test"));

        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_answer_posted_by_a_browser_that_did_not_start_the_sign_in_is_refused()
    {
        // An attacker signs in at the IdP and gets an answer for their own request, then lures the victim's browser to post it.
        // That browser never started the request, so it holds no binding cookie and the answer is refused.
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "attacker", ("email", "a@example.test"));

        using var victim = NewClient();
        (await victim.PostAsync("/saml/okta/acs", Form(("SAMLResponse", response), ("RelayState", relay))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The refusal happens before the answer is consumed, so the browser that did start the sign-in still gets through.
        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task A_response_that_is_not_signed_by_the_provider_is_refused()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");

        var stranger = IdpResponse(OktaEntity, s_strangerKey, requestId, relay, AcsOkta, "s1", ("email", "a@example.test"));
        (await PostAsync("okta", stranger, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The refused attempt used up the request: the genuine answer cannot be replayed into it either.
        await EnsureNoAccountAsync("a@example.test");
    }

    [Fact]
    public async Task A_tampered_response_is_refused()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var genuine = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "s1", ("email", "pat@example.test"));
        var xml = Encoding.UTF8.GetString(Convert.FromBase64String(genuine));

        // Someone changes the e-mail address after it was signed.
        var forged = xml.Replace("pat@example.test", "admin@example.test", StringComparison.Ordinal);
        forged.Should().NotBe(xml);

        (await PostAsync("okta", Convert.ToBase64String(Encoding.UTF8.GetBytes(forged)), relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await EnsureNoAccountAsync("admin@example.test");
    }

    [Fact]
    public async Task An_unsigned_response_is_refused()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var unsigned = IdpResponse(OktaEntity, null, requestId, relay, AcsOkta, "s1", ("email", "a@example.test"));

        (await PostAsync("okta", unsigned, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await EnsureNoAccountAsync("a@example.test");
    }

    [Fact]
    public async Task An_answer_to_a_request_this_server_never_made_is_refused()
    {
        var response = IdpResponse(OktaEntity, s_oktaKey, "_made-up", "made-up-relay", AcsOkta, "s1", ("email", "a@example.test"));

        (await PostAsync("okta", response, "made-up-relay")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_answer_to_another_request_is_refused()
    {
        var (_, _, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, "_some-other-request", relay, AcsOkta, "s1", ("email", "a@example.test"));

        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await EnsureNoAccountAsync("a@example.test");
    }

    [Fact]
    public async Task One_provider_cannot_answer_for_another()
    {
        var (_, requestId, relay) = await BeginAsync("entra", "/");
        // Okta, signing with its own key, answers a sign-in that started at Entra.
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsEntra, "s1", ("email", "a@example.test"));

        (await PostAsync("entra", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (_, requestId2, relay2) = await BeginAsync("okta", "/");
        var wrongDoor = IdpResponse(OktaEntity, s_oktaKey, requestId2, relay2, AcsOkta, "s1", ("email", "a@example.test"));
        (await PostAsync("entra", wrongDoor, relay2)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the relay state belongs to the okta sign-in");
    }

    [Fact]
    public async Task A_response_for_another_service_provider_is_refused()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "s1", [("email", "a@example.test")], "https://other-app.example.test");

        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_sign_in_that_took_too_long_is_refused()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "s1", ("email", "a@example.test"));
        Clock.Advance(TimeSpan.FromMinutes(11));

        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_second_factor_the_provider_reports_reaches_the_token()
    {
        var (_, requestId, relay) = await BeginAsync("entra", "/");
        var response = IdpResponse(EntraEntity, s_entraKey, requestId, relay, AcsEntra, "e1", ("email", "eva@example.test"));
        var done = await PostAsync("entra", response, relay);
        done.StatusCode.Should().Be(HttpStatusCode.Redirect, await done.Content.ReadAsStringAsync());

        Amr(Jwt(await RedeemAsync(CookieOf(done)))).Should().BeEquivalentTo("fed", "mfa");
    }

    [Fact]
    public async Task A_person_without_an_e_mail_address_or_provisioning_gets_nothing()
    {
        var (_, requestId, relay) = await BeginAsync("okta", "/");
        var response = IdpResponse(OktaEntity, s_oktaKey, requestId, relay, AcsOkta, "s1", ("department", "Planning"));

        (await PostAsync("okta", response, relay)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_login_endpoint_only_accepts_local_return_addresses_and_known_providers()
    {
        using var client = NewClient();
        (await client.GetAsync("/saml/okta/login?returnUrl=https://evil.example.test/")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/saml/okta/login?returnUrl=//evil.example.test/")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/saml/okta/login?returnUrl=/\\evil.example.test/")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/saml/nobody/login")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/saml/okta/login")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Metadata_and_the_provider_list_are_public()
    {
        using var client = NewClient();
        var metadata = await client.GetAsync("/saml/okta/metadata");
        metadata.StatusCode.Should().Be(HttpStatusCode.OK);
        var xml = await metadata.Content.ReadAsStringAsync();
        xml.Should().Contain(SpEntityId).And.Contain(AcsOkta);
        new XmlDocument().LoadXml(xml.TrimStart());

        var list = await client.GetStringAsync("/saml");
        list.Should().Contain("okta").And.Contain("entra");
    }

    // ── helpers ──────────────────────────────────────────────────────

    // The binding cookie each sign-in's start response set, keyed by its relay state: a test posts the answer with it, as a browser would.
    private readonly Dictionary<string, string> _bindings = new();

    private async Task<(HttpResponseMessage Response, string RequestId, string Relay)> BeginAsync(string provider, string returnUrl)
    {
        using var client = NewClient();
        var response = await client.GetAsync($"/saml/{provider}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var query = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        var requestId = Regex.Match(AuthnRequestXml(response.Headers.Location), "ID=\"([^\"]+)\"").Groups[1].Value;
        var relay = query["RelayState"]!;
        _bindings[relay] = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".modulus.saml=", StringComparison.Ordinal)).Split(';')[0];
        return (response, requestId, relay);
    }

    private static string AuthnRequestXml(Uri location)
    {
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        using var deflate = new System.IO.Compression.DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"]!)), System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(deflate);
        return reader.ReadToEnd();
    }

    private async Task<HttpResponseMessage> PostAsync(string provider, string samlResponse, string relay)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/saml/{provider}/acs")
        {
            Content = Form(("SAMLResponse", samlResponse), ("RelayState", relay)),
        };
        if (_bindings.TryGetValue(relay, out var binding))
            request.Headers.Add("Cookie", binding);
        return await client.SendAsync(request);
    }

    private async Task SignInAsync(string provider, string issuer, X509Certificate2 key, string acs, string subject, params (string Name, string Value)[] attributes)
    {
        var (_, requestId, relay) = await BeginAsync(provider, "/");
        var response = IdpResponse(issuer, key, requestId, relay, acs, subject, attributes, SpEntityId);
        (await PostAsync(provider, response, relay)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private static string CookieOf(HttpResponseMessage response)
        => string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

    private async Task<System.Text.Json.JsonElement> RedeemAsync(string cookie)
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/connect/authorize?client_id={SpaClient}&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope=openid%20offline_access%20roles&state=xyz&code_challenge={challenge}&code_challenge_method=S256");
        request.Headers.Add("Cookie", cookie);
        var authorized = await client.SendAsync(request);
        authorized.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorized.Content.ReadAsStringAsync());
        var code = System.Web.HttpUtility.ParseQueryString(authorized.Headers.Location!.Query)["code"]!;
        var (status, body) = await TokenAsync(
            ("grant_type", "authorization_code"), ("client_id", SpaClient), ("code", code), ("redirect_uri", RedirectUri), ("code_verifier", verifier));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body;
    }

    private async Task EnsureNoAccountAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>().FindByEmailAsync(email)).Should().BeNull();
    }

    /// <summary>What an identity provider posts back: a SAML response, signed with <paramref name="key"/> (unsigned when null), as the Base64 form value.</summary>
    private static string IdpResponse(
        string issuer, X509Certificate2? key, string inResponseTo, string relay, string destination, string subject,
        params (string Name, string Value)[] attributes)
        => IdpResponse(issuer, key, inResponseTo, relay, destination, subject, attributes, SpEntityId);

    private static string IdpResponse(
        string issuer, X509Certificate2? key, string inResponseTo, string relay, string destination, string subject,
        (string Name, string Value)[] attributes, string audience)
    {
        var config = new Saml2Configuration
        {
            Issuer = issuer,
            SingleSignOnDestination = new Uri("https://idp.example.test/sso"),
            SigningCertificate = key ?? s_strangerKey,
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        var response = new Saml2AuthnResponse(config)
        {
            Status = Saml2StatusCodes.Success,
            InResponseTo = new Saml2Id(inResponseTo),
            Destination = new Uri(destination),
            NameId = new Saml2NameIdentifier(subject, new Uri("urn:oasis:names:tc:SAML:2.0:nameid-format:persistent")),
        };
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        claims.AddRange(attributes.Select(a => new Claim(a.Name, a.Value)));
        response.ClaimsIdentity = new ClaimsIdentity(claims);
        response.CreateSecurityToken(audience, subjectConfirmationLifetime: 5, issuedTokenLifetime: 60);

        var binding = new Saml2PostBinding { RelayState = relay };
        binding.Bind(response);
        var html = binding.PostContent;
        var value = Regex.Match(html, "name=\"SAMLResponse\" value=\"([^\"]+)\"").Groups[1].Value;
        if (key is null)
        {
            // Strip the signature: an unsigned response the provider "forgot" to sign.
            var xml = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            xml = Regex.Replace(xml, "<(\\w+:)?Signature .*?</(\\w+:)?Signature>", "", RegexOptions.Singleline);
            value = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));
        }

        return value;
    }
}
