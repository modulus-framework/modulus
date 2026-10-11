using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Modulus.Authorization;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Modulus.Identity.Tests;

/// <summary>
/// The token endpoint over real HTTP: a full OpenIddict server and validation stack on a TestServer (see <see cref="OidcHostTestBase"/>).
/// Each grant is exercised the way a client calls it, and the issued bearer tokens are used against protected endpoints, including step-up.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenEndpointHttpTests : OidcHostTestBase
{
    [Fact]
    public async Task The_password_grant_issues_a_bearer_token_that_records_how_and_when_the_person_signed_in()
    {
        var user = await CreateUserAsync("ann");

        var (status, body) = await PasswordAsync("ann");

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
        body.TryGetProperty("refresh_token", out _).Should().BeTrue("offline_access was asked for and allowed");
        var jwt = Jwt(body);
        jwt.GetClaim("sub").Value.Should().Be(user.Id.ToString());
        Amr(jwt).Should().Equal("pwd");
        long.Parse(jwt.GetClaim("auth_time").Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 30);

        var whoami = await CallAsync(HttpMethod.Get, "/whoami", body.GetProperty("access_token").GetString());
        whoami.StatusCode.Should().Be(HttpStatusCode.OK);
        (await whoami.Content.ReadAsStringAsync()).Should().Be(user.Id.ToString());
        (await CallAsync(HttpMethod.Get, "/whoami", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_user_and_a_disabled_account_all_get_the_same_invalid_grant()
    {
        await CreateUserAsync("ann");
        await CreateUserAsync("dan");
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var dan = (await users.FindByNameAsync("dan"))!;
            dan.IsActive = false;
            await users.UpdateAsync(dan);
        }

        foreach (var attempt in new[] { ("ann", "wrong-Passw0rd!"), ("nobody", Password), ("dan", Password) })
        {
            var (status, body) = await PasswordAsync(attempt.Item1, attempt.Item2);
            status.Should().Be(HttpStatusCode.BadRequest);
            body.GetProperty("error").GetString().Should().Be("invalid_grant");
            body.TryGetProperty("access_token", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_client_that_may_not_use_a_grant_is_refused_and_so_is_an_unknown_grant_type()
    {
        await CreateUserAsync("ann");
        var (status, body) = await TokenAsync(("grant_type", "refresh_token"), ("client_id", "limited"), ("refresh_token", "x"));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().BeOneOf("unauthorized_client", "invalid_grant");

        var (passkeyStatus, passkeyBody) = await TokenAsync(("grant_type", PasskeyGrant.GrantType), ("client_id", "limited"));
        passkeyStatus.Should().Be(HttpStatusCode.BadRequest);
        passkeyBody.GetProperty("error").GetString().Should().BeOneOf("unauthorized_client", "unsupported_grant_type");

        var (_, unknown) = await TokenAsync(("grant_type", "urn:nope"), ("client_id", SpaClient));
        unknown.GetProperty("error").GetString().Should().BeOneOf("unsupported_grant_type", "unauthorized_client");
    }

    [Fact]
    public async Task An_account_with_two_factor_needs_the_code_and_the_token_then_says_mfa()
    {
        var user = await CreateUserAsync("ann");
        string key;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var twoFactor = scope.ServiceProvider.GetRequiredService<IUserTwoFactorService>();
            var setup = (await twoFactor.BeginSetupAsync(user.Id))!;
            key = setup.SharedKey;
            (await twoFactor.EnableAsync(user.Id, Totp(key))).Should().NotBeNull();
        }

        var (missingStatus, missing) = await PasswordAsync("ann");
        missingStatus.Should().Be(HttpStatusCode.BadRequest);
        missing.GetProperty("error_description").GetString().Should().Contain("mfa_code");

        (await PasswordAsync("ann", mfa: "000000")).Status.Should().Be(HttpStatusCode.BadRequest);

        var (status, body) = await PasswordAsync("ann", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Amr(Jwt(body)).Should().BeEquivalentTo("pwd", "otp", "mfa");
    }

    [Fact]
    public async Task A_refresh_keeps_the_original_sign_in_and_stops_when_the_account_changes()
    {
        var user = await CreateUserAsync("ann", "Planner");
        var (_, first) = await PasswordAsync("ann");
        var firstJwt = Jwt(first);

        var (status, refreshed) = await TokenAsync(
            ("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", first.GetProperty("refresh_token").GetString()!));
        status.Should().Be(HttpStatusCode.OK, refreshed.ToString());
        var refreshedJwt = Jwt(refreshed);
        refreshedJwt.GetClaim("auth_time").Value.Should().Be(firstJwt.GetClaim("auth_time").Value, "a refresh is not a new sign-in");
        Amr(refreshedJwt).Should().Equal(Amr(firstJwt));

        // A password reset changes the security stamp: the refresh token is no longer good.
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            (await users.UpdateSecurityStampAsync(stored)).Succeeded.Should().BeTrue();
        }

        var (staleStatus, stale) = await TokenAsync(
            ("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", refreshed.GetProperty("refresh_token").GetString()!));
        staleStatus.Should().Be(HttpStatusCode.BadRequest);
        stale.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task A_refresh_token_is_refused_once_the_account_is_disabled()
    {
        var user = await CreateUserAsync("ann");
        var (_, first) = await PasswordAsync("ann");

        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            stored.IsActive = false;
            await users.UpdateAsync(stored);
        }

        var (status, body) = await TokenAsync(
            ("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", first.GetProperty("refresh_token").GetString()!));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Shop_floor_sign_in_works_only_from_a_registered_device_and_gives_a_short_token_without_refresh()
    {
        await using (var scope = _app.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IShopFloorAccountService>().CreateAsync(s_company, "E1", "482913", roles: ["ShopFloor"])).Succeeded.Should().BeTrue();

        var (status, body) = await TokenAsync(
            ("grant_type", ShopFloorGrant.GrantType), ("client_id", Tablet), ("employee_code", "e1"), ("pin", "482913"), ("scope", "openid offline_access roles"));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        body.TryGetProperty("refresh_token", out _).Should().BeFalse("a tablet token cannot be refreshed");
        var jwt = Jwt(body);
        Amr(jwt).Should().Equal("pin");
        jwt.GetClaim("tid").Value.Should().Be(s_company.ToString());
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Equal("ShopFloor");

        var (wrongStatus, wrong) = await TokenAsync(("grant_type", ShopFloorGrant.GrantType), ("client_id", Tablet), ("employee_code", "E1"), ("pin", "000111"));
        wrongStatus.Should().Be(HttpStatusCode.BadRequest);
        wrong.GetProperty("error").GetString().Should().Be("invalid_grant");

        // A client that is not bound to a company is not a registered device.
        var (unboundStatus, unbound) = await TokenAsync(("grant_type", ShopFloorGrant.GrantType), ("client_id", SpaClient), ("employee_code", "E1"), ("pin", "482913"));
        unboundStatus.Should().Be(HttpStatusCode.Unauthorized, "invalid_client is a 401");
        unbound.GetProperty("error").GetString().Should().Be("invalid_client");

        // The tablet token works as a bearer token for what its roles allow.
        (await CallAsync(HttpMethod.Get, "/whoami", body.GetProperty("access_token").GetString())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_imported_operator_chooses_their_own_pin_at_the_first_sign_in()
    {
        string initial;
        await using (var scope = _app.Services.CreateAsyncScope())
            initial = (await scope.ServiceProvider.GetRequiredService<IShopFloorAccountService>().ImportAsync(s_company, [new ShopFloorImportRow("E7", Roles: ["ShopFloor"])]))[0].InitialPin!;

        var (status, body) = await TokenAsync(("grant_type", ShopFloorGrant.GrantType), ("client_id", Tablet), ("employee_code", "E7"), ("pin", initial));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("pin_change_required");

        var (ok, token) = await TokenAsync(("grant_type", ShopFloorGrant.GrantType), ("client_id", Tablet), ("employee_code", "E7"), ("pin", initial), ("new_pin", "739184"), ("scope", "openid roles"));
        ok.Should().Be(HttpStatusCode.OK, token.ToString());

        var (again, _) = await TokenAsync(("grant_type", ShopFloorGrant.GrantType), ("client_id", Tablet), ("employee_code", "E7"), ("pin", "739184"));
        again.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_critical_permission_asks_for_step_up_and_a_second_factor_or_a_passkey_satisfies_it()
    {
        var user = await CreateUserAsync("tess", "Treasurer");

        // Password only: the permission is held, but the sign-in is not multi-factor.
        var (_, passwordOnly) = await PasswordAsync("tess");
        var refused = await CallAsync(HttpMethod.Post, "/pay", passwordOnly.GetProperty("access_token").GetString());
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.WwwAuthenticate.ToString().Should().Contain("insufficient_user_authentication").And.Contain("acr_values=\"mfa\"");

        // Register a passkey with the (recent) password token, sign in with it, and pay.
        var device = new SoftwareAuthenticator();
        var bearer = passwordOnly.GetProperty("access_token").GetString()!;
        using (var client = NewClient())
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            var begin = await (await client.PostAsync("/account/passkeys/register/options", null)).Content.ReadFromJsonAsync<JsonElement>();
            var complete = await client.PostAsJsonAsync("/account/passkeys/register/complete", new
            {
                challengeId = begin.GetProperty("challengeId").GetGuid(),
                name = "Laptop",
                credential = JsonDocument.Parse(device.Register(begin.GetProperty("options"))).RootElement,
            });
            complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        }

        using var anonymous = NewClient();
        var login = await (await anonymous.PostAsync("/account/passkeys/login/options", null)).Content.ReadFromJsonAsync<JsonElement>();
        var (status, body) = await TokenAsync(
            ("grant_type", PasskeyGrant.GrantType), ("client_id", SpaClient), ("scope", "openid offline_access roles"),
            ("challenge_id", login.GetProperty("challengeId").GetGuid().ToString()),
            ("assertion", device.Assert(login.GetProperty("options"), user.Id)));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Amr(Jwt(body)).Should().BeEquivalentTo("hwk", "user", "mfa");
        (await CallAsync(HttpMethod.Post, "/pay", body.GetProperty("access_token").GetString())).StatusCode.Should().Be(HttpStatusCode.OK);

        // The answer cannot be replayed, and a refresh of the passkey session keeps its original strength.
        var (replayStatus, replay) = await TokenAsync(
            ("grant_type", PasskeyGrant.GrantType), ("client_id", SpaClient),
            ("challenge_id", login.GetProperty("challengeId").GetGuid().ToString()),
            ("assertion", device.Assert(login.GetProperty("options"), user.Id)));
        replayStatus.Should().Be(HttpStatusCode.BadRequest);
        replay.GetProperty("error").GetString().Should().Be("invalid_grant");

        var (_, refreshed) = await TokenAsync(
            ("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", body.GetProperty("refresh_token").GetString()!));
        Amr(Jwt(refreshed)).Should().BeEquivalentTo("hwk", "user", "mfa");
    }

    [Fact]
    public async Task A_passkey_grant_without_its_parameters_is_an_invalid_request()
    {
        var (status, body) = await TokenAsync(("grant_type", PasskeyGrant.GrantType), ("client_id", SpaClient));
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    private static string Pkce(out string challenge)
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        challenge = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return verifier;
    }

    private async Task<string> LoginCookieAsync(string name)
    {
        using var client = NewClient();
        var response = await client.GetAsync($"/test/login/{name}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    private async Task<HttpResponseMessage> AuthorizeAsync(string? cookie, string? challenge, string extra = "")
    {
        using var client = NewClient();
        var query = $"/connect/authorize?client_id={SpaClient}&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + "&scope=openid%20offline_access%20roles&state=xyz"
            + (challenge is null ? "" : $"&code_challenge={challenge}&code_challenge_method=S256") + extra;
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static System.Collections.Specialized.NameValueCollection QueryOf(HttpResponseMessage response)
        => System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query);

    [Fact]
    public async Task The_authorization_code_flow_signs_in_through_the_cookie_and_redeems_with_pkce_once()
    {
        await CreateUserAsync("ann");
        var verifier = Pkce(out var challenge);

        // Not signed in: sent to the login page, which returns here.
        var anonymous = await AuthorizeAsync(null, challenge);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Redirect);
        anonymous.Headers.Location!.ToString().ToLowerInvariant().Should().Contain("/account/login").And.Contain("returnurl");

        // prompt=none cannot show a login page: the client is told so through its redirect URI.
        var silent = await AuthorizeAsync(null, challenge, "&prompt=none");
        QueryOf(silent)["error"].Should().Be("login_required");

        var cookie = await LoginCookieAsync("ann");
        var authorized = await AuthorizeAsync(cookie, challenge);
        authorized.StatusCode.Should().Be(HttpStatusCode.Redirect, await authorized.Content.ReadAsStringAsync());
        authorized.Headers.Location!.ToString().Should().StartWith(RedirectUri);
        QueryOf(authorized)["state"].Should().Be("xyz");
        var code = QueryOf(authorized)["code"]!;

        // The wrong verifier is refused (and burns nothing the right client needs: the code is a different one below).
        var (wrongStatus, wrong) = await TokenAsync(
            ("grant_type", "authorization_code"), ("client_id", SpaClient), ("code", code), ("redirect_uri", RedirectUri), ("code_verifier", "not-the-verifier-not-the-verifier-not-the-verifier"));
        wrongStatus.Should().Be(HttpStatusCode.BadRequest);
        wrong.GetProperty("error").GetString().Should().Be("invalid_grant");

        var second = QueryOf(await AuthorizeAsync(cookie, challenge))["code"]!;
        var (status, body) = await TokenAsync(
            ("grant_type", "authorization_code"), ("client_id", SpaClient), ("code", second), ("redirect_uri", RedirectUri), ("code_verifier", verifier));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        var jwt = Jwt(body);
        Amr(jwt).Should().Equal("pwd");
        jwt.GetClaim("auth_time").Value.Should().NotBeNullOrEmpty();
        (await CallAsync(HttpMethod.Get, "/whoami", body.GetProperty("access_token").GetString())).StatusCode.Should().Be(HttpStatusCode.OK);

        // A code works once.
        var (replayStatus, replay) = await TokenAsync(
            ("grant_type", "authorization_code"), ("client_id", SpaClient), ("code", second), ("redirect_uri", RedirectUri), ("code_verifier", verifier));
        replayStatus.Should().Be(HttpStatusCode.BadRequest);
        replay.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task The_authorization_endpoint_refuses_a_request_without_pkce_or_with_another_redirect_uri()
    {
        await CreateUserAsync("ann");
        var cookie = await LoginCookieAsync("ann");

        var noPkce = await AuthorizeAsync(cookie, null);
        noPkce.StatusCode.Should().Be(HttpStatusCode.BadRequest, "PKCE is mandatory: no code_challenge is an error, never a code");
        (await noPkce.Content.ReadAsStringAsync()).Should().Contain("code_challenge");

        Pkce(out var challenge);
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/connect/authorize?client_id={SpaClient}&response_type=code&redirect_uri={Uri.EscapeDataString("https://evil.example.test/steal")}&scope=openid&code_challenge={challenge}&code_challenge_method=S256");
        request.Headers.Add("Cookie", cookie);
        var wrongRedirect = await client.SendAsync(request);
        wrongRedirect.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a redirect URI that is not registered is never redirected to");
    }

    [Fact]
    public async Task The_federated_exchange_creates_or_links_the_account_and_records_a_federated_sign_in()
    {
        var ann = await CreateUserAsync("ann");

        // A new person: the account is created from the external identity.
        var (status, body) = await TokenAsync(("grant_type", FederatedLoginGrant.GrantType), ("client_id", SpaClient), ("subject_token", "good-token"), ("scope", "openid roles"));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Amr(Jwt(body)).Should().Equal("fed");
        Jwt(body).GetClaim("sub").Value.Should().NotBe(ann.Id.ToString());

        // The same external subject comes back to the same account.
        var (_, again) = await TokenAsync(("grant_type", FederatedLoginGrant.GrantType), ("client_id", SpaClient), ("subject_token", "good-token"), ("scope", "openid roles"));
        Jwt(again).GetClaim("sub").Value.Should().Be(Jwt(body).GetClaim("sub").Value);

        // A verified email links to the existing local account (LinkByVerifiedEmail is on).
        var (_, linked) = await TokenAsync(("grant_type", FederatedLoginGrant.GrantType), ("client_id", SpaClient), ("subject_token", "link-token"), ("scope", "openid roles"));
        Jwt(linked).GetClaim("sub").Value.Should().Be(ann.Id.ToString());

        // An invalid token, and an email domain that is not allowed, are refused alike.
        foreach (var token in new[] { "garbage", "foreign-token" })
        {
            var (badStatus, bad) = await TokenAsync(("grant_type", FederatedLoginGrant.GrantType), ("client_id", SpaClient), ("subject_token", token));
            badStatus.Should().Be(HttpStatusCode.BadRequest);
            bad.GetProperty("error").GetString().Should().Be("invalid_grant");
        }

        var (missingStatus, missing) = await TokenAsync(("grant_type", FederatedLoginGrant.GrantType), ("client_id", SpaClient));
        missingStatus.Should().Be(HttpStatusCode.BadRequest);
        missing.GetProperty("error").GetString().Should().Be("invalid_request");
    }

    // RFC 6238 (HMAC-SHA1, 30 s, 6 digits) from the base32 key, the algorithm authenticator apps use.
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.ToUpperInvariant().Where(alphabet.Contains).Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0xF;
        var code = (((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3]) % 1_000_000;
        return code.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
