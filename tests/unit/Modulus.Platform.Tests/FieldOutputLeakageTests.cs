using System.Text.Json;
using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A7-4: a restricted value must not leave the server through an alternate output: a serialized response, an export row
/// with a nested object, or an object graph with empty and null parts.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FieldOutputLeakageTests
{
    private const decimal Secret = 987654.25m;

    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Restricted, read: "pay:read", write: "pay:write"));

    private sealed class Pricing
    {
        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; set; }
    }

    private sealed class Line
    {
        public string Label { get; set; } = string.Empty;

        public Pricing? Pricing { get; set; }
    }

    private sealed class Order
    {
        public string Reference { get; set; } = string.Empty;

        public List<Line> Lines { get; set; } = [];
    }

    private static FieldAuthorizer Authorizer(params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(Profile));

    private static Order OrderWithSecret() => new()
    {
        Reference = "PO-7",
        Lines = [new Line { Label = "fabric", Pricing = new Pricing { Cost = Secret } }],
    };

    [Fact]
    public void A_serialized_response_does_not_carry_a_restricted_value_the_caller_cannot_read()
    {
        var authorizer = Authorizer();
        var order = OrderWithSecret();
        authorizer.Redact(order);

        var json = JsonSerializer.Serialize(order);

        json.Should().Contain("PO-7");
        json.Should().NotContain("987654");
    }

    [Fact]
    public void An_export_row_hides_the_nested_restricted_value_for_a_caller_who_cannot_read_it()
    {
        var authorizer = Authorizer();

        var rows = authorizer.RedactForExport([OrderWithSecret(), OrderWithSecret()]);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(o => o.Lines[0].Pricing!.Cost == 0m);
        var json = JsonSerializer.Serialize(rows);
        json.Should().NotContain("987654");
    }

    [Fact]
    public void An_export_row_keeps_the_nested_value_for_a_caller_who_can_read_it()
    {
        var authorizer = Authorizer("pay:read");

        var rows = authorizer.RedactForExport([OrderWithSecret()]);

        rows[0].Lines[0].Pricing!.Cost.Should().Be(Secret);
    }

    [Fact]
    public void Null_and_empty_nested_parts_are_passed_through_without_error()
    {
        var authorizer = Authorizer();
        var order = new Order
        {
            Reference = "PO-8",
            Lines = [new Line { Label = "no pricing yet", Pricing = null }],
        };

        var act = () =>
        {
            authorizer.Redact(order);
            FieldOutputGuard.EnsureMasked(authorizer, order);
        };

        act.Should().NotThrow();
        order.Lines[0].Pricing.Should().BeNull();
    }

    [Fact]
    public void An_empty_list_of_lines_is_a_valid_masked_response()
    {
        var authorizer = Authorizer();
        var order = new Order { Reference = "PO-9", Lines = [] };

        var act = () =>
        {
            authorizer.Redact(order);
            FieldOutputGuard.EnsureMasked(authorizer, order);
        };

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
