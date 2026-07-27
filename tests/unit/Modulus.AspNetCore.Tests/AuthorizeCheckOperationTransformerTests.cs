#if NET10_0_OR_GREATER
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Modulus.AspNetCore.OpenApi;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class AuthorizeCheckOperationTransformerTests
{
    private static async Task<OpenApiOperation> TransformAsync(params object[] endpointMetadata)
    {
        var description = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata },
        };
        var context = new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = description,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };

        var operation = new OpenApiOperation();
        await new AuthorizeCheckOperationTransformer().TransformAsync(operation, context, default);
        return operation;
    }

    [Fact]
    public async Task AddsSecurityRequirement_WhenAuthorizeMetadataPresent()
    {
        var operation = await TransformAsync(new AuthorizeAttribute());

        operation.Security.Should().ContainSingle();
    }

    [Fact]
    public async Task NoRequirement_WhenAnonymousAlsoPresent()
    {
        var operation = await TransformAsync(new AuthorizeAttribute(), new AllowAnonymousAttribute());

        (operation.Security ?? []).Should().BeEmpty();
    }

    [Fact]
    public async Task NoRequirement_WhenNoAuthorizeMetadata()
    {
        var operation = await TransformAsync();

        (operation.Security ?? []).Should().BeEmpty();
    }
}
#endif
