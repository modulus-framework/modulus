namespace Modulus.Tenancy.Lifecycle.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class EfStoreTests : IAsyncLifetime
{
    private static readonly Guid Company = Guid.Parse("3f2c0000-0000-0000-0000-00000000c0de");
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;
    private TestDb _db = null!;
    private EfTenantLifecycleStore<TestDb> _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options;
        _db = new TestDb(options);
        await _db.Database.EnsureCreatedAsync();
        _store = new EfTenantLifecycleStore<TestDb>(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task An_operation_round_trips_with_its_steps_outcomes_and_certificate()
    {
        var operation = new LifecycleOperation
        {
            TenantId = Company,
            Kind = OperationKind.Offboarding,
            State = OperationState.Completed,
            RequestedBy = "operator",
            RequestedAt = Now,
            CompletedAt = Now.AddMinutes(5),
            ExportFirst = true,
            ExportPath = "lifecycle/exports/x/y.zip.enc",
            ExportExpiresAt = Now.AddDays(7),
            CertificatePayload = "{\"datasets\":[]}",
            CertificateSignature = "c2ln",
            CertificateKeyId = "default",
        };
        operation.Complete("suspend", Now.AddSeconds(1));
        operation.Outcomes.Add(new DatasetOutcome("Catalog", "catalog.products", TenantDataCategory.Owned, DatasetAction.Deleted, 42));
        operation.Outcomes.Add(new DatasetOutcome("Billing", "billing.usage", TenantDataCategory.Retained, DatasetAction.PendingDeletion, 3, Now.AddDays(365)));
        await _store.AddOperationAsync(operation, default);

        var loaded = await _store.FindOperationAsync(operation.Id, default);

        loaded.Should().NotBeNull();
        loaded!.Kind.Should().Be(OperationKind.Offboarding);
        loaded.State.Should().Be(OperationState.Completed);
        loaded.ExportExpiresAt.Should().Be(Now.AddDays(7));
        loaded.HasStep("suspend").Should().BeTrue();
        loaded.Outcomes.Should().HaveCount(2);
        loaded.Outcomes[1].Should().Be(operation.Outcomes[1]);
        loaded.CertificatePayload.Should().Be(operation.CertificatePayload);
        loaded.CertificateKeyId.Should().Be("default");
    }

    [Fact]
    public async Task Saving_an_operation_keeps_its_progress()
    {
        var operation = new LifecycleOperation { TenantId = Company, Kind = OperationKind.Export, RequestedBy = "admin", RequestedAt = Now };
        await _store.AddOperationAsync(operation, default);

        operation.State = OperationState.Running;
        operation.Complete("export", Now.AddSeconds(2));
        await _store.SaveOperationAsync(operation, default);

        var loaded = (await _store.FindOperationAsync(operation.Id, default))!;
        loaded.State.Should().Be(OperationState.Running);
        loaded.HasStep("export").Should().BeTrue();
    }

    [Fact]
    public async Task Active_operations_are_the_ones_still_running_or_waiting()
    {
        var pending = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.Pending };
        var onHold = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.OnHold };
        var done = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.Completed };
        foreach (var op in new[] { pending, onHold, done })
            await _store.AddOperationAsync(op, default);

        var active = await _store.ListActiveOperationsAsync(default);

        active.Select(o => o.Id).Should().BeEquivalentTo([pending.Id, onHold.Id]);
    }

    [Fact]
    public async Task Expired_exports_are_found_only_when_their_archive_is_still_there()
    {
        var expired = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.Completed, ExportPath = "a.enc", ExportExpiresAt = Now.AddDays(-1) };
        var fresh = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.Completed, ExportPath = "b.enc", ExportExpiresAt = Now.AddDays(6) };
        var gone = new LifecycleOperation { TenantId = Company, RequestedAt = Now, State = OperationState.Expired, ExportPath = null, ExportExpiresAt = Now.AddDays(-1) };
        foreach (var op in new[] { expired, fresh, gone })
            await _store.AddOperationAsync(op, default);

        var found = await _store.ListExpiredExportsAsync(Now, default);

        found.Select(o => o.Id).Should().Equal(expired.Id);
    }

    [Fact]
    public async Task Only_active_legal_holds_of_the_company_are_listed_and_a_release_persists()
    {
        var hold = new LegalHold { TenantId = Company, Reason = "litigation", PlacedBy = "legal", PlacedAt = Now };
        var other = new LegalHold { TenantId = Guid.NewGuid(), Reason = "other", PlacedBy = "legal", PlacedAt = Now };
        await _store.AddLegalHoldAsync(hold, default);
        await _store.AddLegalHoldAsync(other, default);

        (await _store.ListActiveLegalHoldsAsync(Company, default)).Select(h => h.Id).Should().Equal(hold.Id);

        hold.ReleasedBy = "legal";
        hold.ReleasedAt = Now.AddDays(1);
        await _store.SaveLegalHoldAsync(hold, default);

        (await _store.ListActiveLegalHoldsAsync(Company, default)).Should().BeEmpty();
        (await _store.FindLegalHoldAsync(hold.Id, default))!.ReleasedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public async Task Due_pending_deletions_are_listed_once_and_saving_updates_in_place()
    {
        var id = Guid.NewGuid();
        await _store.SavePendingDeletionAsync(new PendingDeletion
        {
            Id = id,
            TenantId = Company,
            OperationId = Guid.NewGuid(),
            Module = "Billing",
            Dataset = "billing.usage",
            DeleteAfter = Now,
        }, default);
        await _store.SavePendingDeletionAsync(new PendingDeletion
        {
            Id = Guid.NewGuid(),
            TenantId = Company,
            OperationId = Guid.NewGuid(),
            Module = "Billing",
            Dataset = "billing.later",
            DeleteAfter = Now.AddDays(30),
        }, default);

        var due = await _store.ListDuePendingDeletionsAsync(Now.AddHours(1), 10, default);
        due.Should().ContainSingle().Which.Id.Should().Be(id);

        due[0].DoneAt = Now.AddHours(1);
        due[0].Rows = 7;
        await _store.SavePendingDeletionAsync(due[0], default);

        (await _store.ListDuePendingDeletionsAsync(Now.AddHours(1), 10, default)).Should().BeEmpty();
        _db.PendingDeletions.Count().Should().Be(2, "the done item is kept as a record, not duplicated");
    }
}

public sealed class TestDb(DbContextOptions<TestDb> options) : TenantLifecycleDbContext(options);
