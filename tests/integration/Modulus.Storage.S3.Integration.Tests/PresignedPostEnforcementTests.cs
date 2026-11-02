namespace Modulus.Storage.S3.Integration.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Checks, against a real S3-compatible server, that a presigned POST policy is enforced by the server: the size range,
/// the exact key, the expiry, and the signature (a policy edited to allow more is refused).
/// </summary>
[Collection(MinioCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PresignedPostEnforcementTests(MinioFixture minio)
{
    private const int LimitBytes = 1024;

    [Fact]
    public async Task A_file_within_the_limit_is_stored()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("ok/small.bin", TimeSpan.FromMinutes(5), LimitBytes);

        var response = await SendAsync(post, Bytes(LimitBytes / 2));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await storage.ExistsAsync("ok/small.bin")).Should().BeTrue();
    }

    [Fact]
    public async Task A_file_exactly_at_the_limit_is_stored()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("ok/edge.bin", TimeSpan.FromMinutes(5), LimitBytes);

        var response = await SendAsync(post, Bytes(LimitBytes));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_by_the_server_and_not_stored()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("big/too-large.bin", TimeSpan.FromMinutes(5), LimitBytes);

        var response = await SendAsync(post, Bytes(LimitBytes + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "S3 answers EntityTooLarge with 400");
        (await storage.ExistsAsync("big/too-large.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task A_policy_edited_to_allow_a_bigger_file_is_refused()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("big/tampered.bin", TimeSpan.FromMinutes(5), LimitBytes);
        var policy = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(post.Fields["policy"])))!;
        foreach (var condition in policy["conditions"]!.AsArray())
            if (condition is JsonArray range && range[0]?.GetValue<string>() == "content-length-range")
                range[2] = 10_000_000L;
        var tampered = new Dictionary<string, string>(post.Fields)
        {
            ["policy"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(policy.ToJsonString())),
        };

        var response = await SendAsync(post with { Fields = tampered }, Bytes(LimitBytes * 4));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the signature covers the policy, so an edited one fails");
        (await storage.ExistsAsync("big/tampered.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task A_file_under_another_key_than_the_policy_names_is_refused()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("keys/allowed.bin", TimeSpan.FromMinutes(5), LimitBytes);
        var moved = new Dictionary<string, string>(post.Fields) { ["key"] = "keys/other.bin" };

        var response = await SendAsync(post with { Fields = moved }, Bytes(10));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await storage.ExistsAsync("keys/other.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task A_policy_past_its_expiry_is_refused()
    {
        var storage = Storage();
        var post = await storage.GetPresignedUploadPostAsync("late/expired.bin", TimeSpan.FromSeconds(2), LimitBytes);
        await Task.Delay(TimeSpan.FromSeconds(4));

        var response = await SendAsync(post, Bytes(10));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private S3FileStorage Storage() => new(
        minio.Client(),
        Options.Create(new StorageOptions { BucketName = MinioFixture.Bucket, Endpoint = minio.Endpoint, Region = MinioFixture.Region }),
        minio.Credentials);

    private static byte[] Bytes(int count) => Enumerable.Repeat((byte)7, count).ToArray();

    /// <summary>Posts the policy fields and the file as multipart/form-data; the file is the last field, as S3 requires.</summary>
    private static async Task<HttpResponseMessage> SendAsync(PresignedPost post, byte[] file)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in post.Fields)
            form.Add(new StringContent(value, Encoding.UTF8), name);
        var content = new ByteArrayContent(file);
        form.Add(content, "file", "upload.bin");

        using var http = new HttpClient();
        return await http.PostAsync(post.Url, form);
    }
}
