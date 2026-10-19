using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The output safety net: a response is checked after redaction, and a redacted instance cannot be saved. Errors name fields, never values.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldOutputGuardTests
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

        public List<Line> Lines { get; set; } = [];
    }

    private static FieldAuthorizer Authorizer(params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(Profile));

    [Fact]
    public void A_response_that_was_redacted_passes_the_check()
    {
        var authorizer = Authorizer();
        var order = new Order { Reference = "PO-1", Lines = [new Line { Label = "a", Cost = 10m }] };
        authorizer.Redact(order);

        var act = () => FieldOutputGuard.EnsureMasked(authorizer, order);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_value_put_back_in_a_masked_nested_field_after_redaction_is_refused_naming_the_field_and_not_the_value()
    {
        var authorizer = Authorizer();
        var order = new Order { Lines = [new Line { Label = "a", Cost = 10m }] };
        authorizer.Redact(order);
        order.Lines[0].Cost = 900m;

        var act = () => FieldOutputGuard.EnsureMasked(authorizer, order);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Line.Cost*").WithMessage("*not read*");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Line.Cost*").Which.Message.Should().NotContain("900");
    }

    [Fact]
    public void A_caller_who_can_read_the_field_gets_the_value_and_the_check_passes()
    {
        var order = new Order { Lines = [new Line { Label = "a", Cost = 10m }] };
        var authorizer = Authorizer("pay:read");
        authorizer.Redact(order);

        var act = () => FieldOutputGuard.EnsureMasked(authorizer, order);

        act.Should().NotThrow();
        order.Lines[0].Cost.Should().Be(10m);
    }

    [Fact]
    public void An_instance_redaction_reset_cannot_be_saved()
    {
        var order = new Order { Lines = [new Line { Label = "a", Cost = 10m }] };
        Authorizer().Redact(order);

        var act = () => FieldOutputGuard.EnsureNotRedacted(order.Lines[0]);

        var error = act.Should().Throw<InvalidOperationException>().WithMessage("*Line*Cost*").Which;
        error.Message.Should().NotContain("10");
    }

    [Fact]
    public void A_redacted_instance_with_nothing_to_reset_can_be_saved()
    {
        var order = new Order { Lines = [new Line { Label = "a", Cost = 0m }] };
        Authorizer().Redact(order);

        var act = () => FieldOutputGuard.EnsureNotRedacted(order.Lines[0]);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_instance_that_was_never_redacted_can_be_saved()
    {
        var line = new Line { Label = "a", Cost = 10m };

        var act = () => FieldOutputGuard.EnsureNotRedacted(line);

        act.Should().NotThrow();
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
