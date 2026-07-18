using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the <see cref="FieldAuthorizer"/> enforcement point: read projection masks
/// fields the principal may not see, the write boundary rejects fields above the
/// principal's clearance, both are driven from the same per-request mask, and a field
/// classified on the model is fail-closed even with no registered profile (blueprint
/// §5.9, §11).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldAuthorizerTests
{
    // A candidate profile: recruiters read confidential notes; only comp holders see salary.
    private static readonly FieldSecurityProfile CandidateProfile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Confidential, read: "candidate:notes:read", write: "candidate:notes:write")
        .Classification(FieldClassification.Restricted, read: "candidate:comp:read", write: "candidate:comp:write"));

    private sealed class Candidate
    {
        public string Name { get; set; } = string.Empty;

        [Classified(FieldClassification.Confidential)]
        public string Notes { get; set; } = string.Empty;

        [Classified(FieldClassification.Restricted)]
        public decimal Salary { get; set; }
    }

    private static FieldAuthorizer AuthorizerFor(FieldSecurityProfile? profile, params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(typeof(Candidate), profile));

    [Fact]
    public void Redact_MasksUnreadableFields_AndKeepsReadableOnes()
    {
        var authorizer = AuthorizerFor(CandidateProfile); // holds nothing

        var dto = authorizer.Redact(new Candidate { Name = "Ada", Notes = "strong", Salary = 120_000m });

        dto.Name.Should().Be("Ada", "public fields are never masked");
        dto.Notes.Should().BeNull("confidential notes are reset to their default for a caller without clearance");
        dto.Salary.Should().Be(0m, "restricted salary is masked to its default");
    }

    [Fact]
    public void Redact_PreservesFields_ThePrincipalIsClearedFor()
    {
        var authorizer = AuthorizerFor(CandidateProfile, "candidate:notes:read", "candidate:comp:read");

        var dto = authorizer.Redact(new Candidate { Name = "Ada", Notes = "strong", Salary = 120_000m });

        dto.Notes.Should().Be("strong");
        dto.Salary.Should().Be(120_000m);
    }

    [Fact]
    public void AuthorizeWrite_RejectsProtectedFields_ButAllowsPublicOnes()
    {
        var authorizer = AuthorizerFor(CandidateProfile); // holds nothing

        authorizer.AuthorizeWrite(typeof(Candidate), ["Name"])
            .IsAllowed.Should().BeTrue("a public field is writable");

        var denied = authorizer.AuthorizeWrite(typeof(Candidate), ["Name", "Salary"]);
        denied.IsAllowed.Should().BeFalse();
        denied.Reason.Should().Contain("Salary");
    }

    [Fact]
    public void AuthorizeWrite_AllowsAField_TheCallerIsClearedToWrite()
    {
        AuthorizerFor(CandidateProfile, "candidate:comp:write")
            .AuthorizeWrite(typeof(Candidate), ["Salary"])
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeWrite_RejectsUnknownFieldNames_FailClosed()
    {
        AuthorizerFor(CandidateProfile, "candidate:comp:write")
            .AuthorizeWrite(typeof(Candidate), ["Bonus"])
            .IsAllowed.Should().BeFalse("a field that is not a known property must not be settable");
    }

    [Fact]
    public void ClassifiedField_IsFailClosed_EvenWithNoRegisteredProfile()
    {
        // No profile registered → Empty profile → classification alone protects the field.
        var authorizer = AuthorizerFor(profile: null);

        var dto = authorizer.Redact(new Candidate { Name = "Ada", Notes = "secret", Salary = 99m });

        dto.Name.Should().Be("Ada");
        dto.Notes.Should().BeNull("a classified field with no profile is closed, not open");
        authorizer.AuthorizeWrite(typeof(Candidate), ["Notes"]).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Mask_ExposesResolvedAccess_ForAudit()
    {
        var mask = AuthorizerFor(CandidateProfile, "candidate:notes:read").MaskFor(typeof(Candidate));

        mask.Fields.Should().Contain(f => f.Field == "Salary"
            && f.Classification == FieldClassification.Restricted && !f.CanRead);
        mask.Fields.Should().Contain(f => f.Field == "Notes" && f.CanRead);
        mask.CanRead("Name").Should().BeTrue();
    }

    private sealed class StubRegistry(Type type, FieldSecurityProfile? profile) : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType)
            => resourceType == type ? profile : null;
    }

    private sealed class StubUser(Guid? userId, params string[] permissions) : ICurrentUser
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Guid? UserId => userId;
        public string? UserName => userId?.ToString();
        public string? Email => null;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => [.. _permissions];
    }
}
