// The AWS SDK's client is mocked; the POST policy is signed locally, so these tests need no network.
namespace Modulus.Storage.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public sealed class S3PresignedPostTests
{
    private const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";

    private static S3FileStorage Create(StorageOptions? options = null, AWSCredentials? credentials = null)
        => new(Substitute.For<IAmazonS3>(),
               Options.Create(options ?? new StorageOptions { BucketName = "uploads", Region = "eu-central-1" }),
               credentials ?? new BasicAWSCredentials(AccessKey, SecretKey));

    [Fact]
    public async Task Policy_names_the_bucket_key_and_size_range()
    {
        var post = await Create().GetPresignedUploadPostAsync("avatars/a.png", TimeSpan.FromMinutes(10), 5 * 1024 * 1024, "image/png");

        var conditions = Conditions(post);
        conditions.Should().Contain("bucket=uploads");
        conditions.Should().Contain("key=avatars/a.png");
        conditions.Should().Contain("Content-Type=image/png");
        conditions.Should().Contain($"content-length-range:1-{5 * 1024 * 1024}");
    }

    [Fact]
    public async Task Form_fields_match_the_policy_conditions()
    {
        var post = await Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(10), 1000);

        post.Fields["key"].Should().Be("a.pdf");
        post.Fields["x-amz-algorithm"].Should().Be("AWS4-HMAC-SHA256");
        post.Fields["x-amz-credential"].Should().StartWith(AccessKey + "/").And.EndWith("/eu-central-1/s3/aws4_request");
        post.Fields.Should().ContainKey("x-amz-date").WhoseValue.Should().MatchRegex(@"^\d{8}T\d{6}Z$");
        post.Fields.Should().NotContainKey("Content-Type", "no content type was requested");
        post.Fields.Should().NotContainKey("x-amz-security-token");
    }

    [Fact]
    public async Task Signature_is_the_sigv4_chain_over_the_encoded_policy()
    {
        var post = await Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(10), 1000, "application/pdf");

        var date = post.Fields["x-amz-date"][..8];
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + SecretKey), date);
        key = Hmac(key, "eu-central-1");
        key = Hmac(key, "s3");
        key = Hmac(key, "aws4_request");
        var expected = Convert.ToHexString(Hmac(key, post.Fields["policy"])).ToLowerInvariant();

        post.Fields["x-amz-signature"].Should().Be(expected);
    }

    [Fact]
    public async Task Changing_the_size_limit_changes_the_signature()
    {
        var storage = Create();

        var small = await storage.GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(10), 1000);
        var large = await storage.GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(10), 1_000_000_000);

        // The policy is signed as a whole, so a client cannot edit the limit and keep the signature.
        large.Fields["x-amz-signature"].Should().NotBe(small.Fields["x-amz-signature"]);
        large.Fields["policy"].Should().NotBe(small.Fields["policy"]);
    }

    [Fact]
    public async Task Expiration_is_the_requested_lifetime_from_now()
    {
        var before = DateTime.UtcNow;

        var post = await Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(15), 1000);

        var expiration = DateTime.Parse(DecodePolicy(post).GetProperty("expiration").GetString()!,
            null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        expiration.Should().BeCloseTo(before.AddMinutes(15), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Virtual_host_url_on_aws_and_path_style_url_on_a_custom_endpoint()
    {
        var aws = await Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(5), 1000);
        aws.Url.Should().Be("https://uploads.s3.eu-central-1.amazonaws.com/");

        var minio = await Create(new StorageOptions { BucketName = "uploads", Endpoint = "http://localhost:9000/", Region = "us-east-1" })
            .GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(5), 1000);
        minio.Url.Should().Be("http://localhost:9000/uploads");
    }

    [Fact]
    public async Task Session_token_is_part_of_the_policy_when_present()
    {
        var credentials = new ImmutableCredentials(AccessKey, SecretKey, "SESSION-TOKEN");
        var post = await Create(credentials: new StaticCredentials(credentials))
            .GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(5), 1000);

        post.Fields["x-amz-security-token"].Should().Be("SESSION-TOKEN");
        Conditions(post).Should().Contain("x-amz-security-token=SESSION-TOKEN");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Refuses_a_policy_that_allows_no_bytes(long maxBytes)
    {
        var act = () => Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(5), maxBytes);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task Refuses_a_lifetime_outside_zero_to_seven_days(int days)
    {
        var act = () => Create().GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromDays(days), 1000);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Without_credentials_the_post_is_refused_not_unsigned()
    {
        var storage = new S3FileStorage(Substitute.For<IAmazonS3>(), Options.Create(new StorageOptions { BucketName = "uploads", Region = "eu-central-1" }));

        var act = () => storage.GetPresignedUploadPostAsync("a.pdf", TimeSpan.FromMinutes(5), 1000);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>The policy's conditions flattened to <c>name=value</c> (exact match), or <c>name:min-max</c> (size range).</summary>
    private static List<string> Conditions(PresignedPost post)
    {
        var result = new List<string>();
        foreach (var condition in DecodePolicy(post).GetProperty("conditions").EnumerateArray())
        {
            if (condition.ValueKind == JsonValueKind.Array)
                result.Add($"{condition[0].GetString()}:{condition[1].GetInt64()}-{condition[2].GetInt64()}");
            else
                foreach (var property in condition.EnumerateObject())
                    result.Add($"{property.Name}={property.Value.GetString()}");
        }
        return result;
    }

    private static JsonElement DecodePolicy(PresignedPost post)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(post.Fields["policy"]));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    /// <summary>Hands out fixed credentials, so a session token can be tested.</summary>
    private sealed class StaticCredentials(ImmutableCredentials credentials) : AWSCredentials
    {
        public override ImmutableCredentials GetCredentials() => credentials;
    }
}
