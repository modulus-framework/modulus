namespace Modulus.Identity.Tests;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// Self-service profile (decision 8): a person reads and updates only their own name, language, time zone and phone, and changes the
/// address only through a one-time link to the new address.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProfileTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private IDisposable? _host;
    private readonly RecordingSender _sender = new();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
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
        EnterHost();
        _host?.Dispose();
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_person_reads_and_updates_their_own_profile()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync("ada@example.com");

        var update = await controller.UpdateProfileAsync(new UpdateProfileRequest
        {
            DisplayName = "  Ada L.  ",
            Language = "en-GB",
            TimeZone = "Europe/London",
            PhoneNumber = "+44 20 7946 0958",
        });
        update.Should().BeOfType<OkObjectResult>();

        var read = (ProfileResponse)((OkObjectResult)await controller.GetProfileAsync()).Value!;
        read.DisplayName.Should().Be("Ada L.", "surrounding spaces are trimmed");
        read.Language.Should().Be("en-GB");
        read.TimeZone.Should().Be("Europe/London");
        read.PhoneNumber.Should().Be("+44 20 7946 0958");
        read.Email.Should().Be("ada@example.com");
    }

    [Fact]
    public async Task Blank_values_clear_the_fields()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();
        await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "Ada", Language = "fr-FR" });

        await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "   ", Language = "" });

        var read = (ProfileResponse)((OkObjectResult)await controller.GetProfileAsync()).Value!;
        read.DisplayName.Should().BeNull();
        read.Language.Should().BeNull();
    }

    [Theory]
    [InlineData("not a language!", null, null, "language")]
    [InlineData(null, "Mars/Olympus_Mons", null, "timeZone")]
    [InlineData(null, null, "call me maybe", "phoneNumber")]
    public async Task Invalid_values_are_refused_with_a_message_on_the_field(string? language, string? zone, string? phone, string field)
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();

        var result = await controller.UpdateProfileAsync(new UpdateProfileRequest { Language = language, TimeZone = zone, PhoneNumber = phone });

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(bad.Value).Should().Contain($"\"{field}\"");
    }

    [Fact]
    public async Task A_changed_phone_number_is_unconfirmed_until_proven_again()
    {
        EnterHost();
        var (controller, users) = await SignedInAsync();
        var user = await users.FindByEmailAsync("ada@example.com");
        user!.PhoneNumber = "+447700900000";
        user.PhoneNumberConfirmed = true;
        await users.UpdateAsync(user);

        await controller.UpdateProfileAsync(new UpdateProfileRequest { PhoneNumber = "+447700900111" });

        (await users.FindByIdAsync(user.Id.ToString()))!.PhoneNumberConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task An_anonymous_caller_gets_no_profile()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        (await controller.GetProfileAsync()).Should().BeOfType<UnauthorizedResult>();
        (await controller.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = "x" })).Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task An_email_change_sends_a_link_to_the_new_address_and_completes_only_with_its_token()
    {
        EnterHost();
        var (controller, users) = await SignedInAsync("ada@example.com");

        var requested = await controller.RequestEmailChangeAsync(new ChangeEmailRequest { NewEmail = "ada@new.example" }, _sender, default);

        requested.Should().BeOfType<AcceptedResult>();
        _sender.Recipients.Should().BeEquivalentTo(["ada@new.example"], "the link goes to the new address, not the current one");

        var wrong = await controller.ConfirmEmailChangeAsync(new ConfirmEmailChangeRequest { NewEmail = "ada@new.example", Token = "not-the-token" });
        wrong.Should().BeOfType<BadRequestObjectResult>();
        (await users.FindByEmailAsync("ada@example.com")).Should().NotBeNull("a bad token changes nothing");

        var confirmed = await controller.ConfirmEmailChangeAsync(new ConfirmEmailChangeRequest
        {
            NewEmail = "ada@new.example",
            Token = _sender.Tokens["ada@new.example"],
        });

        confirmed.Should().BeOfType<OkObjectResult>();
        (await users.FindByEmailAsync("ada@new.example")).Should().NotBeNull();
        (await users.FindByEmailAsync("ada@example.com")).Should().BeNull();
    }

    [Fact]
    public async Task An_email_change_to_a_taken_address_looks_the_same_and_sends_nothing()
    {
        EnterHost();
        var (controller, users) = await SignedInAsync("ada@example.com");
        (await users.CreateAsync(new ModulusUser { UserName = "grace", Email = "grace@example.com", IsActive = true }, "Passw0rd!x"))
            .Succeeded.Should().BeTrue();

        var result = await controller.RequestEmailChangeAsync(new ChangeEmailRequest { NewEmail = "grace@example.com" }, _sender, default);

        result.Should().BeOfType<AcceptedResult>("the answer must not show which addresses have accounts");
        _sender.Recipients.Should().BeEmpty();
    }

    [Fact]
    public async Task A_malformed_new_address_is_refused()
    {
        EnterHost();
        var (controller, _) = await SignedInAsync();

        var result = await controller.RequestEmailChangeAsync(new ChangeEmailRequest { NewEmail = "no-at-sign" }, _sender, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        _sender.Recipients.Should().BeEmpty();
    }

    /// <summary>Enters the host context in the test method itself: an AsyncLocal set inside an async helper does not flow back to the test.</summary>
    private void EnterHost() => _host = _provider.GetRequiredService<ICurrentTenant>().Change(null);

    /// <summary>Creates a signed-in account and a controller acting as it, in the host context.</summary>
    private async Task<(AccountController<ModulusUser> Controller, UserManager<ModulusUser> Users)> SignedInAsync(string email = "ada@example.com")
    {
        var users = _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = email.Split('@')[0], Email = email, IsActive = true };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
        var controller = new AccountController<ModulusUser>(users, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, users);
    }

    private sealed class RecordingSender : IIdentityEmailSender
    {
        public List<string> Recipients { get; } = [];

        public Dictionary<string, string> Tokens { get; } = [];

        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendEmailChangeEmailAsync(string newEmail, string changeToken, CancellationToken ct = default)
        {
            Recipients.Add(newEmail);
            Tokens[newEmail] = changeToken;
            return Task.CompletedTask;
        }
    }
}
