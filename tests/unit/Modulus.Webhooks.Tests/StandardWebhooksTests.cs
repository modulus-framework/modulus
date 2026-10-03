namespace Modulus.Webhooks.Tests;

using System.Net;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class StandardWebhooksTests
{
    // The Standard Webhooks specification's own test vector.
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string MessageId = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const string Body = """{"test": 2432232314}""";
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(1614265330);

    [Fact]
    public void Signs_like_the_specification()
        => StandardWebhooks.Sign(Secret, MessageId, Timestamp, Body)
            .Should().Be("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");

    [Fact]
    public void Verifies_a_signed_message()
        => StandardWebhooks.Verify([Secret], MessageId, "1614265330", StandardWebhooks.Sign(Secret, MessageId, Timestamp, Body), Body, Timestamp)
            .Should().Be(WebhookVerificationResult.Valid);

    [Fact]
    public void Accepts_any_listed_signature_during_a_rotation()
    {
        var other = StandardWebhooks.GenerateSecret();
        var header = StandardWebhooks.Sign(other, MessageId, Timestamp, Body) + " " + StandardWebhooks.Sign(Secret, MessageId, Timestamp, Body);

        StandardWebhooks.Verify([Secret], MessageId, "1614265330", header, Body, Timestamp).Should().Be(WebhookVerificationResult.Valid);
        StandardWebhooks.Verify([other], MessageId, "1614265330", header, Body, Timestamp).Should().Be(WebhookVerificationResult.Valid);
    }

    [Fact]
    public void Rejects_a_changed_body()
        => StandardWebhooks.Verify([Secret], MessageId, "1614265330", StandardWebhooks.Sign(Secret, MessageId, Timestamp, Body), Body + " ", Timestamp)
            .Should().Be(WebhookVerificationResult.InvalidSignature);

    [Fact]
    public void Rejects_an_old_timestamp()
        => StandardWebhooks.Verify([Secret], MessageId, "1614265330", StandardWebhooks.Sign(Secret, MessageId, Timestamp, Body), Body, Timestamp.AddMinutes(6))
            .Should().Be(WebhookVerificationResult.InvalidTimestamp);

    [Theory]
    [InlineData(null, "1614265330", "v1,x")]
    [InlineData(MessageId, null, "v1,x")]
    [InlineData(MessageId, "1614265330", null)]
    public void Rejects_missing_headers(string? id, string? timestamp, string? signature)
        => StandardWebhooks.Verify([Secret], id, timestamp, signature, Body, Timestamp).Should().Be(WebhookVerificationResult.MissingHeaders);

    [Theory]
    [InlineData("v2,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=")]
    [InlineData("v1,not base64!")]
    [InlineData("garbage")]
    public void Ignores_unknown_or_malformed_signatures(string header)
        => StandardWebhooks.Verify([Secret], MessageId, "1614265330", header, Body, Timestamp).Should().Be(WebhookVerificationResult.InvalidSignature);

    [Fact]
    public void Generated_secrets_are_valid_and_distinct()
    {
        var a = StandardWebhooks.GenerateSecret();
        var b = StandardWebhooks.GenerateSecret();

        a.Should().StartWith("whsec_");
        StandardWebhooks.IsValidSecret(a).Should().BeTrue();
        a.Should().NotBe(b);
    }

    [Theory]
    [InlineData("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw", true)]
    [InlineData("whsec_c2hvcnQ=", false)]
    [InlineData("whsec_***", false)]
    [InlineData("", false)]
    public void Validates_secrets(string secret, bool valid) => StandardWebhooks.IsValidSecret(secret).Should().Be(valid);
}

[Trait("Category", "Unit")]
public sealed class WebhookAddressGuardTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2001:db8::1", false)]
    public void Tells_public_addresses_from_internal_ones(string address, bool isPublic)
        => WebhookAddressGuard.IsPublic(IPAddress.Parse(address)).Should().Be(isPublic);

    [Theory]
    [InlineData("https://hooks.example.com/in", null)]
    [InlineData("http://hooks.example.com/in", "Must be an https URL.")]
    [InlineData("https://user:pass@hooks.example.com/", "Must not contain credentials.")]
    [InlineData("https://localhost/hook", "Must not point at this machine.")]
    [InlineData("https://api.localhost/hook", "Must not point at this machine.")]
    [InlineData("https://127.0.0.1/hook", "Must not point at this machine.")]
    [InlineData("https://10.0.0.5/hook", "Must not point at a private or reserved address.")]
    [InlineData("https://[fd00::1]/hook", "Must not point at a private or reserved address.")]
    [InlineData("https://hooks.example.com/in#x", "Must not contain a fragment.")]
    [InlineData("hooks.example.com/in", "Must be an absolute URL.")]
    public void Checks_subscription_urls(string url, string? error)
        => WebhookUrlRules.Validate(url, new ModulusWebhooksOptions()).Should().Be(error);

    [Fact]
    public void Development_settings_allow_local_http_endpoints()
        => WebhookUrlRules.Validate("http://localhost:5000/hook", new ModulusWebhooksOptions { AllowHttp = true, AllowPrivateNetworks = true })
            .Should().BeNull();

    [Fact]
    public async Task A_name_resolving_to_loopback_is_refused_at_connect_time()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddModulusWebhooks(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var client = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IHttpClientFactory>(provider).CreateClient(WebhookDeliveryProcessor.HttpClientName);

        var call = () => client.PostAsync(new Uri("http://localhost:9/hook"), new StringContent("{}"));

        // SocketsHttpHandler wraps connect failures; the guard's exception is the inner one.
        (await call.Should().ThrowAsync<HttpRequestException>()).WithInnerException<WebhookAddressRejectedException>();
    }
}

[Trait("Category", "Unit")]
public sealed class WebhookEventFilterTests
{
    [Theory]
    [InlineData("*", "catalog.product-created.v1", true)]
    [InlineData("catalog.*", "catalog.product-created.v1", true)]
    [InlineData("catalog.*", "orders.order-placed.v1", false)]
    [InlineData("Catalog.Product-Created.V1", "catalog.product-created.v1", true)]
    [InlineData("catalog.product-created.v2", "catalog.product-created.v1", false)]
    public void Matches_names_prefixes_and_everything(string pattern, string eventType, bool matches)
        => WebhookEventFilter.Matches([pattern], eventType).Should().Be(matches);

    [Theory]
    [InlineData("*", true)]
    [InlineData("catalog.*", true)]
    [InlineData("catalog.product-created.v1", true)]
    [InlineData("billing.*", false)]
    [InlineData("catalog.product-deleted.v1", false)]
    [InlineData(" ", false)]
    public void Accepts_only_patterns_that_can_match(string pattern, bool valid)
        => WebhookEventFilter.IsValid(pattern, ["catalog.product-created.v1"]).Should().Be(valid);
}
