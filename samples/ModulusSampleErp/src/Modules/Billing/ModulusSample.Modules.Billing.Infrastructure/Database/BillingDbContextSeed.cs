using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Billing.Domain.Entities;

namespace ModulusSample.Modules.Billing.Infrastructure.Database;

public static class BillingDbContextSeed
{
    public static async Task SeedAsync(
        BillingDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid orgUnitId)
    {
        try
        {
            if (await context.Invoices.AnyAsync())
                return;

            var invoices = new[]
            {
                Invoice.Create(Guid.NewGuid(), "INV-2024-001", Guid.NewGuid(), Guid.NewGuid(), orgUnitId, tenantId).Value,
                Invoice.Create(Guid.NewGuid(), "INV-2024-002", Guid.NewGuid(), Guid.NewGuid(), orgUnitId, tenantId).Value,
                Invoice.Create(Guid.NewGuid(), "INV-2024-003", Guid.NewGuid(), Guid.NewGuid(), orgUnitId, tenantId).Value,
            };

            foreach (var invoice in invoices)
            {
                invoice.AddLine(Guid.NewGuid(), "Sample Product", 5, 100.00m);
            }

            context.Invoices.AddRange(invoices);
            await context.CommitAsync();

            logger.LogInformation("Billing module seeding completed: {InvoiceCount} invoices added", invoices.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Billing module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
        BillingDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid orgUnitId,
        Guid financeUserId)
    {
        try
        {
            if (await context.Invoices.AnyAsync())
                return;

            var invoices = new[]
            {
                // Paid invoices
                Invoice.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa001"),
                    "INV-2024-001",
                    Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                    Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                    orgUnitId,
                    tenantId).Value,

                Invoice.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa002"),
                    "INV-2024-002",
                    Guid.Parse("a0000000-0000-0000-0000-000000000002"),
                    Guid.Parse("c0000000-0000-0000-0000-000000000002"),
                    orgUnitId,
                    tenantId).Value,

                // Sent invoices
                Invoice.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa003"),
                    "INV-2024-003",
                    Guid.Parse("a0000000-0000-0000-0000-000000000003"),
                    Guid.Parse("c0000000-0000-0000-0000-000000000003"),
                    orgUnitId,
                    tenantId).Value,

                // Overdue invoice
                Invoice.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa004"),
                    "INV-2024-004",
                    Guid.Parse("a0000000-0000-0000-0000-000000000004"),
                    Guid.Parse("c0000000-0000-0000-0000-000000000004"),
                    orgUnitId,
                    tenantId).Value,

                // Draft invoice
                Invoice.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa005"),
                    "INV-2024-005",
                    Guid.Parse("a0000000-0000-0000-0000-000000000005"),
                    Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                    orgUnitId,
                    tenantId).Value,
            };

            foreach (var invoice in invoices)
            {
                invoice.AddLine(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Widget A", 3, 150.00m, 0.08m);
                invoice.AddLine(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Premium Widget", 2, 250.00m, 0.08m);
            }

            // Set statuses
            invoices[0].Send();
            invoices[0].MarkAsPaid();

            invoices[1].Send();
            invoices[1].MarkAsPaid();

            invoices[2].Send();

            invoices[3].Send();
            invoices[3].MarkAsOverdue();

            context.Invoices.AddRange(invoices);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Billing module seeding completed: {InvoiceCount} invoices with various statuses", invoices.Length);

            // Seed sample payments
            var payments = new[]
            {
                Payment.Create(
                    Guid.NewGuid(),
                    "PAY-2024-001",
                    invoices[0].Id,
                    486.00m,
                    "Credit Card",
                    orgUnitId,
                    tenantId).Value,

                Payment.Create(
                    Guid.NewGuid(),
                    "PAY-2024-002",
                    invoices[1].Id,
                    540.00m,
                    "Bank Transfer",
                    orgUnitId,
                    tenantId).Value,
            };

            payments[0].Confirm("REF-001");
            payments[1].Confirm("REF-002");

            context.Payments.AddRange(payments);
            await context.CommitAsync();

            logger.LogInformation("Added {PaymentCount} sample payments", payments.Length);

            // Seed sample credit note
            var creditNotes = new[]
            {
                CreditNote.Create(
                    Guid.NewGuid(),
                    "CN-2024-001",
                    invoices[0].Id,
                    50.00m,
                    "Customer discount",
                    orgUnitId,
                    tenantId).Value,
            };

            creditNotes[0].Issue();

            context.CreditNotes.AddRange(creditNotes);
            await context.CommitAsync();

            logger.LogInformation("Added {CreditNoteCount} sample credit notes", creditNotes.Length);
            logger.LogInformation("  Finance user: {FinanceUserId}", financeUserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Billing module");
            throw;
        }
    }
}
