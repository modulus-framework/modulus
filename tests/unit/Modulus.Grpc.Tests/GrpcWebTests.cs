namespace Modulus.Grpc.Tests;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Google.Protobuf;
using Modulus.Grpc.Tests.Protos;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GrpcWebTests
{
    [Fact]
    public async Task Grpc_web_calls_are_served_when_enabled()
    {
        await using var host = await GrpcTestHost.StartAsync(new Dictionary<string, string?> { ["Grpc:EnableGrpcWeb"] = "true" });

        var response = await Post(host);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/grpc-web");
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Length.Should().BeGreaterThan(5);
        System.Text.Encoding.UTF8.GetString(body).Should().Contain("hello");
    }

    [Fact]
    public async Task Grpc_web_calls_are_refused_when_disabled()
    {
        await using var host = await GrpcTestHost.StartAsync();

        var response = await Post(host);

        // Not translated, so the HTTP/1.1 gRPC call fails the protocol check.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> Post(GrpcTestHost host)
    {
        var message = new EchoRequest { Text = "hello" }.ToByteArray();
        var frame = new byte[5 + message.Length];
        frame[1] = (byte)(message.Length >> 24);
        frame[2] = (byte)(message.Length >> 16);
        frame[3] = (byte)(message.Length >> 8);
        frame[4] = (byte)message.Length;
        message.CopyTo(frame, 5);

        var content = new ByteArrayContent(frame);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc-web");
        var request = new HttpRequestMessage(HttpMethod.Post, "/modulus.tests.Probe/Echo") { Content = content };
        request.Headers.Add("x-grpc-web", "1");
        return await host.Server.CreateClient().SendAsync(request);
    }
}
