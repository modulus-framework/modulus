using FluentAssertions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Export and bulk paths: rows are masked for the caller, and a batch write is all or nothing.</summary>
[Trait("Category", "Unit")]
public sealed class FieldBatchExtensionsTests
{
    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Restricted, read: "pay:read", write: "pay:write"));

    private sealed class Line
    {
        public string Label { get; set; } = string.Empty;

        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; set; }
    }

    private static FieldAuthorizer Authorizer(params string[] permissions)
        => new(new StubUser(Guid.NewGuid(), permissions), new StubRegistry(Profile));

    [Fact]
    public void An_export_masks_the_restricted_field_of_every_row_for_a_caller_who_cannot_read_it()
    {
        var rows = new[] { new Line { Label = "a", Cost = 10m }, new Line { Label = "b", Cost = 20m } };

        var exported = Authorizer().RedactForExport(rows);

        exported.Select(r => r.Label).Should().Equal("a", "b");
        exported.Select(r => r.Cost).Should().OnlyContain(c => c == 0m);
    }

    [Fact]
    public void An_export_keeps_the_restricted_field_for_a_caller_who_can_read_it()
    {
        var rows = new[] { new Line { Label = "a", Cost = 10m } };

        var exported = Authorizer("pay:read").RedactForExport(rows);

        exported.Single().Cost.Should().Be(10m);
    }

    [Fact]
    public async Task A_bulk_write_that_sets_a_field_the_caller_may_not_write_is_refused_as_a_whole()
    {
        var rows = new[] { new[] { "Label" }, new[] { "Label", "Cost" } };

        var act = () => Authorizer().EnsureBatchWriteAllowedAsync(typeof(Line), rows);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_bulk_write_of_public_fields_passes()
    {
        var rows = new[] { new[] { "Label" }, new[] { "Label" } };

        var act = () => Authorizer().EnsureBatchWriteAllowedAsync(typeof(Line), rows);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_bulk_write_with_the_write_permission_passes_for_the_restricted_field()
    {
        var rows = new[] { new[] { "Label", "Cost" } };

        var act = () => Authorizer("pay:write").EnsureBatchWriteAllowedAsync(typeof(Line), rows);

        await act.Should().NotThrowAsync();
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
