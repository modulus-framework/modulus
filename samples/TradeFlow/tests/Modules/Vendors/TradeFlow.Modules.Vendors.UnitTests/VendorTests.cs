using FluentAssertions;
using TradeFlow.Modules.Vendors.Domain.Entities;

namespace TradeFlow.Modules.Vendors.UnitTests;

public class VendorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid VendorId = Guid.NewGuid();

    private static Result<Vendor> CreateValidVendor(
        string name = "TestVendor",
        string country = "BD",
        string? tin = "1234567890",
        string? bin = null) =>
        Vendor.Create(VendorId, TenantId, name, "Test Vendor Ltd", country,
            VendorType.Manufacturer, tin, bin, "test@example.com", "+8801712345678",
            "Dhaka, Bangladesh", "admin");

    /// <summary>Walks vendor through Draft → Submitted → UnderReview → Qualified → Active.</summary>
    private static Vendor CreateActiveVendor()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");
        vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)), "admin");
        vendor.Activate();
        return vendor;
    }

    [Fact]
    public void Create_ValidVendor_ReturnsSuccess()
    {
        var result = CreateValidVendor();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(VendorStatus.Draft);
        result.Value.Name.Should().Be("TestVendor");
    }

    [Fact]
    public void Create_EmptyName_ReturnsFailure()
    {
        var result = Vendor.Create(VendorId, TenantId, "", "Legal", "BD",
            VendorType.Manufacturer, "123", null, null, null, null, "admin");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Vendor.EmptyName");
    }

    [Fact]
    public void Create_BangladeshiWithoutTinOrBin_ReturnsFailure()
    {
        var result = Vendor.Create(VendorId, TenantId, "Vendor", "Legal", "BD",
            VendorType.Manufacturer, null, null, null, null, null, "admin");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Vendor.TinOrBinRequired");
    }

    [Fact]
    public void Create_ForeignVendorWithoutTin_ReturnsSuccess()
    {
        var result = Vendor.Create(VendorId, TenantId, "ForeignCo", "Foreign Co Ltd", "CN",
            VendorType.Manufacturer, null, null, null, null, null, "admin");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Submit_FromDraft_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.Submit();

        result.IsSuccess.Should().BeTrue();
        vendor.Status.Should().Be(VendorStatus.Submitted);
    }

    [Fact]
    public void Submit_FromActive_ReturnsFailure()
    {
        var vendor = CreateActiveVendor();

        var result = vendor.Submit();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Vendor.InvalidTransition");
    }

    [Fact]
    public void Qualify_FromUnderReview_TransitionsToQualified()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");

        var result = vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)), "admin");

        result.IsSuccess.Should().BeTrue();
        vendor.Status.Should().Be(VendorStatus.Qualified);
        vendor.Qualifications.Should().HaveCount(1);
    }

    [Fact]
    public void Qualify_InvalidDateRange_ReturnsFailure()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");

        var result = vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), "admin");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Vendor.BadQualificationWindow");
    }

    [Fact]
    public void Activate_FromQualified_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");
        vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)), "admin");

        var result = vendor.Activate();

        result.IsSuccess.Should().BeTrue();
        vendor.Status.Should().Be(VendorStatus.Active);
    }

    [Fact]
    public void Activate_FromDraft_ReturnsFailure()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.Activate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Suspend_FromActive_ReturnsSuccess()
    {
        var vendor = CreateActiveVendor();

        var result = vendor.Suspend("Quality issues");

        result.IsSuccess.Should().BeTrue();
        vendor.Status.Should().Be(VendorStatus.Suspended);
    }

    [Fact]
    public void Suspend_EmptyReason_ReturnsFailure()
    {
        var vendor = CreateActiveVendor();

        var result = vendor.Suspend("");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Blacklist_FromActive_ReturnsSuccess()
    {
        var vendor = CreateActiveVendor();

        var result = vendor.Blacklist("Sanctions hit", "compliance-officer");

        result.IsSuccess.Should().BeTrue();
        vendor.Status.Should().Be(VendorStatus.Blacklisted);
        vendor.BlacklistReason.Should().Be("Sanctions hit");
        vendor.BlacklistedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Blacklist_FromDraft_ReturnsFailure()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.Blacklist("Reason", "admin");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CanTransact_OnlyWhenActive()
    {
        var vendor = CreateValidVendor().Value!;

        vendor.CanTransact().Should().BeFalse();

        vendor.Submit();
        vendor.CanTransact().Should().BeFalse();

        vendor.StartReview("reviewer");
        vendor.CanTransact().Should().BeFalse();

        vendor.Qualify("Cat", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)), "admin");
        vendor.CanTransact().Should().BeFalse();

        vendor.Activate();
        vendor.CanTransact().Should().BeTrue();
    }

    [Fact]
    public void IsQualifiedForCategory_ValidUnexpiredQualification_ReturnsTrue()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");
        vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(365)), "admin");

        vendor.IsQualifiedForCategory("Electronics", DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeTrue();
    }

    [Fact]
    public void IsQualifiedForCategory_ExpiredQualification_ReturnsFalse()
    {
        var vendor = CreateValidVendor().Value!;
        vendor.Submit();
        vendor.StartReview("reviewer");
        vendor.Qualify("Electronics", "CERT-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-400)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), "admin");

        vendor.IsQualifiedForCategory("Electronics", DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public void AddBankAccount_ValidAccount_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;
        var bankAccountId = Guid.NewGuid();

        var result = vendor.AddBankAccount(bankAccountId, "Dutch-Bangla", "TestVendor",
            "1234567890", "Gulshan", "DBBLBDDH", "maker-user");

        result.IsSuccess.Should().BeTrue();
        vendor.BankAccounts.Should().HaveCount(1);
    }

    [Fact]
    public void AddBankAccount_EmptyBankName_ReturnsFailure()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.AddBankAccount(Guid.NewGuid(), "", "Name", "123", "Branch", "SWIFT", "maker");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ApproveBankAccount_DifferentUser_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;
        var bankAccountId = Guid.NewGuid();
        vendor.AddBankAccount(bankAccountId, "Dutch-Bangla", "TestVendor",
            "1234567890", "Gulshan", "DBBLBDDH", "maker-user");

        var result = vendor.ApproveBankAccount(bankAccountId, "checker-user");

        result.IsSuccess.Should().BeTrue();
        vendor.BankAccounts.First().Status.Should().Be(BankAccountStatus.Approved);
    }

    [Fact]
    public void ApproveBankAccount_SameUser_ReturnsFailure_SoD()
    {
        var vendor = CreateValidVendor().Value!;
        var bankAccountId = Guid.NewGuid();
        vendor.AddBankAccount(bankAccountId, "Dutch-Bangla", "TestVendor",
            "1234567890", "Gulshan", "DBBLBDDH", "same-user");

        var result = vendor.ApproveBankAccount(bankAccountId, "same-user");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void RejectBankAccount_ValidRejection_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;
        var bankAccountId = Guid.NewGuid();
        vendor.AddBankAccount(bankAccountId, "Dutch-Bangla", "TestVendor",
            "1234567890", "Gulshan", "DBBLBDDH", "maker-user");

        var result = vendor.RejectBankAccount(bankAccountId, "Invalid documents", "checker-user");

        result.IsSuccess.Should().BeTrue();
        vendor.BankAccounts.First().Status.Should().Be(BankAccountStatus.Rejected);
        vendor.BankAccounts.First().RejectionReason.Should().Be("Invalid documents");
    }

    [Fact]
    public void AddDocument_ValidDocument_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.AddDocument(Guid.NewGuid(), VendorDocumentType.TradeLicense,
            "TL-2026-001", "s3://bucket/key", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)), "admin");

        result.IsSuccess.Should().BeTrue();
        vendor.Documents.Should().HaveCount(1);
    }

    [Fact]
    public void AddDocument_BlacklistedVendor_ReturnsFailure()
    {
        var vendor = CreateActiveVendor();
        vendor.Blacklist("Reason", "admin");

        var result = vendor.AddDocument(Guid.NewGuid(), VendorDocumentType.TradeLicense,
            "TL-001", "s3://key", null, "admin");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AddScorecard_ValidScores_ReturnsSuccess()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.AddScorecard(
            DateOnly.FromDateTime(DateTime.UtcNow), 85m, 90m, 75m, 80m, 95m, "admin");

        result.IsSuccess.Should().BeTrue();
        vendor.Scorecards.Should().HaveCount(1);
        vendor.Scorecards.First().WeightedAverage.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void AddScorecard_OutOfRangeScore_ReturnsFailure()
    {
        var vendor = CreateValidVendor().Value!;

        var result = vendor.AddScorecard(
            DateOnly.FromDateTime(DateTime.UtcNow), 150m, 90m, 75m, 80m, 95m, "admin");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Vendor.ScoreOutOfRange");
    }

    [Fact]
    public void DuplicateKey_IncludesTinAndName()
    {
        var vendor = CreateValidVendor(tin: "1234567890").Value!;

        var key = vendor.DuplicateKey;

        key.Should().Contain("tin:1234567890");
        key.Should().Contain("name:TESTVENDOR");
        key.Should().Contain("country:BD");
    }
}
