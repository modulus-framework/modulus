namespace Modulus.AI.Connector.Tests;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.AI.Connector.Capabilities;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

/// <summary>
/// <c>Ai:Connector:MaskPersonalInformation</c>: a <c>[PersonalInformation]</c> or <c>[ProtectedPersonalData]</c> field is read-gated like a
/// Restricted one, by the clearance the entity's field-security profile names for Restricted (or for the field).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PersonalInformationMaskingTests
{
    public sealed class Person
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = "";

        [PersonalInformation]
        public string? Email { get; init; }

        [ProtectedPersonalData]
        public string? NationalId { get; init; }
    }

    private sealed class User(params string[] permissions) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public string? UserName => "u";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => permissions;
    }

    private static AiResultProjector Projector(bool mask, bool withProfile, params string[] permissions)
    {
        var user = new User(permissions);
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        if (withProfile)
            services.AddFieldSecurity<Person>(FieldSecurityProfile.Define(p => p.Classification(FieldClassification.Restricted, read: "people:pii:read")));
        var provider = services.BuildServiceProvider();
        return new AiResultProjector(provider, user, Options.Create(new ModulusAiConnectorOptions { MaskPersonalInformation = mask }));
    }

    private static IReadOnlyList<string> Visible(AiResultProjector projector)
        => [.. AiFieldCatalog.For(typeof(Person)).Where(projector.CanRead).Select(f => f.Property!.Name).Order()];

    [Fact]
    public void By_default_personal_information_is_only_declared_not_masked()
        => Visible(Projector(mask: false, withProfile: true)).Should().Equal("Email", "Id", "Name", "NationalId");

    [Fact]
    public void With_masking_on_only_a_user_with_the_restricted_clearance_sees_personal_fields()
    {
        Visible(Projector(mask: true, withProfile: true, "people:pii:read")).Should().Equal("Email", "Id", "Name", "NationalId");
        Visible(Projector(mask: true, withProfile: true, "people:read")).Should().Equal("Id", "Name");
    }

    [Fact]
    public void With_masking_on_and_no_profile_personal_fields_are_withheld_from_everyone()
        => Visible(Projector(mask: true, withProfile: false, "people:pii:read")).Should().Equal("Id", "Name");

    [Fact]
    public void A_masked_field_does_not_appear_in_a_projected_record()
    {
        var projector = Projector(mask: true, withProfile: true, "people:read");
        var person = new Person { Id = Guid.NewGuid(), Name = "Ann", Email = "ann@example.test", NationalId = "123" };

        var result = projector.Project([person], AiFieldCatalog.For(typeof(Person)), null, max: 10);

        var json = JsonSerializer.Serialize(result);
        json.Should().Contain("Ann").And.NotContain("ann@example.test").And.NotContain("123");
    }
}
