using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Modulus.AspNetCore.Configuration;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// Exercises the guard's detection logic: a sensitive value is flagged only when it
// is *effectively* sourced from a committed appsettings*.json under the content root,
// and never when it comes from another provider (env vars) or an out-of-tree file
// (User Secrets). Uses real temp files so the physical-path resolution is exercised.
[Trait("Category", "Unit")]
public sealed class SecretsGuardScannerTests : IDisposable
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("modulus-secrets-guard-").FullName;
    private readonly List<string> _extraDirs = [];

    private string WriteJson(string fileName, string json, string? directory = null)
    {
        var dir = directory ?? _contentRoot;
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static IReadOnlyList<SecretViolation> Scan(IConfigurationRoot root, string contentRoot)
        => SecretsGuardScanner.Scan(root, contentRoot, new SecretsGuardOptions());

    [Fact]
    public void FlagsSecretCommittedToAppSettings()
    {
        var appsettings = WriteJson("appsettings.json", """{ "Auth": { "ApiKey": "sk-live-abc123" } }""");
        var root = new ConfigurationBuilder().AddJsonFile(appsettings).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().ContainSingle(v => v.Key == "Auth:ApiKey");
    }

    [Fact]
    public void IgnoresSecretOverriddenByEnvironment()
    {
        var appsettings = WriteJson("appsettings.json", """{ "Auth": { "ApiKey": "sk-live-abc123" } }""");
        // The in-memory provider stands in for environment variables: it wins over the
        // JSON file, so the effective value no longer originates from a committed file.
        var root = new ConfigurationBuilder()
            .AddJsonFile(appsettings)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:ApiKey"] = "sk-from-env" })
            .Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void IgnoresSecretFromFileOutsideContentRoot()
    {
        // Model a User Secrets file: a JSON provider whose file lives outside the app.
        var userSecretsDir = Directory.CreateTempSubdirectory("modulus-user-secrets-").FullName;
        _extraDirs.Add(userSecretsDir);
        var secrets = WriteJson("secrets.json", """{ "Auth": { "ApiKey": "sk-live-abc123" } }""", userSecretsDir);
        var root = new ConfigurationBuilder().AddJsonFile(secrets).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void IgnoresLocalConnectionString()
    {
        var appsettings = WriteJson("appsettings.json", """
            {
              "ConnectionStrings": {
                "Sqlite": "Data Source=app.db",
                "LocalPg": "Host=localhost;Database=app;Username=postgres;Password=postgres"
              }
            }
            """);
        var root = new ConfigurationBuilder().AddJsonFile(appsettings).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FlagsRemoteConnectionStringWithCredential()
    {
        var appsettings = WriteJson("appsettings.json", """
            {
              "ConnectionStrings": {
                "Prod": "Host=db.example.com;Database=app;Username=svc;Password=hunter2"
              }
            }
            """);
        var root = new ConfigurationBuilder().AddJsonFile(appsettings).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().ContainSingle(v => v.Key == "ConnectionStrings:Prod");
    }

    [Fact]
    public void IgnoresNonSensitiveKeys()
    {
        var appsettings = WriteJson("appsettings.json", """
            { "Logging": { "LogLevel": { "Default": "Information" } }, "AllowedHosts": "*" }
            """);
        var root = new ConfigurationBuilder().AddJsonFile(appsettings).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void ReportsSourceFileName()
    {
        var appsettings = WriteJson("appsettings.json", """{ "ClientSecret": "shhh-value" } """);
        var root = new ConfigurationBuilder().AddJsonFile(appsettings).Build();

        var violations = Scan(root, _contentRoot);

        violations.Should().ContainSingle()
            .Which.Source.Should().Be("appsettings.json");
    }

    public void Dispose()
    {
        TryDelete(_contentRoot);
        foreach (var dir in _extraDirs)
            TryDelete(dir);
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
    }
}
