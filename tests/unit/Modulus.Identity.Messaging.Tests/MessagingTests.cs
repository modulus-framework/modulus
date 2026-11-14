using System.Net;
using System.Net.Mail;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Email;
using Modulus.Identity.Messaging;
using Xunit;

namespace Modulus.Identity.Messaging.Tests;

/// <summary>SendGrid email and Twilio SMS over a fake HTTP handler: the requests they send, and what they refuse.</summary>
[Trait("Category", "Unit")]
public sealed class MessagingTests
{
    private const string SendGridKey = "SG.test-key-123";

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.Accepted) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient Client(HttpMessageHandler handler, string baseUrl) => new(handler) { BaseAddress = new Uri(baseUrl) };

    private static MailMessage Mail(string subject = "Reset your password", string body = "Use this link: https://shop.example.com/ResetPassword?token=abc123")
    {
        var message = new MailMessage
        {
            From = new MailAddress("noreply@shop.example.com", "Shop"),
            Subject = subject,
            Body = body,
        };
        message.To.Add("ana@example.com");
        return message;
    }

    [Fact]
    public async Task SendGrid_posts_the_mail_to_v3_with_the_api_key_as_a_bearer_token()
    {
        var handler = new CapturingHandler();
        var transport = new SendGridIdentityEmailTransport(Client(handler, "https://api.sendgrid.com/"), Options.Create(new SendGridOptions { ApiKey = SendGridKey }));

        await transport.SendAsync(Mail(), CancellationToken.None);

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().Should().Be("https://api.sendgrid.com/v3/mail/send");
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be(SendGridKey);
        handler.Body.Should().Contain("\"email\":\"ana@example.com\"")
            .And.Contain("\"subject\":\"Reset your password\"")
            .And.Contain("\"type\":\"text/plain\"")
            .And.Contain("\"email\":\"noreply@shop.example.com\"");
    }

    [Fact]
    public async Task SendGrid_sends_one_personalization_per_recipient()
    {
        var handler = new CapturingHandler();
        var transport = new SendGridIdentityEmailTransport(Client(handler, "https://api.sendgrid.com/"), Options.Create(new SendGridOptions { ApiKey = SendGridKey }));
        var message = Mail();
        message.To.Add("ben@example.com");

        await transport.SendAsync(message, CancellationToken.None);

        handler.Body!.Split("\"personalizations\"")[1].Split("\"email\":\"ben@example.com\"").Length.Should().Be(2);
        handler.Body.Should().Contain("\"to\":[{\"email\":\"ana@example.com\"");
    }

    [Fact]
    public async Task SendGrid_refusal_names_the_status_but_not_the_key_or_the_link()
    {
        var handler = new CapturingHandler(HttpStatusCode.Forbidden);
        var transport = new SendGridIdentityEmailTransport(Client(handler, "https://api.sendgrid.com/"), Options.Create(new SendGridOptions { ApiKey = SendGridKey }));

        var act = () => transport.SendAsync(Mail(), CancellationToken.None);

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("403").And.NotContain(SendGridKey).And.NotContain("abc123");
    }

    [Fact]
    public async Task SendGrid_refuses_to_send_without_an_api_key_and_makes_no_request()
    {
        var handler = new CapturingHandler();
        var transport = new SendGridIdentityEmailTransport(Client(handler, "https://api.sendgrid.com/"), Options.Create(new SendGridOptions()));

        var act = () => transport.SendAsync(Mail(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ApiKey*");
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task The_reset_link_is_built_by_the_shared_sender_and_delivered_through_sendgrid()
    {
        var handler = new CapturingHandler();
        var transport = new SendGridIdentityEmailTransport(Client(handler, "https://api.sendgrid.com/"), Options.Create(new SendGridOptions { ApiKey = SendGridKey }));
        var sender = new SmtpIdentityEmailSender(
            Options.Create(new IdentityEmailOptions { BaseUrl = "https://shop.example.com", FromAddress = "noreply@shop.example.com", FromName = "Shop" }),
            transport);

        await sender.SendPasswordResetEmailAsync("ana@example.com", "tok en/1", CancellationToken.None);

        handler.Body.Should().Contain("https://shop.example.com/ResetPassword?token=tok%20en%2F1");
        handler.Body.Should().Contain("\"subject\":\"Reset your password\"");
    }

    [Fact]
    public async Task Twilio_posts_the_message_with_basic_auth_to_the_account()
    {
        var handler = new CapturingHandler();
        var sender = new TwilioIdentitySmsSender(
            Client(handler, "https://api.twilio.com/"),
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+15555550100" }));

        await sender.SendAsync("+15555550199", "Your code is 123456");

        handler.Request!.RequestUri!.ToString().Should().Be("https://api.twilio.com/2010-04-01/Accounts/AC123/Messages.json");
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.Request.Headers.Authorization.Parameter.Should().Be(Convert.ToBase64String(Encoding.ASCII.GetBytes("AC123:secret")));
        handler.Body.Should().Contain("To=%2B15555550199").And.Contain("From=%2B15555550100").And.Contain("Body=Your+code+is+123456");
    }

    [Fact]
    public async Task Twilio_uses_a_messaging_service_when_one_is_configured()
    {
        var handler = new CapturingHandler();
        var sender = new TwilioIdentitySmsSender(
            Client(handler, "https://api.twilio.com/"),
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", MessagingServiceSid = "MG999" }));

        await sender.SendAsync("+15555550199", "hi");

        handler.Body.Should().Contain("MessagingServiceSid=MG999").And.NotContain("From=");
    }

    [Theory]
    [InlineData("15555550199")]
    [InlineData("+0123")]
    [InlineData("+1 555 555 0199")]
    [InlineData("")]
    public async Task Twilio_refuses_a_number_that_is_not_e164_without_calling_the_api(string number)
    {
        var handler = new CapturingHandler();
        var sender = new TwilioIdentitySmsSender(
            Client(handler, "https://api.twilio.com/"),
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+15555550100" }));

        var act = () => sender.SendAsync(number, "hi");

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Request.Should().BeNull();
    }

    [Fact]
    public async Task Twilio_refusal_does_not_echo_the_phone_number()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized);
        var sender = new TwilioIdentitySmsSender(
            Client(handler, "https://api.twilio.com/"),
            Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "secret", FromNumber = "+15555550100" }));

        var error = await sender.Invoking(s => s.SendAsync("+15555550199", "hi")).Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("401").And.NotContain("15555550199");
    }

    [Fact]
    public void Sendgrid_options_are_validated_when_first_read()
    {
        var provider = Registered(new Dictionary<string, string?> { ["Identity:Email:BaseUrl"] = "https://shop.example.com" });

        var act = () => provider.GetRequiredService<IOptions<SendGridOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void Sendgrid_registration_takes_the_email_sender_slot_before_identity_defaults()
    {
        var provider = Registered(new Dictionary<string, string?>
        {
            ["Identity:Email:BaseUrl"] = "https://shop.example.com",
            ["Identity:Email:FromAddress"] = "noreply@shop.example.com",
            ["Identity:Email:SendGrid:ApiKey"] = SendGridKey,
        });

        provider.GetRequiredService<IIdentityEmailSender>().Should().BeOfType<SmtpIdentityEmailSender>();
        provider.GetRequiredService<IIdentityEmailTransport>().Should().BeOfType<SendGridIdentityEmailTransport>();
    }

    [Fact]
    public void Twilio_options_need_a_sender_number_or_a_messaging_service()
    {
        var provider = TwilioRegistered(new Dictionary<string, string?>
        {
            ["Identity:Sms:Twilio:AccountSid"] = "AC123",
            ["Identity:Sms:Twilio:AuthToken"] = "secret",
        });

        var act = () => provider.GetRequiredService<IOptions<TwilioOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*FromNumber*");
    }

    [Fact]
    public void Twilio_registration_resolves_the_sms_sender()
    {
        var provider = TwilioRegistered(new Dictionary<string, string?>
        {
            ["Identity:Sms:Twilio:AccountSid"] = "AC123",
            ["Identity:Sms:Twilio:AuthToken"] = "secret",
            ["Identity:Sms:Twilio:FromNumber"] = "+15555550100",
        });

        provider.GetRequiredService<IIdentitySmsSender>().Should().BeOfType<TwilioIdentitySmsSender>();
    }

    private static ServiceProvider Registered(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusSendGridEmail(configuration);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider TwilioRegistered(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusTwilioSms(configuration);
        return services.BuildServiceProvider();
    }
}
