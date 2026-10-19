using System.Net;
using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Xunit;

namespace Modulus.Secrets.OpenBao.Tests;

[Trait("Category", "Unit")]
public sealed class OpenBaoKvClientTests : IDisposable
{
    private static readonly SecretReference Primary = SecretReference.Parse("tenant-db/3f2c0000/primary");
    private readonly TempFile _token = new("tok-1");

    public void Dispose() => _token.Dispose();

    private (OpenBaoKvClient Kv, ScriptedHandler Handler) Create(Func<SentRequest, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);
        var options = Harness.Options(new OpenBaoAuthOptions { Method = OpenBaoAuthMethod.TokenFile, TokenPath = _token.FilePath });
        return (Harness.Kv(handler, options, TimeProvider.System), handler);
    }

    [Fact]
    public async Task ReadAsync_ParsesKvV2Response()
    {
        var (kv, handler) = Create(_ => Respond.Json(HttpStatusCode.OK,
            """{"data":{"data":{"connectionString":"Host=a","port":5432,"tls":true},"metadata":{"version":4}}}"""));

        var value = await kv.ReadAsync(Primary, CancellationToken.None);

        value.Version.Should().Be(4);
        value.Get("connectionString").Should().Be("Host=a");
        value.Data["port"].Should().Be("5432");
        value.Data["tls"].Should().Be("true");
        var sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Method.Should().Be("GET");
        sent.Path.Should().Be("/v1/modulus/data/tenant-db/3f2c0000/primary");
        sent.Token.Should().Be("tok-1");
    }

    [Fact]
    public async Task ReadAsync_MissingSecretIsNotFound()
    {
        var (kv, _) = Create(_ => Respond.Status(HttpStatusCode.NotFound));

        Func<Task> act = () => kv.ReadAsync(Primary, CancellationToken.None);

        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_NullDataIsNotFound()
    {
        var (kv, _) = Create(_ => Respond.Json(HttpStatusCode.OK, """{"data":null}"""));

        Func<Task> act = () => kv.ReadAsync(Primary, CancellationToken.None);

        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task ReadAsync_ServerErrorIsUnavailable(int status)
    {
        var (kv, _) = Create(_ => Respond.Status((HttpStatusCode)status));

        Func<Task> act = () => kv.ReadAsync(Primary, CancellationToken.None);

        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }

    [Fact]
    public async Task ReadAsync_TransportFailureIsUnavailable()
    {
        var (kv, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Func<Task> act = () => kv.ReadAsync(Primary, CancellationToken.None);

        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }

    [Fact]
    public async Task ReadAsync_OutsideNamespaceIsRefusedBeforeAnyRequest()
    {
        var (kv, handler) = Create(_ => Respond.Status(HttpStatusCode.OK));
        var outside = SecretReference.Parse("other/3f2c0000/primary");

        Func<Task> act = () => kv.ReadAsync(outside, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ForbiddenReloginsOnceAndRetries()
    {
        var calls = 0;
        var (kv, handler) = Create(_ =>
        {
            calls++;
            if (calls == 1)
            {
                // The token was rotated on the host: the next login reads the new one from the file.
                _token.Write("tok-2");
                return Respond.Status(HttpStatusCode.Forbidden);
            }

            return Respond.Json(HttpStatusCode.OK, """{"data":{"data":{"k":"v"},"metadata":{"version":1}}}""");
        });

        var value = await kv.ReadAsync(Primary, CancellationToken.None);

        value.Version.Should().Be(1);
        handler.Sent.Should().HaveCount(2);
        handler.Sent[0].Token.Should().Be("tok-1");
        handler.Sent[1].Token.Should().Be("tok-2");
    }

    [Fact]
    public async Task ReadAsync_SecondForbiddenIsAPolicyDenial()
    {
        var (kv, handler) = Create(_ => Respond.Status(HttpStatusCode.Forbidden));

        Func<Task> act = () => kv.ReadAsync(Primary, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task WriteAsync_NullExpectedVersionWritesUnconditionally()
    {
        var (kv, handler) = Create(_ => Respond.Json(HttpStatusCode.OK, """{"data":{"version":5}}"""));

        var version = await kv.WriteAsync(Primary, new Dictionary<string, string> { ["connectionString"] = "x" }, null, CancellationToken.None);

        version.Should().Be(5);
        var sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Method.Should().Be("POST");
        sent.Path.Should().Be("/v1/modulus/data/tenant-db/3f2c0000/primary");
        sent.Body.Should().Contain("\"data\"").And.NotContain("options");
    }

    [Fact]
    public async Task WriteAsync_ZeroExpectedVersionIsCreateOnly()
    {
        var (kv, handler) = Create(_ => Respond.Json(HttpStatusCode.OK, """{"data":{"version":1}}"""));

        await kv.WriteAsync(Primary, new Dictionary<string, string>(), 0, CancellationToken.None);

        handler.Sent.Single().Body.Should().Contain("\"cas\":0");
    }

    [Fact]
    public async Task WriteAsync_CheckAndSetMismatchIsConflict()
    {
        var (kv, _) = Create(_ => Respond.Json(HttpStatusCode.BadRequest,
            """{"errors":["check-and-set parameter did not match the current version"]}"""));

        Func<Task> act = () => kv.WriteAsync(Primary, new Dictionary<string, string>(), 3, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task WriteAsync_OtherBadRequestIsAFault()
    {
        var (kv, _) = Create(_ => Respond.Json(HttpStatusCode.BadRequest, """{"errors":["bad input"]}"""));

        Func<Task> act = () => kv.WriteAsync(Primary, new Dictionary<string, string>(), null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCurrentVersionAsync_ReadsMetadata()
    {
        var (kv, handler) = Create(_ => Respond.Json(HttpStatusCode.OK, """{"data":{"current_version":7}}"""));

        var version = await kv.GetCurrentVersionAsync(Primary, CancellationToken.None);

        version.Should().Be(7);
        handler.Sent.Single().Path.Should().Be("/v1/modulus/metadata/tenant-db/3f2c0000/primary");
    }

    [Fact]
    public async Task GetCurrentVersionAsync_AbsentSecretIsNull()
    {
        var (kv, _) = Create(_ => Respond.Status(HttpStatusCode.NotFound));

        var version = await kv.GetCurrentVersionAsync(Primary, CancellationToken.None);

        version.Should().BeNull();
    }
}
