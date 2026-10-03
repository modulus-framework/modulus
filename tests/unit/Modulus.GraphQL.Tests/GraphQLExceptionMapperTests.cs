namespace Modulus.GraphQL.Tests;

using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GraphQLExceptionMapperTests
{
    [Fact]
    public void Client_errors_keep_their_details_and_server_errors_hide_them()
    {
        var validation = GraphQLExceptionMapper.Map(new ValidationException(["Name: required", "Price: must be positive"]));
        validation.Code.Should().Be("VALIDATION_FAILED");
        validation.IsClientError.Should().BeTrue();
        validation.Extensions["errors"].Should().BeEquivalentTo(new[] { "Name: required", "Price: must be positive" });

        var server = GraphQLExceptionMapper.Map(new InvalidOperationException("password=hunter2"));
        server.Should().BeEquivalentTo(new { Message = "An unexpected error occurred", Code = "INTERNAL", IsClientError = false });
        server.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void Cancellation_and_ef_concurrency_are_client_errors()
    {
        GraphQLExceptionMapper.Map(new OperationCanceledException()).Code.Should().Be("CANCELLED");
        var concurrency = GraphQLExceptionMapper.Map(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException());
        concurrency.Code.Should().Be("CONCURRENCY_CONFLICT");
        concurrency.IsClientError.Should().BeTrue();
    }
}
