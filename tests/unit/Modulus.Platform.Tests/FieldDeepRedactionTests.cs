using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Field security reaches below the top-level DTO: a classified field inside a nested object or a list element is masked, a cycle
/// does not loop, and a write above the caller's clearance is refused with <c>FIELD_PROTECTED</c> (throwing or as a decision).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldDeepRedactionTests
{
    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Restricted, read: "pay:read", write: "pay:write"));

    private sealed class Line
    {
        public string Label { get; set; } = string.Empty;

        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; set; }
    }

    private sealed class Order
    {
        public string Reference { get; set; } = string.Empty;

        public Line? Primary { get; set; }

        public List<Line> Lines { get; set; } = [];

        public Order? Parent { get; set; }
    }

    private sealed class Bare
    {
        public string Name { get; set; } = string.Empty;
    }

    private static FieldAuthorizer Authorizer(params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(Profile));

    [Fact]
    public void A_classified_field_inside_a_nested_object_is_masked()
    {
        var order = new Order { Reference = "PO-1", Primary = new Line { Label = "main", Cost = 900m } };

        Authorizer().Redact(order);

        order.Reference.Should().Be("PO-1");
        order.Primary!.Label.Should().Be("main");
        order.Primary.Cost.Should().Be(0m);
    }

    [Fact]
    public void A_classified_field_inside_a_list_element_is_masked_and_a_cleared_caller_keeps_it()
    {
        var order = new Order { Lines = [new Line { Label = "a", Cost = 10m }, new Line { Label = "b", Cost = 20m }] };

        Authorizer().Redact(order);
        order.Lines.Select(l => l.Cost).Should().OnlyContain(c => c == 0m);
        order.Lines.Select(l => l.Label).Should().Equal("a", "b");

        var cleared = new Order { Lines = [new Line { Cost = 10m }] };
        Authorizer("pay:read").Redact(cleared);
        cleared.Lines[0].Cost.Should().Be(10m);
    }

    [Fact]
    public void A_cycle_in_the_object_graph_is_walked_once_and_terminates()
    {
        var parent = new Order { Reference = "P", Primary = new Line { Cost = 5m } };
        var child = new Order { Reference = "C", Primary = new Line { Cost = 7m }, Parent = parent };
        parent.Parent = child;

        var act = () => Authorizer().Redact(child);

        act.Should().NotThrow();
        child.Primary!.Cost.Should().Be(0m);
        parent.Primary!.Cost.Should().Be(0m);
    }

    [Fact]
    public async Task A_write_to_a_protected_field_is_refused_with_FIELD_PROTECTED()
    {
        var decision = await Authorizer().AuthorizeWriteAsync(typeof(Line), ["Cost"]);

        decision.IsAllowed.Should().BeFalse();
        decision.Code.Should().Be(AccessReasonCodes.FieldProtected);
    }

    [Fact]
    public async Task The_throwing_write_guard_raises_forbidden_naming_the_fields_and_no_values()
    {
        var authorizer = Authorizer();

        var act = () => authorizer.EnsureWriteAllowedAsync(typeof(Line), ["Label", "Cost"]);

        var error = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        error.Message.Should().Contain(AccessReasonCodes.FieldProtected).And.Contain("Cost");
        error.Message.Should().NotContain("900");
    }

    [Fact]
    public async Task An_unclassified_field_on_a_plain_type_is_writable_by_any_caller()
    {
        // The generated Create and Update handlers set Name and ExtraProperties on a plain entity: that must keep working.
        await Authorizer().EnsureWriteAllowedAsync(typeof(Bare), ["Name"]);
    }

    [Fact]
    public async Task A_field_the_type_does_not_have_is_refused()
    {
        var decision = await Authorizer("pay:write").AuthorizeWriteAsync(typeof(Line), ["Nope"]);

        decision.IsAllowed.Should().BeFalse();
    }

    private sealed class StubRegistry(FieldSecurityProfile profile) : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType) => profile;
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
