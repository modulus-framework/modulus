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

            await context.SaveChangesAsync();

            logger.LogInformation("Billing module seeding completed: {InvoiceCount} invoices, {PaymentCount} payments, {CreditNoteCount} credit notes added",
                sampleInvoices.Length, payments.Length, creditNotes.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Billing module");
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
        invoice.AddLine(Guid.NewGuid(), "Product A", "Qty 100", 100m, 50m);
        invoice.AddLine(Guid.NewGuid(), "Product B", "Qty 50", 50m, 30m);

        return invoice;
    }
}
