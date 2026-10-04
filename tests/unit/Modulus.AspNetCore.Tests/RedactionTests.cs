namespace Modulus.AspNetCore.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.AspNetCore.Logging;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.DataProtection;
using Xunit;

/// <summary>
/// Log redaction for the Modulus taxonomy: personal values pseudonymized (or erased without a key), confidential and
/// restricted values and secrets erased, internal values kept; secrets erased even with redaction switched off.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class RedactionTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static ServiceProvider Build(Dictionary<string, string?> settings, CaptureProvider? capture = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (capture is not null)
                logging.AddProvider(capture);
        });
        services.AddModulusRedaction(configuration);
        return services.BuildServiceProvider();
    }

    private static string Redact(IServiceProvider services, Microsoft.Extensions.Compliance.Classification.DataClassification classification, string value)
        => services.GetRequiredService<IRedactorProvider>().GetRedactor(classification).Redact(value);

    [Fact]
    public void Without_a_key_personal_data_is_erased_and_internal_data_is_kept()
    {
        using var services = Build([]);

        Redact(services, ModulusTaxonomy.Personal, "alice@example.com").Should().BeEmpty();
        Redact(services, ModulusTaxonomy.Confidential, "4200.00").Should().BeEmpty();
        Redact(services, ModulusTaxonomy.Restricted, "DE89 3704").Should().BeEmpty();
        Redact(services, ModulusTaxonomy.Secret, "hunter2").Should().BeEmpty();
        Redact(services, ModulusTaxonomy.Internal, "ORD-7").Should().Be("ORD-7");
        Redact(services, new("Other", "Thing"), "x").Should().BeEmpty("an unknown classification falls back to erasure");
    }

    [Fact]
    public void With_a_key_personal_data_is_pseudonymized_deterministically()
    {
        using var services = Build(new() { ["Security:Redaction:HmacKey"] = Key, ["Security:Redaction:HmacKeyId"] = "7" });

        var first = Redact(services, ModulusTaxonomy.Personal, "alice@example.com");

        first.Should().NotBeEmpty().And.NotContain("alice").And.StartWith("7:");
        Redact(services, ModulusTaxonomy.Personal, "alice@example.com").Should().Be(first);
        Redact(services, ModulusTaxonomy.Personal, "bob@example.com").Should().NotBe(first);
    }

    [Fact]
    public void Switched_off_only_secrets_are_still_erased()
    {
        using var services = Build(new() { ["Security:Redaction:Enabled"] = "false" });

        Redact(services, ModulusTaxonomy.Personal, "alice@example.com").Should().Be("alice@example.com");
        Redact(services, ModulusTaxonomy.Restricted, "DE89 3704").Should().Be("DE89 3704");
        Redact(services, ModulusTaxonomy.Secret, "hunter2").Should().BeEmpty();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")]
    public void A_weak_or_malformed_key_fails_at_registration(string key)
    {
        var act = () => Build(new() { ["Security:Redaction:HmacKey"] = key });

        act.Should().Throw<InvalidOperationException>().WithMessage("*HmacKey*");
    }

    [Fact]
    public void Generated_log_calls_never_write_classified_values()
    {
        var capture = new CaptureProvider();
        using var services = Build(new() { ["Security:Redaction:HmacKey"] = Key }, capture);
        var logger = services.GetRequiredService<ILogger<RedactionTests>>();

        SignedIn(logger, "ORD-7", "alice@example.com", "hunter2");
        Created(logger, new Customer { Code = "C-1", Email = "bob@example.com" });

        var written = string.Join('\n', capture.Lines);
        written.Should().Contain("ORD-7").And.Contain("C-1")
            .And.NotContain("alice@example.com").And.NotContain("hunter2").And.NotContain("bob@example.com");
    }

    [Fact]
    public void Protected_personal_data_is_a_personal_classification()
        => new ProtectedPersonalDataAttribute().Classification.Should().Be(ModulusTaxonomy.Personal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {Order} placed by {Email} ({Password})")]
    private static partial void SignedIn(
        ILogger logger, [InternalData] string order, [PersonalInformation] string email, [SecretData] string password);

    [LoggerMessage(Level = LogLevel.Information, Message = "Customer created")]
    private static partial void Created(ILogger logger, [LogProperties] Customer customer);

    internal sealed class Customer
    {
        public string Code { get; set; } = "";

        [ProtectedPersonalData]
        public string Email { get; set; } = "";
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(Lines);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue(formatter(state, exception));
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                        lines.Enqueue($"{pair.Key}={pair.Value}");
                }
            }
        }
    }
}
