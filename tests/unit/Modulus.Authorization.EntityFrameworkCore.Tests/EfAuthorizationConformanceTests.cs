using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Sharing;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Workflow;
using Modulus.Testing.Authorization;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The permission truth table (TEST-1) against the durable EF grant store: every case runs on a fresh database, so the store's own
/// answers, not an in-memory copy, decide each row.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EfAuthorizationConformanceTests
{
    private sealed class EfSubject : IAuthorizationConformanceSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;
        private EfPermissionGrantStore? _store;

        public EfSubject() => Rebuild();

        public PermissionResolver Resolver => new(_store!, Registry());

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        public Task AddGrantAsync(PermissionGrant grant, CancellationToken ct = default)
        {
            var store = _store!;
            return grant.Type is PermissionGrantType.Allow or PermissionGrantType.Deny && grant.Scope is null && grant.ValidFrom is null && grant.ValidUntil is null
                ? AddPlainAsync(store, grant, ct)
                : store.AddScopedGrantAsync(grant, createdBy: null, DateTimeOffset.UtcNow, ct);
        }

        private static async Task AddPlainAsync(EfPermissionGrantStore store, PermissionGrant grant, CancellationToken ct)
        {
            var permissions = new[] { grant.Permission };
            var holder = grant.Holder;
            if (grant.HolderType == GrantHolderType.User)
            {
                var userId = Guid.Parse(holder);
                if (grant.Type == PermissionGrantType.Allow)
                    await store.GrantToUserAsync(userId, permissions, ct);
                else
                    await store.DenyToUserAsync(userId, permissions, ct);
            }
            else
            {
                if (grant.Type == PermissionGrantType.Allow)
                    await store.GrantToRoleAsync(holder, permissions, ct);
                else
                    await store.DenyToRoleAsync(holder, permissions, ct);
            }
        }

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
            _store = _provider.GetRequiredService<EfPermissionGrantStore>();
        }

        private static PermissionRegistry Registry()
        {
            var registry = new PermissionRegistry();
            registry.Add(AuthorizationConformance.Read, "Read orders.", null);
            registry.Add(AuthorizationConformance.Approve, "Approve orders.", null);
            registry.Freeze();
            return registry;
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }
    }

    [Fact]
    public async Task The_durable_grant_store_passes_every_case_of_the_truth_table()
    {
        using var subject = new EfSubject();
        using var otherCompany = new EfSubject();

        var report = await AuthorizationConformance.RunAsync(subject, otherCompany);

        report.Failures.Should().BeEmpty("the EF store must answer every row: " + string.Join("; ", report.Failures.Select(f => $"{f.Name}: {f.Problem}")));
    }

    private sealed class DurableResourceSubject(IResourceConformanceSubject inner) : IResourceConformanceSubject
    {
        public RecordShares Shares => inner.Shares;

        public Task ResetAsync(CancellationToken ct = default) => inner.ResetAsync(ct);
    }

    // The durable share store, over a fresh database per reset, so each record-level case starts with no shares.
    private sealed class EfResourceSubject : IResourceConformanceSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;

        public EfResourceSubject() => Rebuild();

        public RecordShares Shares => new(_provider!.GetRequiredService<IRecordShareStore>(), new Modulus.Core.Null.NullCurrentTenant(), TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new RecordShareOptions()));

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }
    }

    [Fact]
    public async Task The_durable_share_store_passes_every_record_level_case()
    {
        using var subject = new EfResourceSubject();

        var report = await ResourceConformance.RunAsync(subject);

        report.Failures.Should().BeEmpty("the EF share store must answer every row: " + string.Join("; ", report.Failures.Select(f => $"{f.Name}: {f.Problem}")));
    }

    // The durable approval-limit store over a fresh database per reset.
    private sealed class EfApprovalSubject : IApprovalConformanceSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;

        public EfApprovalSubject() => Rebuild();

        public IApprovalAuthorityStore Store => _provider!.GetRequiredService<IApprovalAuthorityStore>();

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        public Task AddAsync(Modulus.Authorization.Approval.ApprovalAuthority authority, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfApprovalAuthorityStore>().AddAsync(authority, null, DateTimeOffset.UtcNow, ct);

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }
    }

    [Fact]
    public async Task The_durable_approval_store_passes_every_limit_case()
    {
        using var subject = new EfApprovalSubject();

        var report = await ApprovalConformance.RunAsync(subject);

        report.Failures.Should().BeEmpty("the EF approval store must answer every row: " + string.Join("; ", report.Failures.Select(f => $"{f.Name}: {f.Problem}")));
    }

    // The durable limit and delegation stores over a fresh database per reset.
    private sealed class EfDelegationSubject : IApprovalDelegationSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;

        public EfDelegationSubject() => Rebuild();

        public IApprovalAuthorityStore Limits => _provider!.GetRequiredService<IApprovalAuthorityStore>();

        public IDelegationStore Delegations => _provider!.GetRequiredService<IDelegationStore>();

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        public Task AddLimitAsync(Modulus.Authorization.Approval.ApprovalAuthority authority, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfApprovalAuthorityStore>().AddAsync(authority, null, DateTimeOffset.UtcNow, ct);

        public Task<Modulus.Authorization.Governance.Delegation> DelegateAsync(
            Guid fromUserId, string fromRole, Guid toUserId, string permission,
            DateTimeOffset notBefore, DateTimeOffset notAfter, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfDelegationStore>().DelegateAsync(fromUserId, [fromRole], toUserId, [permission], notBefore, notAfter, ct);

        public Task RevokeAsync(Guid delegationId, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfDelegationStore>().RevokeAsync(delegationId, ct);

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }
    }

    // The durable chain and request stores under the production workflow, over a fresh database per reset.
    private sealed class EfWorkflowSubject : IApprovalWorkflowConformanceSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;

        public EfWorkflowSubject() => Rebuild();

        public IApprovalWorkflow WorkflowAs(Guid userId, IReadOnlyCollection<string> permissions)
        {
            var scope = _provider!.CreateScope();
            scope.ServiceProvider.GetRequiredService<WorkflowUser>().Act(userId, permissions);
            return scope.ServiceProvider.GetRequiredService<IApprovalWorkflow>();
        }

        public Task AddChainAsync(ApprovalChain chain, CancellationToken ct = default)
            => _provider!.GetRequiredService<IApprovalChainStore>().SaveAsync(chain, ct);

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            services.AddScoped<WorkflowUser>();
            services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<WorkflowUser>());
            services.AddModulusAuthorization();
            services.AddApprovalWorkflow(o => o.EnableEscalation = false);
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }

        // The signed-in user the workflow reads for each scope.
        private sealed class WorkflowUser : ICurrentUser
        {
            private Guid? _id;
            private IReadOnlyList<string> _permissions = [];

            public void Act(Guid userId, IReadOnlyCollection<string> permissions)
            {
                _id = userId;
                _permissions = [.. permissions];
            }

            public Guid? UserId => _id;
            public string? UserName => null;
            public string? Email => null;
            public bool IsAuthenticated => _id is not null;
            public bool IsInRole(string role) => false;
            public bool HasPermission(string permission) => _permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
            public IReadOnlyList<string> Permissions => _permissions;
        }
    }

    [Fact]
    public async Task The_durable_workflow_passes_every_step_rule_case()
    {
        using var subject = new EfWorkflowSubject();

        var report = await ApprovalWorkflowConformance.RunAsync(subject);

        report.EnsurePassed();
    }

    // The durable org hierarchy and placement stores over a fresh database per reset.
    private sealed class EfOrgSubject : IOrgPlacementConformanceSubject, IDisposable
    {
        private SqliteConnection? _connection;
        private ServiceProvider? _provider;

        public EfOrgSubject() => Rebuild();

        public IOrgHierarchy Hierarchy => _provider!.GetRequiredService<EfOrgHierarchy>();

        public IOrgPlacementStore Placements => _provider!.GetRequiredService<EfOrgPlacementStore>();

        public Task ResetAsync(CancellationToken ct = default)
        {
            Rebuild();
            return Task.CompletedTask;
        }

        public Task AddUnitAsync(Guid id, Guid[] parents, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfOrgHierarchy>().AddUnitAsync(id, parents, ct);

        public Task PlaceAsync(Guid userId, Guid orgUnitId, OrgScopeMode mode, CancellationToken ct = default)
            => _provider!.GetRequiredService<EfOrgPlacementStore>().PlaceAsync(userId, orgUnitId, mode, ct);

        private void Rebuild()
        {
            _provider?.Dispose();
            _connection?.Dispose();
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();
            using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            _connection?.Dispose();
        }
    }

    [Fact]
    public async Task The_durable_org_stores_pass_every_placement_case()
    {
        using var subject = new EfOrgSubject();

        var report = await OrgPlacementConformance.RunAsync(subject);

        report.EnsurePassed();
    }

    [Fact]
    public async Task The_durable_delegation_store_passes_every_delegated_limit_case()
    {
        using var subject = new EfDelegationSubject();

        var report = await ApprovalDelegationConformance.RunAsync(subject);

        report.Failures.Should().BeEmpty("the EF delegation store must answer every row: " + string.Join("; ", report.Failures.Select(f => $"{f.Name}: {f.Problem}")));
    }
}
