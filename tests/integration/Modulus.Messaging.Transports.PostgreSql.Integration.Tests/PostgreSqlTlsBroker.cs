using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>
/// A PostgreSQL 17 server with SSL on, using a certificate created for this fixture. The key is owned by the postgres user
/// (uid 70 in the Alpine image) and kept at mode 0600, as the server requires.
/// </summary>
public sealed class PostgreSqlTlsBroker : IAsyncLifetime
{
    private const int PostgresUser = 70;
    private const string CertificatePath = "/var/lib/postgresql/server.crt";
    private const string KeyPath = "/var/lib/postgresql/server.key";

    private readonly TlsTestCertificate _tls = TlsTestCertificate.Create();
    private readonly PostgreSqlContainer _container;
    private readonly string _rootCertificatePath = Path.Combine(Path.GetTempPath(), $"modulus-pg-root-{Guid.NewGuid():N}.pem");
    private NpgsqlDataSource? _dataSource;

    public PostgreSqlTlsBroker()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("modulus_messaging")
            .WithUsername("modulus")
            .WithPassword("modulus-test-secret")
            .WithResourceMapping(Encoding.UTF8.GetBytes(_tls.CertificatePem), CertificatePath, PostgresUser, PostgresUser, UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithResourceMapping(Encoding.UTF8.GetBytes(_tls.PrivateKeyPem), KeyPath, PostgresUser, PostgresUser, UnixFileModes.UserRead | UnixFileModes.UserWrite)
            .WithCommand("-c", "ssl=on", "-c", $"ssl_cert_file={CertificatePath}", "-c", $"ssl_key_file={KeyPath}")
            // Port-based readiness, as in the plain broker: rootless Podman cannot run pg_isready inside the container.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(5432))
            .Build();
    }

    /// <summary>A connection string that verifies the server certificate against the fixture's certificate.</summary>
    public string ConnectionString => $"{_container.GetConnectionString()};SSL Mode=VerifyFull;Root Certificate={_rootCertificatePath}";

    /// <summary>The same server with no trusted root: a verifying client must refuse it.</summary>
    public string UntrustedConnectionString => $"{_container.GetConnectionString()};SSL Mode=VerifyFull";

    public async Task InitializeAsync()
    {
        await File.WriteAllTextAsync(_rootCertificatePath, _tls.CertificatePem);
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(ConnectionString);

        // The port can accept connections before the server finishes its start (the proxy forwards at once), so wait for a
        // real TLS session, not just an open port.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync();
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
        File.Delete(_rootCertificatePath);
    }
}
