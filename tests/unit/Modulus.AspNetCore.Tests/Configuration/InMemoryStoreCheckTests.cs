namespace Modulus.AspNetCore.Tests.Configuration;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Configuration;
using Modulus.AspNetCore.Idempotency;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryStoreCheckTests
{
    private static (ServiceCollection Services, IServiceProvider Provider) Host(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusIdempotency(new ConfigurationBuilder().Build());
        configure?.Invoke(services);
        return (services, services.BuildServiceProvider());
    }

    private static InMemoryStoreCheck Check(
        ServiceCollection services, IServiceProvider provider, InMemoryStoreCheckOptions options, string environment)
        => new(
            new InMemoryStoreCheckRegistration(services),
            provider,
            Options.Create(options),
            NullLogger<InMemoryStoreCheck>.Instance,
            new StubEnvironment(environment));

    [Fact]
    public void Finds_the_framework_default_store()
    {
        var (services, provider) = Host();

        InMemoryStoreCheck.Find(services, provider)
            .Should().ContainSingle().Which.Should().Be(new InMemoryStore("IIdempotencyStore", "InMemoryIdempotencyStore"));
    }

    [Fact]
    public void A_durable_store_registered_later_wins()
    {
        var (services, provider) = Host(s => s.AddSingleton<IIdempotencyStore>(_ => new DurableStore()));

        InMemoryStoreCheck.Find(services, provider).Should().BeEmpty();
    }

    [Fact]
    public async Task Fail_mode_refuses_to_start_in_production()
    {
        var (services, provider) = Host();
        var check = Check(services, provider, new InMemoryStoreCheckOptions { Mode = InMemoryStoreCheckMode.Fail }, Environments.Production);

        var act = () => check.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*IIdempotencyStore (InMemoryIdempotencyStore)*");
    }

    [Fact]
    public async Task Fail_mode_starts_when_the_store_is_allowed()
    {
        var (services, provider) = Host();
        var options = new InMemoryStoreCheckOptions { Mode = InMemoryStoreCheckMode.Fail, Allow = ["IIdempotencyStore"] };

        await Check(services, provider, options, Environments.Production).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Development_and_warn_mode_never_refuse()
    {
        var (services, provider) = Host();

        await Check(services, provider, new InMemoryStoreCheckOptions { Mode = InMemoryStoreCheckMode.Fail }, Environments.Development)
            .StartAsync(CancellationToken.None);
        await Check(services, provider, new InMemoryStoreCheckOptions(), Environments.Production).StartAsync(CancellationToken.None);
    }

    [Fact]
    public void Registration_binds_the_section_and_is_idempotent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:InMemoryStores:Mode"] = "Fail",
                ["Security:InMemoryStores:Allow:0"] = "ISettingStore",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddModulusInMemoryStoreCheck(configuration).AddModulusInMemoryStoreCheck(configuration);

        services.Count(d => d.ImplementationType == typeof(InMemoryStoreCheck)).Should().Be(1);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<InMemoryStoreCheckOptions>>().Value;
        options.Mode.Should().Be(InMemoryStoreCheckMode.Fail);
        options.Allow.Should().Equal("ISettingStore");
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class DurableStore : IIdempotencyStore
    {
        public Task<IdempotencyResult> TryBeginAsync(string key, string fingerprint, CancellationToken ct)
            => throw new NotSupportedException();

        public Task CompleteAsync(string key, CachedResponse response, CancellationToken ct)
            => throw new NotSupportedException();

        public Task AbandonAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
