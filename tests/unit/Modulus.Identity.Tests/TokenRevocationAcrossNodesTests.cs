using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// TEST-3: a session revoked on one node is gone from another node at its next check. The session list and the token checks read the
/// stored OpenIddict entries on every request, so the measured staleness is zero. Each node is its own container over one SQLite file.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenRevocationAcrossNodesTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-revoke-nodes-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _nodes = [];

    public void Dispose()
    {
        foreach (var node in _nodes)
            node.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite($"Data Source={_databasePath}").UseOpenIddict());
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        services.AddModulusIdentityStore<ModulusIdentityDbContext>();
        var provider = services.BuildServiceProvider();
        _nodes.Add(provider);
        return provider;
    }

    private async Task<(ServiceProvider Writer, ServiceProvider Reader, Guid UserId)> TwoNodesAsync()
    {
        var writer = Node();
        using (var scope = writer.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();

        Guid userId;
        using (var scope = writer.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = "ann", Email = "ann@example.test" };
            (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
            userId = user.Id;

            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            await tokens.CreateAsync(new OpenIddictTokenDescriptor
            {
                Subject = userId.ToString(),
                Type = OpenIddictConstants.TokenTypeHints.AccessToken,
                Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = DateTimeOffset.UtcNow,
                ExpirationDate = DateTimeOffset.UtcNow.AddHours(1),
            });
            await tokens.CreateAsync(new OpenIddictTokenDescriptor
            {
                Subject = userId.ToString(),
                Type = OpenIddictConstants.TokenTypeHints.RefreshToken,
                Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = DateTimeOffset.UtcNow,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(1),
            });
        }

        return (writer, Node(), userId);
    }

    private static async Task<int> ActiveOnAsync(ServiceProvider node, Guid userId)
    {
        using var scope = node.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IUserSessionService>().ListAsync(userId)).Count;
    }

    [Fact]
    public async Task Revoking_every_session_on_one_node_empties_the_list_on_the_other_at_once()
    {
        var (writer, reader, userId) = await TwoNodesAsync();
        (await ActiveOnAsync(reader, userId)).Should().Be(2);

        using (var scope = writer.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IUserSessionService>().RevokeAllAsync(userId, "test")).Should().Be(2);

        (await ActiveOnAsync(reader, userId)).Should().Be(0, "the other node's next list reads the revoked entries");
    }

    [Fact]
    public async Task Revoking_one_session_on_one_node_removes_it_from_the_other_at_once()
    {
        var (writer, reader, userId) = await TwoNodesAsync();
        string sessionId;
        using (var scope = reader.CreateScope())
            sessionId = (await scope.ServiceProvider.GetRequiredService<IUserSessionService>().ListAsync(userId))[0].Id;

        using (var scope = writer.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<IUserSessionService>().RevokeAsync(userId, sessionId)).Should().BeTrue();

        (await ActiveOnAsync(reader, userId)).Should().Be(1);
    }
}
