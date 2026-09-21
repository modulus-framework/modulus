using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>The API side of extension fields: same registry and permission filter as the form, different edge cases.</summary>
[Trait("Category", "Unit")]
public sealed class EntityApiFieldsTests
{
    private const string Entity = "Catalog.Product";

    private static IEntityUiRegistry Registry()
    {
        var services = new ServiceCollection();
        services.ConfigureEntityUi(Entity, e =>
        {
            e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", order: 20, validators: [new RangeAttribute(0, 1000)]));
            e.Fields.Add(new EntityField("Bin", typeof(string), "Bin", order: 10, requiredPermission: "inventory.manage"));
            e.Fields.Add(new EntityField("Supplier", typeof(string), "Supplier", order: 30, validators: [new RequiredAttribute()]));
        });
        services.AddModulusUi();
        return services.BuildServiceProvider().GetRequiredService<IEntityUiRegistry>();
    }

    private static ICurrentUser User(params string[] permissions)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));
        return user;
    }

    private static Dictionary<string, string?> Bag(params (string Name, string? Value)[] items)
        => items.ToDictionary(i => i.Name, i => i.Value);

    // ── Reading ──────────────────────────────────────────────────

    [Fact]
    public void A_caller_sees_only_the_stored_values_of_fields_they_may_see()
    {
        var stored = Bag(("ReorderLevel", "25.5"), ("Bin", "A-7"), ("Supplier", "Acme"), ("Removed", "old"));

        var plain = Registry().VisibleExtraProperties(Entity, User(), stored);
        var manager = Registry().VisibleExtraProperties(Entity, User("inventory.manage"), stored);

        plain.Should().BeEquivalentTo(new Dictionary<string, string?> { ["ReorderLevel"] = "25.5", ["Supplier"] = "Acme" });
        manager.Should().ContainKey("Bin").WhoseValue.Should().Be("A-7");
        manager.Should().NotContainKey("Removed", "a value stored for a field that is no longer contributed is not exposed");
    }

    [Fact]
    public void A_missing_bag_reads_as_empty()
        => Registry().VisibleExtraProperties(Entity, User(), null).Should().BeEmpty();

    // ── Validating ───────────────────────────────────────────────

    [Fact]
    public void A_create_checks_every_visible_field_so_a_missing_required_one_is_reported()
    {
        var errors = Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("ReorderLevel", "10")), partial: false);

        errors.Should().ContainSingle().Which.Should().StartWith("Supplier:");
    }

    [Fact]
    public void An_update_only_checks_the_fields_that_were_sent()
    {
        var errors = Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("ReorderLevel", "10")), partial: true);

        errors.Should().BeEmpty("Supplier is required but was not sent, so it is left as it is");
    }

    [Fact]
    public void A_value_outside_the_rules_is_reported_by_field_name()
    {
        var errors = Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("ReorderLevel", "5000"), ("Supplier", "Acme")), partial: false);

        errors.Should().ContainSingle().Which.Should().StartWith("ReorderLevel:");
        Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("ReorderLevel", "many")), partial: true)
            .Should().ContainSingle().Which.Should().Contain("ReorderLevel").And.Contain("not valid");
    }

    [Fact]
    public void A_field_the_caller_may_not_see_reads_exactly_like_one_that_does_not_exist()
    {
        var hidden = Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("Bin", "A-7"), ("Supplier", "Acme")), partial: false);
        var missing = Registry().ValidateEntityFieldsForApi(Entity, User(), Bag(("Nope", "A-7"), ("Supplier", "Acme")), partial: false);

        hidden.Should().ContainSingle().Which.Should().Be("Unknown extension field 'Bin'.");
        missing.Should().ContainSingle().Which.Should().Be("Unknown extension field 'Nope'.");
    }

    [Fact]
    public void A_permitted_caller_may_set_the_gated_field_and_names_match_in_any_case()
    {
        var errors = Registry().ValidateEntityFieldsForApi(Entity, User("inventory.manage"), Bag(("bin", "A-7"), ("SUPPLIER", "Acme")), partial: false);

        errors.Should().BeEmpty();
    }

    // ── Reading what to store ────────────────────────────────────

    [Fact]
    public void Only_the_fields_that_were_sent_are_stored_and_an_empty_one_clears_its_key()
    {
        var text = Registry().ReadSubmittedEntityFieldText(Entity, User("inventory.manage"),
            Bag(("ReorderLevel", "30"), ("Bin", "")));

        text.Should().BeEquivalentTo(new Dictionary<string, string?> { ["ReorderLevel"] = "30", ["Bin"] = null });
        text.Should().NotContainKey("Supplier", "not sent, so an update leaves the stored value alone");
    }

    [Fact]
    public void A_field_the_caller_may_not_see_is_never_stored_even_if_it_is_sent()
    {
        var text = Registry().ReadSubmittedEntityFieldText(Entity, User(), Bag(("Bin", "A-7"), ("ReorderLevel", "30")));

        text.Should().NotContainKey("Bin");
        text.Should().ContainKey("ReorderLevel");
    }

    [Fact]
    public void Values_are_stored_as_canonical_invariant_text()
    {
        Registry().ReadSubmittedEntityFieldText(Entity, User(), Bag(("ReorderLevel", "0025.5")))
            .Should().ContainKey("ReorderLevel").WhoseValue.Should().Be("25.5");
    }
}
