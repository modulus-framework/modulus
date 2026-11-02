namespace Modulus.Tenancy.Lifecycle.Tests;

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Tenancy.Lifecycle.Certificates;
using Xunit;

[Trait("Category", "Unit")]
public sealed class LifecycleTests
{
    private static readonly Guid Company = Harness.Company;

    [Fact]
    public async Task Export_archive_holds_json_csv_attachments_and_a_manifest_that_matches_its_contents()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestExportAsync(Company, "admin@example.test");

        await provider.RunOperationAsync(operation.Id);
        var archive = await OpenArchiveAsync(provider, operation.Id);

        operation.State.Should().Be(OperationState.Completed);
        operation.ExportExpiresAt.Should().Be(harness.Clock.Now.AddDays(7));
        archive.Entries.Select(e => e.FullName).Should().Contain(["catalog.products.json", "catalog.products.csv", "billing.invoices.json", "files/uploads/spec.txt", "manifest.json"]);
        archive.Entries.Select(e => e.FullName).Should().NotContain(e => e.StartsWith("catalog.accounts", StringComparison.Ordinal),
            "global identity data is not a company's data");

        var products = JsonDocument.Parse(ReadEntry(archive, "catalog.products.json")).RootElement;
        products.GetArrayLength().Should().Be(2);

        var csv = ReadEntry(archive, "catalog.products.csv").Split('\n');
        csv[0].Should().Contain("Name").And.Contain("Amount");
        csv.Should().Contain(line => line.Contains("'=HYPERLINK", StringComparison.Ordinal),
            "a text cell that starts like a formula is quoted so a spreadsheet shows it as text");

        var manifest = JsonDocument.Parse(ReadEntry(archive, "manifest.json")).RootElement;
        manifest.GetProperty("format").GetString().Should().Be("modulus-tenant-export");
        foreach (var entry in manifest.GetProperty("entries").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var bytes = ReadBytes(archive, name);
            entry.GetProperty("bytes").GetInt64().Should().Be(bytes.Length, name);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
                .Should().Be(entry.GetProperty("sha256").GetString(), name);
        }
    }

    [Fact]
    public async Task The_stored_archive_is_encrypted()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestExportAsync(Company, "admin");

        await provider.RunOperationAsync(operation.Id);

        var stored = harness.Files.Files[operation.ExportPath!];
        Encoding.UTF8.GetString(stored).Should().NotContain("Chair").And.NotContain("catalog.products");
        stored.AsSpan(0, 6).SequenceEqual("MODLX1"u8).Should().BeTrue();
    }

    [Fact]
    public async Task A_damaged_archive_writes_nothing_to_the_caller()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestExportAsync(Company, "admin");
        await provider.RunOperationAsync(operation.Id);
        var stored = harness.Files.Files[operation.ExportPath!];
        stored[stored.Length / 2] ^= 0xFF;
        using var destination = new MemoryStream();

        var act = () => provider.OpenExportAsync(operation.Id, destination);

        await act.Should().ThrowAsync<InvalidDataException>();
        destination.Length.Should().Be(0);
    }

    [Fact]
    public async Task Export_cannot_be_requested_without_its_encryption_key()
    {
        using var harness = new Harness(withExportKey: false);

        var act = () => harness.Provider().RequestExportAsync(Company, "admin");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ExportEncryptionKey*");
    }

    [Fact]
    public async Task An_expired_export_is_deleted_and_refused()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestExportAsync(Company, "admin");
        await provider.RunOperationAsync(operation.Id);
        var path = operation.ExportPath!;

        harness.Clock.Advance(TimeSpan.FromDays(8));
        await provider.RunRetentionAsync();

        operation.State.Should().Be(OperationState.Expired);
        operation.ExportPath.Should().BeNull();
        harness.Files.Files.Should().NotContainKey(path);
        var act = () => provider.OpenExportAsync(operation.Id, new MemoryStream());
        await act.Should().ThrowAsync<TenantLifecycleRefusedException>();
    }

    [Fact]
    public async Task A_legal_hold_keeps_an_export_past_its_lifetime()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestExportAsync(Company, "admin");
        await provider.RunOperationAsync(operation.Id);
        await provider.PlaceLegalHoldAsync(Company, "litigation 2026-17", "legal@example.test");

        harness.Clock.Advance(TimeSpan.FromDays(8));
        await provider.RunRetentionAsync();

        operation.State.Should().Be(OperationState.Completed);
        operation.ExportPath.Should().NotBeNull();
    }

    [Fact]
    public async Task Offboarding_suspends_stops_integrations_deletes_owned_data_and_keeps_the_rest()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();

        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);

        operation.State.Should().Be(OperationState.Completed);
        harness.Status.Statuses.Should().Equal(Modulus.Core.Abstractions.TenantStatus.Suspended);
        harness.World.DisabledSteps.Should().Equal("webhooks");
        harness.World.Deleted.Should().Contain(CatalogHandler.Products).And.Contain(CatalogHandler.Attachments);
        harness.World.Deleted.Should().NotContain(CatalogHandler.Accounts, "global accounts are never deleted with a company");
        harness.World.Rows.Should().ContainKey(CatalogHandler.Accounts);
        harness.World.Deleted.Should().NotContain(BillingHandler.Invoices, "financial records are kept for their period");
        harness.World.Deleted.Should().NotContain(BillingHandler.Trail, "audit records are never deleted by offboarding");

        Outcome(operation, CatalogHandler.Products).Action.Should().Be(DatasetAction.Deleted);
        Outcome(operation, CatalogHandler.Products).Rows.Should().Be(2);
        Outcome(operation, CatalogHandler.Accounts).Action.Should().Be(DatasetAction.Global);
        Outcome(operation, BillingHandler.Invoices).Action.Should().Be(DatasetAction.Retained, "financial period not configured: kept");
        Outcome(operation, BillingHandler.Usage).Action.Should().Be(DatasetAction.PendingDeletion);
        Outcome(operation, BillingHandler.Usage).DeleteAfter.Should().Be(harness.Clock.Now.AddDays(365));
        Outcome(operation, BillingHandler.Trail).Action.Should().Be(DatasetAction.Retained, "audit records keep the hash chain intact");

        harness.World.DeleteContexts.Should().OnlyContain(c => c.Tenant == Company, "handlers run inside the company's context");
        (await provider.VerifyCertificateAsync(operation.Id)).Should().BeTrue();
        var body = JsonDocument.Parse(operation.CertificatePayload!).RootElement;
        body.GetProperty("backupsExpireAt").GetDateTimeOffset().Should().Be(harness.Clock.Now.AddDays(90));
    }

    [Fact]
    public async Task Offboarding_can_export_first_so_the_archive_holds_what_was_deleted()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: true);

        await provider.RunOperationAsync(operation.Id);

        harness.World.Deleted.Should().Contain(CatalogHandler.Products);
        var archive = await OpenArchiveAsync(provider, operation.Id);
        JsonDocument.Parse(ReadEntry(archive, "catalog.products.json")).RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task A_certificate_detects_a_changed_payload()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);

        operation.CertificatePayload += " ";

        (await provider.VerifyCertificateAsync(operation.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_legal_hold_stops_offboarding_before_anything_is_deleted_and_release_resumes_it()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var hold = await provider.PlaceLegalHoldAsync(Company, "audit request", "legal@example.test");
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);

        await provider.RunOperationAsync(operation.Id);

        operation.State.Should().Be(OperationState.OnHold);
        harness.World.Deleted.Should().BeEmpty();

        await provider.ReleaseLegalHoldAsync(hold.Id, "legal@example.test");
        await provider.RunOperationAsync(operation.Id);

        operation.State.Should().Be(OperationState.Completed);
        harness.World.Deleted.Should().Contain(CatalogHandler.Products);
    }

    [Fact]
    public async Task A_failed_offboarding_resumes_without_repeating_finished_work()
    {
        using var harness = new Harness();
        harness.Seed();
        harness.World.FailOnce.Add(CatalogHandler.Attachments);
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);

        var act = () => provider.RunOperationAsync(operation.Id);
        await act.Should().ThrowAsync<IOException>();

        operation.State.Should().Be(OperationState.Failed);
        operation.Error.Should().Be(nameof(IOException), "only the exception type is recorded, never its message");

        await provider.RunOperationAsync(operation.Id);

        operation.State.Should().Be(OperationState.Completed);
        harness.World.DeleteContexts.Count(c => c.Dataset == CatalogHandler.Products).Should().Be(1, "the finished dataset is not deleted twice");
        harness.World.Deleted.Should().Contain(CatalogHandler.Attachments);
    }

    [Fact]
    public async Task A_second_offboarding_is_refused_while_one_is_open()
    {
        using var harness = new Harness();
        var provider = harness.Provider();
        await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);

        var act = () => provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);

        await act.Should().ThrowAsync<TenantLifecycleRefusedException>();
    }

    [Fact]
    public async Task Retention_deletes_the_pending_dataset_when_due_and_writes_a_receipt()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);

        harness.Clock.Advance(TimeSpan.FromDays(10));
        await provider.RunRetentionAsync();
        harness.World.Deleted.Should().NotContain(BillingHandler.Usage, "not yet due");

        harness.Clock.Advance(TimeSpan.FromDays(360));
        await provider.RunRetentionAsync();

        harness.World.Deleted.Should().Contain(BillingHandler.Usage);
        var receipt = (await provider.ListOperationsAsync(Company)).Single(o => o.Kind == OperationKind.RetentionDeletion);
        receipt.Outcomes.Single().Action.Should().Be(DatasetAction.DeletedByRetention);
        receipt.Outcomes.Single().Rows.Should().Be(1);
        (await provider.VerifyCertificateAsync(receipt.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Retention_skips_pending_data_under_a_legal_hold()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);
        await provider.PlaceLegalHoldAsync(Company, "dispute", "legal@example.test");

        harness.Clock.Advance(TimeSpan.FromDays(400));
        await provider.RunRetentionAsync();

        harness.World.Deleted.Should().NotContain(BillingHandler.Usage);
    }

    [Fact]
    public async Task Every_dataset_of_the_company_is_accounted_for_in_the_certificate()
    {
        using var harness = new Harness();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);

        await provider.RunOperationAsync(operation.Id);

        operation.Outcomes.Select(o => o.Dataset).Should().BeEquivalentTo(
            [CatalogHandler.Products, CatalogHandler.Attachments, CatalogHandler.Accounts,
             BillingHandler.Invoices, BillingHandler.Usage, BillingHandler.Trail]);
    }

    private static DatasetOutcome Outcome(LifecycleOperation operation, string dataset) =>
        operation.Outcomes.Single(o => o.Dataset == dataset);

    private static async Task<ZipArchive> OpenArchiveAsync(ITenantLifecycleProvider provider, Guid operationId)
    {
        var buffer = new MemoryStream();
        await provider.OpenExportAsync(operationId, buffer);
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static string ReadEntry(ZipArchive archive, string name) => Encoding.UTF8.GetString(ReadBytes(archive, name));

    private static byte[] ReadBytes(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)?.Open() ?? throw new FileNotFoundException(name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task A_broken_pending_deletion_does_not_block_the_others_in_the_sweep()
    {
        using var harness = new Harness();
        harness.Seed();
        var provider = harness.Provider();
        var operation = await provider.RequestOffboardingAsync(Company, "operator", exportFirst: false);
        await provider.RunOperationAsync(operation.Id);

        // A due item for a company that no longer resolves: it must be skipped, not stop the sweep.
        var orphan = Guid.NewGuid();
        await harness.Store().SavePendingDeletionAsync(new PendingDeletion
        {
            Id = Guid.NewGuid(),
            TenantId = orphan,
            OperationId = operation.Id,
            Module = "Billing",
            Dataset = BillingHandler.Usage,
            DeleteAfter = harness.Clock.Now.AddDays(-1),
        }, default);

        harness.Clock.Advance(TimeSpan.FromDays(366));
        await provider.RunRetentionAsync();

        harness.World.Deleted.Should().Contain(BillingHandler.Usage, "the company's own item is still processed");
        (await harness.Store().ListDuePendingDeletionsAsync(harness.Clock.Now, 10, default))
            .Should().ContainSingle(p => p.TenantId == orphan, "the orphan stays pending for a human to look at");
    }
}
