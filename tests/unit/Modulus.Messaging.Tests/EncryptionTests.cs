using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Internal;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Tests;

[EncryptedMessage]
public sealed record SecretNote(Guid Id, [property: Modulus.Messaging.Sensitive] string Text);

/// <summary>Not marked: sealed only when its consumer asks for encryption.</summary>
public sealed record PlainNote(Guid Id, string Text);

public sealed class SecretLog
{
    public ConcurrentQueue<SecretNote> Notes { get; } = new();

    public ConcurrentQueue<PlainNote> Plain { get; } = new();
}

public sealed class SecretNoteConsumer(SecretLog log) : IConsumer<SecretNote>
{
    public Task Consume(ConsumeContext<SecretNote> context)
    {
        log.Notes.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

public sealed class FailingSecretNoteConsumer : IConsumer<SecretNote>
{
    public Task Consume(ConsumeContext<SecretNote> context) => throw new InvalidOperationException("refused on purpose");
}

public sealed class PlainNoteConsumer(SecretLog log) : IConsumer<PlainNote>
{
    public Task Consume(ConsumeContext<PlainNote> context)
    {
        log.Plain.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}

public sealed class FaultedSecretRecorder(SecretLog log) : IConsumer<Fault<SecretNote>>
{
    public Task Consume(ConsumeContext<Fault<SecretNote>> context)
    {
        log.Notes.Enqueue(context.Message.Message);
        return Task.CompletedTask;
    }
}

/// <summary>Keys that can be changed while the test runs.</summary>
public sealed class TestKeys : IMessageEncryptionKeys
{
    public string CurrentKeyId { get; set; } = "k1";

    public Dictionary<string, byte[]> Known { get; } = new(StringComparer.Ordinal);

    public byte[]? GetKey(string keyId) => Known.TryGetValue(keyId, out var key) ? (byte[])key.Clone() : null;

    public static TestKeys WithKeys(params string[] ids)
    {
        var keys = new TestKeys();
        foreach (var id in ids)
        {
            keys.Known[id] = RandomNumberGenerator.GetBytes(32);
        }

        return keys;
    }
}

/// <summary>Hands out its key on the first lookup only: the seal. The next lookup, the open, finds nothing.</summary>
public sealed class FirstLookupOnlyKeys : IMessageEncryptionKeys
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private int _lookups;

    public string CurrentKeyId => "only";

    public byte[]? GetKey(string keyId) => Interlocked.Increment(ref _lookups) == 1 ? (byte[])_key.Clone() : null;
}

[Trait("Category", "Unit")]
public sealed class EncryptionTests
{
    [Fact]
    public async Task D6_a_sealed_body_is_not_readable_in_the_transport_or_a_dead_letter()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<FailingSecretNoteConsumer, SecretNote>(endpoint).UseInMemory(),
            services => services.AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(Guid.NewGuid(), "the launch code is 4411"));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        var dead = harness.Transport.Errors(endpoint).Single();
        dead.Envelope.Message.Should().BeOfType<EncryptedPayload>("the transport holds only the sealed body");
        JsonSerializer.Serialize(dead.Envelope.Message).Should().NotContain("launch code");
        dead.Envelope.Headers.Should().ContainKey(MessagingHeaders.EncryptedType);
    }

    [Fact]
    public async Task D6_the_consumer_gets_the_opened_message()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<SecretNoteConsumer, SecretNote>(endpoint).UseInMemory(),
            services => services
                .AddSingleton(new SecretLog())
                .AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(id, "the launch code is 4411"));

        var log = harness.Services.GetRequiredService<SecretLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Notes.Any(n => n.Id == id))).Should().BeTrue();
        log.Notes.Single(n => n.Id == id).Text.Should().Be("the launch code is 4411");
    }

    [Fact]
    public async Task D6_a_message_whose_key_is_not_available_is_parked_as_decrypt_failed_and_not_run()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<SecretNoteConsumer, SecretNote>(endpoint).UseInMemory(),
            services => services
                .AddSingleton(new SecretLog())
                .AddSingleton<IMessageEncryptionKeys>(new FirstLookupOnlyKeys()));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(Guid.NewGuid(), "unreadable here"));

        (await MessagingTestHarness.WaitUntilAsync(() => harness.Transport.Errors(endpoint).Count == 1)).Should().BeTrue();
        harness.Transport.Errors(endpoint).Single().Exception.Should().BeOfType<MessageParkException>()
            .Which.Reason.Should().Be("decrypt-failed");
        harness.Services.GetRequiredService<SecretLog>().Notes.Should().BeEmpty("the consumer never sees an unopened body");
    }

    [Fact]
    public async Task D6_a_message_sealed_under_an_older_key_still_opens_after_rotation()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        var keys = TestKeys.WithKeys("k1", "k2");
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<SecretNoteConsumer, SecretNote>(endpoint).UseInMemory(),
            services => services
                .AddSingleton(new SecretLog())
                .AddSingleton<IMessageEncryptionKeys>(keys));
        var log = harness.Services.GetRequiredService<SecretLog>();

        var before = Guid.NewGuid();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(before, "sealed under k1"));
        (await MessagingTestHarness.WaitUntilAsync(() => log.Notes.Any(n => n.Id == before))).Should().BeTrue();

        keys.CurrentKeyId = "k2";
        var after = Guid.NewGuid();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(after, "sealed under k2"));

        (await MessagingTestHarness.WaitUntilAsync(() => log.Notes.Any(n => n.Id == after))).Should().BeTrue();
        log.Notes.Single(n => n.Id == before).Text.Should().Be("sealed under k1");
    }

    [Fact]
    public async Task D6_a_fault_of_a_sealed_message_is_sealed_and_reaches_its_fault_consumer_opened()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b
                .AddConsumer<FailingSecretNoteConsumer, SecretNote>(endpoint)
                .AddConsumer<FaultedSecretRecorder, Fault<SecretNote>>($"faults-{endpoint}")
                .UseInMemory(),
            services => services
                .AddSingleton(new SecretLog())
                .AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(id, "fault me"));

        var log = harness.Services.GetRequiredService<SecretLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Notes.Any(n => n.Id == id))).Should().BeTrue();
        log.Notes.Single(n => n.Id == id).Text.Should().Be("fault me");
    }

    [Fact]
    public async Task D6_an_endpoint_that_asks_for_encryption_seals_an_unmarked_message()
    {
        var endpoint = $"plain-sealed-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<PlainNoteConsumer, PlainNote>(endpoint, o => o.EncryptPayload = true).UseInMemory(),
            services => services
                .AddSingleton(new SecretLog())
                .AddSingleton<IMessageEncryptionKeys>(TestKeys.WithKeys("k1")));

        var id = Guid.NewGuid();
        await harness.Bus.Send(new Uri($"queue:{endpoint}"), new PlainNote(id, "sealed by the endpoint"));

        var log = harness.Services.GetRequiredService<SecretLog>();
        (await MessagingTestHarness.WaitUntilAsync(() => log.Plain.Any(n => n.Id == id))).Should().BeTrue();
        log.Plain.Single(n => n.Id == id).Text.Should().Be("sealed by the endpoint");
    }

    [Fact]
    public async Task D6_sending_a_sealed_message_without_keys_is_refused()
    {
        var endpoint = $"secret-{Guid.NewGuid():N}";
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<SecretNoteConsumer, SecretNote>(endpoint).UseInMemory(),
            services => services.AddSingleton(new SecretLog()));

        var send = async () => await harness.Bus.Send(new Uri($"queue:{endpoint}"), new SecretNote(Guid.NewGuid(), "no keys"));

        await send.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IMessageEncryptionKeys*");
    }

    [Fact]
    public void D6_the_cipher_refuses_a_body_moved_to_another_message()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var first = new MessageEnvelope(Guid.NewGuid(), "orders", new object(), new Dictionary<string, string>());
        var second = first with { MessageId = Guid.NewGuid() };
        var payload = MessageCipher.Encrypt("k", key, Encoding.UTF8.GetBytes("{}"), MessageCipher.AssociatedData(first, "T"));

        var open = () => MessageCipher.Decrypt(key, payload, MessageCipher.AssociatedData(second, "T"));

        open.Should().Throw<CryptographicException>("the associated data names the message id");
    }

    [Fact]
    public void D6_a_static_key_must_be_32_bytes()
    {
        var short16 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var create = () => new StaticMessageEncryptionKeys("k", new Dictionary<string, string> { ["k"] = short16 });

        create.Should().Throw<ArgumentException>().WithMessage("*32 bytes*");
    }
}
