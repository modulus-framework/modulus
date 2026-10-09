using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class ModulusErrorCatalogTests
{
    [Theory]
    [InlineData(typeof(NotFoundException), ModulusErrorKind.NotFound)]
    [InlineData(typeof(ConflictException), ModulusErrorKind.Conflict)]
    [InlineData(typeof(UnauthorizedException), ModulusErrorKind.Unauthenticated)]
    [InlineData(typeof(OperationCanceledException), ModulusErrorKind.Cancelled)]
    [InlineData(typeof(InvalidOperationException), ModulusErrorKind.Internal)]
    public void Classifies_by_type(Type type, ModulusErrorKind kind)
    {
        var exception = type == typeof(NotFoundException) || type == typeof(ConflictException)
            ? (Exception)Activator.CreateInstance(type, "x")!
            : (Exception)Activator.CreateInstance(type)!;

        ModulusErrorCatalog.Classify(exception).Kind.Should().Be(kind);
    }

    [Fact]
    public void A_cross_tenant_write_is_a_client_side_permission_failure()
    {
        var error = ModulusErrorCatalog.Classify(new CrossTenantWriteException("Product", Guid.NewGuid(), null));

        error.Kind.Should().Be(ModulusErrorKind.PermissionDenied);
        error.IsClientError.Should().BeTrue();
    }

    [Fact]
    public void Field_errors_group_by_field_and_keep_unprefixed_messages_under_the_empty_key()
    {
        var exception = new ValidationException(["Name: required", "Name: too short", "price is too low"]);

        exception.FieldErrors["Name"].Should().Equal("required", "too short");
        exception.FieldErrors[""].Should().Equal("price is too low");
        exception.Errors.Should().HaveCount(3);
    }
}
