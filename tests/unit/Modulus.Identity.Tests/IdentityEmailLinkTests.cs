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
        services.AddScoped(typeof(WorkEmailVerifier<>));
        services.Configure<WorkEmailVerificationOptions>(_ => { });
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

    [Fact]
    public async Task The_delivered_work_email_link_leads_to_the_confirmation_page_and_confirms_the_account()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var users = _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var ada = new ModulusUser { UserName = "ada", Email = "ada@example.com", IsActive = true };
        (await users.CreateAsync(ada, "Passw0rd!x")).Succeeded.Should().BeTrue();
        var verifier = _scope.ServiceProvider.GetRequiredService<WorkEmailVerifier<ModulusUser>>();
        var sender = _scope.ServiceProvider.GetRequiredService<IIdentityEmailSender>();

        var request = await verifier.RequestAsync(ada, "ada@work.example");
        await sender.SendWorkEmailVerificationAsync("ada@work.example", request.Token!);

        var mail = _transport.Sent.Should().ContainSingle().Subject;
        mail.To.Single().Address.Should().Be("ada@work.example");
        mail.From!.Address.Should().Be("no-reply@shop.example.test");
        // The link is the one whitespace-delimited address in the body that starts with the base address (a line break ends it).
        var link = new Uri(System.Text.RegularExpressions.Regex.Matches(mail.Body, @"https://\S+").Select(m => m.Value).Single(v => v.StartsWith(BaseUrl + "ConfirmWorkEmail?token=", StringComparison.Ordinal)));
        link.Scheme.Should().Be("https");
        link.AbsolutePath.Should().Be("/ConfirmWorkEmail");
        var token = System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!;

        (await verifier.ConfirmAsync(ada.Id, token)).Should().Be(WorkEmailConfirmOutcome.Verified, "the token from the delivered link confirms the account");
    }

    [Fact]
    public async Task The_email_change_link_carries_the_new_address_and_the_token()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var sender = _scope.ServiceProvider.GetRequiredService<IIdentityEmailSender>();

        await sender.SendEmailChangeEmailAsync("new@example.com", "tok en&x=1");

        var body = _transport.Sent.Single().Body;
        body.Should().Contain($"{BaseUrl}ConfirmEmailChange?NewEmail=new%40example.com&Token=tok%20en%26x%3D1");
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
