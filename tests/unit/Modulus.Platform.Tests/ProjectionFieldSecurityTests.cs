using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A DTO that projects an entity inherits the entity's field classifications, so a classified field copied into the DTO
/// is masked on read and refused on write, even when the DTO itself carries no attribute (F-15).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProjectionFieldSecurityTests
{
    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Restricted, read: "candidate:comp:read", write: "candidate:comp:write"));

    private sealed class Candidate
    {
        public string Name { get; set; } = string.Empty;

        [Classified(FieldClassification.Restricted)]
        public decimal Salary { get; set; }
    }

    // Copies the entity's fields by name and carries no classification of its own.
    [ProjectionOf(typeof(Candidate))]
    private sealed class CandidateSummary
    {
        public string Name { get; set; } = string.Empty;

        public decimal Salary { get; set; }
    }

    // Declares a weaker classification than the entity: the entity's stricter one still applies.
    [ProjectionOf(typeof(Candidate))]
    private sealed class CandidateLoosened
    {
        public string Name { get; set; } = string.Empty;

        [Classified(FieldClassification.Public)]
        public decimal Salary { get; set; }
    }

    private static FieldAuthorizer AuthorizerFor(params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(typeof(CandidateSummary), Profile));

    [Fact]
    public void Redact_MasksAProjectedField_TheEntityClassifies()
    {
        var authorizer = AuthorizerFor();

        var dto = authorizer.Redact(new CandidateSummary { Name = "Ada", Salary = 120_000m });

        dto.Name.Should().Be("Ada");
        dto.Salary.Should().Be(0m, "the entity classifies Salary as Restricted, and the DTO inherits it");
    }

    [Fact]
    public void Redact_KeepsTheProjectedField_ThePrincipalIsClearedFor()
    {
        var authorizer = AuthorizerFor("candidate:comp:read");

        authorizer.Redact(new CandidateSummary { Salary = 120_000m }).Salary.Should().Be(120_000m);
    }

    [Fact]
    public async Task Write_IsRefused_ForAProjectedField_TheCallerCannotWrite()
    {
        var denied = await AuthorizerFor().AuthorizeWriteAsync(typeof(CandidateSummary), ["Salary"]);

        denied.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void A_DTO_cannot_lower_the_entity_classification()
    {
        var map = FieldClassificationMap.For(typeof(CandidateLoosened));

        map["Salary"].Should().Be(FieldClassification.Restricted);
        map["Name"].Should().Be(FieldClassification.Public);
    }

    private sealed class StubRegistry(Type type, FieldSecurityProfile? profile) : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType) => resourceType == type ? profile : null;
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
