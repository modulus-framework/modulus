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

            var customerId = Guid.NewGuid();
            var sampleInvoices = new[]
            {
                CreateInvoice(Guid.NewGuid(), "INV-2026-001", Guid.NewGuid(), customerId, orgUnitId, tenantId),
                CreateInvoice(Guid.NewGuid(), "INV-2026-002", Guid.NewGuid(), Guid.NewGuid(), orgUnitId, tenantId),
                CreateInvoice(Guid.NewGuid(), "INV-2026-003", Guid.NewGuid(), customerId, orgUnitId, tenantId),
            };

            // Set first invoice as sent, second as overdue for demo purposes
            sampleInvoices[0].Send();
            sampleInvoices[1].Send();
            sampleInvoices[1].MarkAsOverdue();

            context.Invoices.AddRange(sampleInvoices);

            // Seed sample payments
            var payments = new[]
            {
                Payment.Create(Guid.NewGuid(), "PAY-2026-001", sampleInvoices[0].Id, 1000m, "Bank Transfer", orgUnitId, tenantId).Value,
                Payment.Create(Guid.NewGuid(), "PAY-2026-002", sampleInvoices[0].Id, 500m, "Bank Transfer", orgUnitId, tenantId).Value,
            };

            payments[0].Confirm("TXN123456");

            context.Payments.AddRange(payments);

            // Seed sample credit notes
            var creditNotes = new[]
            {
                CreditNote.Create(Guid.NewGuid(), "CN-2026-001", sampleInvoices[1].Id, 250m, "Discount provided", orgUnitId, tenantId).Value,
            };

            creditNotes[0].Issue();

            context.CreditNotes.AddRange(creditNotes);

            await context.CommitAsync();

            logger.LogInformation("Billing module seeding completed: {InvoiceCount} invoices, {PaymentCount} payments, {CreditNoteCount} credit notes added",
                sampleInvoices.Length, payments.Length, creditNotes.Length);
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

            // Customer IDs from Partners module
            var acmeCustomerId = Guid.Parse("cust0001-0000-0000-0000-000000000001");
            var globalDistCustomerId = Guid.Parse("cust0002-0000-0000-0000-000000000002");
            var southernWholesaleCustomerId = Guid.Parse("cust0003-0000-0000-0000-000000000003");

            // Product IDs from Catalog module (for margin calculation)
            var widgetAProductId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var premiumWidgetProductId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

            var invoices = new List<Invoice>();

            // Create invoices with field security attributes (cost, tax, margin)
            for (int i = 1; i <= 3; i++)
            {
                var customerId = i == 1 ? acmeCustomerId :
                                  i == 2 ? globalDistCustomerId : southernWholesaleCustomerId;

                var invoice = Invoice.Create(
                    Guid.NewGuid(),
                    $"INV-{DateTime.UtcNow.Year}-{i:D5}",
                    Guid.NewGuid(), // Sales order ID
                    customerId,
                    orgUnitId,
                    tenantId).Value;

                // Add realistic invoice lines with tax information
                invoice.AddLine(widgetAProductId, "Widget A", 10m, 150.00m, 0.005m); // 10 @ $150 = $1,500, tax $7.50
                invoice.AddLine(premiumWidgetProductId, "Premium Widget", 5m, 250.00m, 0.01m); // 5 @ $250 = $1,250, tax $12.50

                if (i <= 2)
                {
                    // Add third line for first 2 invoices
                    invoice.AddLine(Guid.NewGuid(), "Universal Gadget", 3m, 900.00m, 0.01m); // 3 @ $900 = $2,700, tax $27.00
                }

                // Set different states
                if (i == 1)
                {
                    invoice.Send(); // Issued and sent
                }
                else if (i == 2)
                {
                    invoice.Send();
                    invoice.MarkAsOverdue(); // Overdue for AR aging demo
                }
                // Third invoice stays as Draft

                invoices.Add(invoice);
            }

            context.Invoices.AddRange(invoices);

            // Seed sample payments
            var payments = new[]
            {
                Payment.Create(Guid.NewGuid(), "PAY-2026-001", invoices[0].Id, 1000.00m, "Bank Transfer", orgUnitId, tenantId).Value,
                Payment.Create(Guid.NewGuid(), "PAY-2026-002", invoices[0].Id, 500.00m, "Wire Transfer", orgUnitId, tenantId).Value,
            };

            payments[0].Confirm("TXN123456789"); // First payment confirmed
            // Second payment pending

            context.Payments.AddRange(payments);

            // Seed sample credit notes for feature entitlement demo
            var creditNotes = new[]
            {
                CreditNote.Create(Guid.NewGuid(), "CN-2026-001", invoices[1].Id, 250.00m, "Volume Discount", orgUnitId, tenantId).Value,
            };

            creditNotes[0].Issue();

            context.CreditNotes.AddRange(creditNotes);

            await context.CommitAsync();

            logger.LogInformation("Enhanced Billing module seeding completed:");
            logger.LogInformation("  Invoices: {InvoiceCount} invoices with cost/tax/margin for field security demo", invoices.Count);
            logger.LogInformation("  Payments: {PaymentCount} payments in different states", payments.Length);
            logger.LogInformation("  Feature Entitlement Setup: Overdue invoice and credit notes for AR-aging report demo");
            logger.LogInformation("  Field Security: Cost price and margin visible to Finance role only");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Billing module");
            throw;
        }
    }

    private static Invoice CreateInvoice(
        Guid id,
        string number,
        Guid salesOrderId,
        Guid customerId,
        Guid orgUnitId,
        Guid tenantId)
    {
        var invoice = Invoice.Create(id, number, salesOrderId, customerId, orgUnitId, tenantId).Value;

        // Add sample lines
        invoice.AddLine(Guid.NewGuid(), "Product A", 100m, 100m);
        invoice.AddLine(Guid.NewGuid(), "Product B", 50m, 50m);

        return invoice;
    }
}
