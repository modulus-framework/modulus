using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

public sealed record RequestPing(Guid Id);

public sealed record RequestPong(Guid Id);

[EncryptedMessage]
public sealed record SecretPong(Guid Id, string Text);

public sealed class PingResponder : IConsumer<RequestPing>
{
    public Task Consume(ConsumeContext<RequestPing> context) => context.RespondAsync(new RequestPong(context.Message.Id));
}

public sealed class SecretResponder : IConsumer<RequestPing>
{
    public Task Consume(ConsumeContext<RequestPing> context) => context.RespondAsync(new SecretPong(context.Message.Id, "answer"));
}

/// <summary>Takes the request and never answers: the requester must time out.</summary>
public sealed class SilentResponder : IConsumer<RequestPing>
{
    public Task Consume(ConsumeContext<RequestPing> context) => Task.CompletedTask;
}

/// <summary>Tries to answer a message that was not sent as a request.</summary>
public sealed class NotARequestResponder : IConsumer<RequestPing>
{
    public Task Consume(ConsumeContext<RequestPing> context) => context.RespondAsync(new RequestPong(context.Message.Id));
}

/// <summary>Refuses every reply, so the reply is parked and its stored body can be inspected.</summary>
public sealed class RefusingReplyReceiver : IConsumer<RequestReply<SecretPong>>
{
    public Task Consume(ConsumeContext<RequestReply<SecretPong>> context) => throw new InvalidOperationException("refused on purpose");
}

[Trait("Category", "Unit")]
public sealed class RequestResponseTests
{
    [Fact]
    public async Task D6_a_reply_to_an_encrypted_response_is_stored_sealed()
    {
        var endpoint = $"reply-sealed-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumer<RefusingReplyReceiver, RequestReply<SecretPong>>(endpoint)
                .UseInMemory(),
            services => services.AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new RequestReply<SecretPong>(Guid.NewGuid(), new SecretPong(Guid.NewGuid(), "the answer")));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        harness.Transport.Errors(endpoint).Single().Envelope.Message.Should().BeOfType<EncryptedPayload>("the reply's response is sealed");
    }

    [Fact]
    public async Task FR_06_a_request_gets_the_response_from_the_consumer()
    {
        var endpoint = $"pong-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<PingResponder, RequestPing>(endpoint)
            .AddRequestClient<RequestPong>()
            .UseInMemory());
        var id = Guid.NewGuid();

        var pong = await harness.Services.GetRequiredService<IRequestClient>()
            .GetResponse<RequestPing, RequestPong>(new Uri($"queue:{endpoint}"), new RequestPing(id), TimeSpan.FromSeconds(5));

        pong.Id.Should().Be(id);
    }

    [Fact]
    public async Task FR_06_no_response_within_the_timeout_raises_a_request_timeout()
    {
        var endpoint = $"silent-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<SilentResponder, RequestPing>(endpoint)
            .AddRequestClient<RequestPong>()
            .UseInMemory());

        var request = async () => await harness.Services.GetRequiredService<IRequestClient>()
            .GetResponse<RequestPing, RequestPong>(new Uri($"queue:{endpoint}"), new RequestPing(Guid.NewGuid()), TimeSpan.FromMilliseconds(300));

        await request.Should().ThrowAsync<RequestTimeoutException>();
    }

    [Fact]
    public async Task FR_06_a_response_type_with_no_reply_receiver_is_refused()
    {
        var endpoint = $"unreceived-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<PingResponder, RequestPing>(endpoint)
            .UseInMemory());

        var request = async () => await harness.Services.GetRequiredService<IRequestClient>()
            .GetResponse<RequestPing, RequestPong>(new Uri($"queue:{endpoint}"), new RequestPing(Guid.NewGuid()), TimeSpan.FromSeconds(1));

        await request.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddRequestClient*");
    }

    [Fact]
    public async Task FR_06_an_encrypted_response_is_sealed_and_opens_for_the_requester()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumer<SecretResponder, RequestPing>(endpoint)
                .AddRequestClient<SecretPong>()
                .UseInMemory(),
            services => services.AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        var answer = await harness.Services.GetRequiredService<IRequestClient>()
            .GetResponse<RequestPing, SecretPong>(new Uri($"queue:{endpoint}"), new RequestPing(Guid.NewGuid()), TimeSpan.FromSeconds(5));

        answer.Text.Should().Be("answer");
    }

    [Fact]
    public async Task FR_06_responding_to_a_message_that_is_not_a_request_is_refused_and_parked()
    {
        var endpoint = $"plain-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(b => b
            .AddConsumer<NotARequestResponder, RequestPing>(endpoint)
            .UseInMemory());

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new RequestPing(Guid.NewGuid()));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        harness.Transport.Errors(endpoint).Single().Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("not a request");
    }
}
