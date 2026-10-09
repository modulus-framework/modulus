using FluentAssertions;
using Grpc.Core;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Grpc;
using Xunit;

namespace Modulus.Grpc.Tests;

[Trait("Category", "Unit")]
public sealed class GrpcExceptionMapperTests
{
    [Fact]
    public void A_cross_tenant_write_is_permission_denied_not_internal()
    {
        var rpc = GrpcExceptionMapper.ToRpcException(new CrossTenantWriteException("Product", Guid.NewGuid(), Guid.NewGuid()));

        rpc.StatusCode.Should().Be(StatusCode.PermissionDenied);
        rpc.GetErrorReason().Should().Be("PERMISSION_DENIED");
    }

    [Fact]
    public void Errors_carry_the_request_id_as_request_info()
    {
        var rpc = GrpcExceptionMapper.ToRpcException(new NotFoundException("x"), includeExceptionDetails: false, requestId: "corr-42");

        rpc.GetRpcStatus()!.GetDetail<Google.Rpc.RequestInfo>()!.RequestId.Should().Be("corr-42");
        rpc.GetErrorReason().Should().Be("NOT_FOUND"); // the other details are still there
    }

    [Fact]
    public void Without_a_request_id_no_request_info_is_added()
        => GrpcExceptionMapper.ToRpcException(new NotFoundException("x")).GetRpcStatus()!
            .GetDetail<Google.Rpc.RequestInfo>().Should().BeNull();

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("UnitPrice", "unit_price")]
    [InlineData("Address.PostalCode", "address.postal_code")]
    [InlineData("HTTPCode", "http_code")]
    [InlineData("", "")]
    public void Field_paths_use_proto_names(string property, string expected)
        => GrpcExceptionMapper.ToProtoFieldPath(property).Should().Be(expected);
}
