using System.Runtime.CompilerServices;
using FluentAssertions;
using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>RULE-11: a message identity that was released cannot disappear without a deliberate decision in the contract file.</summary>
[Trait("Category", "Unit")]
public sealed class MessageUrnContractTests
{
    // The contract lives beside this file, under source control, so a change to it shows in the diff.
    private static readonly string ContractPath = Path.Combine(SourceDirectory(), "message-urns.contract");

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    [Fact]
    public void The_service_message_contract_has_no_breaking_change()
    {
        var report = MessageUrnContract.Verify(ContractPath, [typeof(OrderSubmitted), typeof(CreateInvoice)]);

        report.EnsureNoBreakingChange();
    }

    [Fact]
    public void A_recorded_urn_that_is_gone_is_a_breaking_change()
    {
        var path = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.contract");
        File.WriteAllText(path, "urn:billing:invoice-issued:v1 Billing.InvoiceIssued\n");
        try
        {
            var report = MessageUrnContract.Verify(path, [typeof(OrderSubmitted)]);

            report.Removed.Should().ContainSingle().Which.Should().Be("urn:billing:invoice-issued:v1");
            File.ReadAllText(path).Should().Contain("urn:billing:invoice-issued:v1", "a refused removal is not written over the record");
            var act = report.EnsureNoBreakingChange;
            act.Should().Throw<InvalidOperationException>().WithMessage("*RULE-11*urn:billing:invoice-issued:v1*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_removal_accepted_on_purpose_is_dropped_from_the_record_and_new_urns_are_recorded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.contract");
        File.WriteAllText(path, "urn:billing:invoice-issued:v1 Billing.InvoiceIssued\n");
        try
        {
            var report = MessageUrnContract.Verify(path, [typeof(OrderSubmitted)], acceptRemovals: true);

            report.Removed.Should().ContainSingle();
            var written = File.ReadAllText(path);
            written.Should().NotContain("urn:billing:invoice-issued:v1");
            written.Should().Contain(MessageUrns.Of(typeof(OrderSubmitted)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
