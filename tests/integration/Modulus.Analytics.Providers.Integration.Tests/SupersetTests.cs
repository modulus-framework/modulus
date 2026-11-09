namespace Modulus.Analytics.Providers.Integration.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Superset;
using Xunit;

/// <summary>The Superset guest-token service against a real Superset: it signs in as the service account and gets a token that carries the company's row-level rule.</summary>
[Trait("Category", "Integration")]
public sealed class SupersetTests : IAsyncLifetime
{
    private const int Port = 8088;

    private readonly IContainer _superset = new ContainerBuilder("docker.io/apache/superset:4.1.2")
        .WithEnvironment("SUPERSET_CONFIG_PATH", "/app/pythonpath/superset_config.py")
        .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "Assets", "superset_config.py"), "/app/pythonpath/")
        .WithPortBinding(Port, true)
        .WithEntrypoint("bash", "-c")
        .WithCommand("superset db upgrade && superset fab create-admin --username admin --firstname a --lastname b --email a@b.c --password admin && superset init && exec superset run -h 0.0.0.0 -p 8088")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/health")))
        .Build();

    public Task InitializeAsync() => _superset.StartAsync();

    public Task DisposeAsync() => _superset.DisposeAsync().AsTask();

    [Fact]
    public async Task A_guest_token_is_issued_for_a_company_and_carries_its_row_level_rule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusSupersetGuestTokens(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Superset:BaseUrl"] = $"http://{_superset.Hostname}:{_superset.GetMappedPublicPort(Port)}/",
            ["Analytics:Superset:Username"] = "admin",
            ["Analytics:Superset:Password"] = "admin",
        }).Build());
        await using var provider = services.BuildServiceProvider();
        var company = Guid.NewGuid();
        var embedded = await CreateEmbeddedDashboardAsync();

        var token = await provider.GetRequiredService<ISupersetGuestTokens>().CreateAsync(company, embedded, CancellationToken.None);

        token.Username.Should().Be($"tenant-{company:N}");
        var payload = JsonDocument.Parse(Base64UrlDecode(token.Token.Split('.')[1]));
        payload.RootElement.GetProperty("rls_rules")[0].GetProperty("clause").GetString().Should().Be($"tenant_id = '{company:D}'");
    }

    // Superset issues a guest token only for an embedded dashboard, so the id the service is given is the embedded uuid, not the dashboard's own id.
    private async Task<string> CreateEmbeddedDashboardAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{_superset.Hostname}:{_superset.GetMappedPublicPort(Port)}/") };
        var login = await http.PostAsJsonAsync("api/v1/security/login", new { username = "admin", password = "admin", provider = "db", refresh = false });
        login.EnsureSuccessStatusCode();
        http.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString());
        var created = await http.PostAsJsonAsync("api/v1/dashboard/", new { dashboard_title = "Sales" });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var embed = await http.PostAsJsonAsync($"api/v1/dashboard/{id}/embedded", new { allowed_domains = Array.Empty<string>() });
        embed.EnsureSuccessStatusCode();
        return (await embed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("uuid").GetString()!;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
