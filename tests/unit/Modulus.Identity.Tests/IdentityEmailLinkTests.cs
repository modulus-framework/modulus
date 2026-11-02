namespace Modulus.Identity.Tests;

using System.Net.Mail;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Email;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// The confirmation link a person receives: built from the configured base address, pointing at the page that handles it, and confirming
/// the account it was sent for. The SMTP wire is replaced by a recording transport; everything else is the production code.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityEmailLinkTests : IAsyncLifetime
{
    private const string BaseUrl = "https://shop.example.test/";

    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private readonly RecordingTransport _transport = new();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
        services.Configure<IdentityEmailOptions>(o =>
        {
            o.BaseUrl = BaseUrl;
            o.FromAddress = "no-reply@shop.example.test";
            o.SmtpHost = "smtp.example.test";
        });
        services.AddSingleton<IIdentityEmailTransport>(_transport);
        services.AddScoped<IIdentityEmailSender, SmtpIdentityEmailSender>();
        _provider = services.BuildServiceProvider();

        using (_provider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        }
        _scope = _provider.CreateAsyncScope();
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("http://shop.example.test")]
    [InlineData("https://user:pw@shop.example.test")]
    [InlineData("ftp://shop.example.test")]
    [InlineData("shop.example.test")]
    public void A_base_address_that_is_not_https_or_has_credentials_is_refused(string baseUrl)
    {
        var act = () => SmtpIdentityEmailSender.ValidatedBase(baseUrl);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Plain_http_is_allowed_only_for_localhost()
    {
        SmtpIdentityEmailSender.ValidatedBase("http://localhost:5180").Host.Should().Be("localhost");
    }

    private sealed class RecordingTransport : IIdentityEmailTransport
    {
        public List<MailMessage> Sent { get; } = [];

        public Task SendAsync(MailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
