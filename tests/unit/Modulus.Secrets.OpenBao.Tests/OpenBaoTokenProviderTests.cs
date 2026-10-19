using System.Net;
using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Secrets.OpenBao.Tests;

[Trait("Category", "Unit")]
public sealed class OpenBaoTokenProviderTests : IDisposable
{
    private readonly TempFile _jwt = new("jwt-1");

    public void Dispose() => _jwt.Dispose();

    private static OpenBaoAuthOptions Kubernetes(string jwtPath) => new()
    {
        Method = OpenBaoAuthMethod.Kubernetes,
        Role = "modulus-api",
        MountPath = "kubernetes",
        JwtPath = jwtPath,
    };

    [Fact]
    public async Task Login_SendsRoleAndJwtAndReusesTheToken()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Json(HttpStatusCode.OK, Respond.LoginLease900));
        var auth = Kubernetes(_jwt.FilePath);
        var tokens = Harness.Tokens(Harness.Client(handler), auth, clock);

        var first = await tokens.GetAsync(CancellationToken.None);
        var second = await tokens.GetAsync(CancellationToken.None);

        first.Should().Be("tok-a");
        second.Should().Be("tok-a");
        var login = handler.Sent.Should().ContainSingle().Subject;
        login.Path.Should().Be("/v1/auth/kubernetes/login");
        login.Body.Should().Contain("\"role\":\"modulus-api\"").And.Contain("\"jwt\":\"jwt-1\"");
    }

    [Fact]
    public async Task AppRoleLogin_UsesTheApproleMountByDefault()
    {
        using var roleId = new TempFile("role-1");
        using var secretId = new TempFile("secret-1");
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Json(HttpStatusCode.OK, Respond.LoginLease900));
        var auth = new OpenBaoAuthOptions
        {
            Method = OpenBaoAuthMethod.AppRole,
            Role = "modulus-api",
            RoleIdPath = roleId.FilePath,
            SecretIdPath = secretId.FilePath,
        };
        var tokens = Harness.Tokens(Harness.Client(handler), auth, clock);

        await tokens.GetAsync(CancellationToken.None);

        var login = handler.Sent.Should().ContainSingle().Subject;
        login.Path.Should().Be("/v1/auth/approle/login");
        login.Body.Should().Contain("\"role_id\":\"role-1\"").And.Contain("\"secret_id\":\"secret-1\"");
    }

    [Fact]
    public async Task RenewableToken_IsRenewedBeforeItsLeaseEnds()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(sent => sent.Path.EndsWith("renew-self", StringComparison.Ordinal)
            ? Respond.Json(HttpStatusCode.OK, """{"auth":{"lease_duration":900,"renewable":true}}""")
            : Respond.Json(HttpStatusCode.OK, Respond.LoginLease900));
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);
        await tokens.GetAsync(CancellationToken.None);

        // Two thirds of 900 s is 600 s; 610 s in, the lease has 290 s left, so the token is renewed rather than replaced.
        clock.Now = clock.Now.AddSeconds(610);
        var token = await tokens.GetAsync(CancellationToken.None);

        token.Should().Be("tok-a");
        handler.Sent.Select(s => s.Path).Should().Equal("/v1/auth/kubernetes/login", "/v1/auth/token/renew-self");
        handler.Sent[1].Token.Should().Be("tok-a");
    }

    [Fact]
    public async Task NonRenewableToken_LogsInAgainAtTwoThirdsOfItsLease()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var logins = 0;
        var handler = new ScriptedHandler(_ =>
        {
            logins++;
            return Respond.Json(HttpStatusCode.OK, logins == 1
                ? """{"auth":{"client_token":"tok-a","lease_duration":900,"renewable":false}}"""
                : """{"auth":{"client_token":"tok-b","lease_duration":900,"renewable":false}}""");
        });
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);
        await tokens.GetAsync(CancellationToken.None);

        clock.Now = clock.Now.AddSeconds(610);
        var token = await tokens.GetAsync(CancellationToken.None);

        token.Should().Be("tok-b");
        handler.Sent.Should().HaveCount(2);
        handler.Sent.Should().OnlyContain(s => s.Path == "/v1/auth/kubernetes/login");
    }

    [Fact]
    public async Task ExpiredLease_LogsInEvenWhenRenewable()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Json(HttpStatusCode.OK, Respond.LoginLease900));
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);
        await tokens.GetAsync(CancellationToken.None);

        clock.Now = clock.Now.AddSeconds(901);
        await tokens.GetAsync(CancellationToken.None);

        handler.Sent.Should().HaveCount(2);
        handler.Sent.Should().OnlyContain(s => s.Path == "/v1/auth/kubernetes/login");
    }

    [Fact]
    public async Task InvalidateAsync_ForcesALoginOnTheNextCall()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Json(HttpStatusCode.OK, Respond.LoginLease900));
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);
        await tokens.GetAsync(CancellationToken.None);

        await tokens.InvalidateAsync(CancellationToken.None);
        await tokens.GetAsync(CancellationToken.None);

        handler.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoginRefusedByPolicyIsAFault()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Status(HttpStatusCode.Forbidden));
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);

        Func<Task> act = () => tokens.GetAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LoginServerErrorIsUnavailable()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => Respond.Status(HttpStatusCode.ServiceUnavailable));
        var tokens = Harness.Tokens(Harness.Client(handler), Kubernetes(_jwt.FilePath), clock);

        Func<Task> act = () => tokens.GetAsync(CancellationToken.None);

        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }

    [Fact]
    public async Task TokenFile_IsReadAgainOnlyAfterFiveMinutes()
    {
        using var tokenFile = new TempFile("tok-1");
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("No request is expected for a token file."));
        var auth = new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.TokenFile, TokenPath = tokenFile.FilePath };
        var tokens = Harness.Tokens(Harness.Client(handler), auth, clock);
        (await tokens.GetAsync(CancellationToken.None)).Should().Be("tok-1");

        tokenFile.Write("tok-2");
        clock.Now = clock.Now.AddMinutes(4);
        (await tokens.GetAsync(CancellationToken.None)).Should().Be("tok-1");

        clock.Now = clock.Now.AddMinutes(2);
        (await tokens.GetAsync(CancellationToken.None)).Should().Be("tok-2");
        handler.Sent.Should().BeEmpty();
    }
}
