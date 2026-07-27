#if NET10_0_OR_GREATER
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Modulus.AspNetCore.OpenApi;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class OpenApiDocumentTransformerTests
{
    // The document transformer does not touch its context, so a null context is
    // safe here and keeps the test free of OpenAPI pipeline plumbing.
    private static async Task<OpenApiDocument> TransformAsync(ModulusOpenApiOptions options)
    {
        var transformer = new ModulusOpenApiDocumentTransformer(Options.Create(options));
        var document = new OpenApiDocument();
        await transformer.TransformAsync(document, null!, default);
        return document;
    }

    [Fact]
    public async Task Sets_Info_FromOptions()
    {
        var doc = await TransformAsync(new ModulusOpenApiOptions
        {
            Title = "Orders API",
            Version = "v2",
            Description = "Handles orders.",
        });

        doc.Info!.Title.Should().Be("Orders API");
        doc.Info.Version.Should().Be("v2");
        doc.Info.Description.Should().Be("Handles orders.");
    }

    [Fact]
    public async Task AddsBearerScheme_WhenEnabled()
    {
        var doc = await TransformAsync(new ModulusOpenApiOptions { IncludeBearerSecurity = true });

        doc.Components!.SecuritySchemes.Should().ContainKey("Bearer");
        var scheme = doc.Components.SecuritySchemes["Bearer"];
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    [Fact]
    public async Task OmitsBearerScheme_WhenDisabled()
    {
        var doc = await TransformAsync(new ModulusOpenApiOptions { IncludeBearerSecurity = false });

        (doc.Components?.SecuritySchemes).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Sets_Contact_And_License_WhenProvided()
    {
        var doc = await TransformAsync(new ModulusOpenApiOptions
        {
            ContactName = "Platform",
            ContactEmail = "team@example.com",
            ContactUrl = "https://example.com",
            LicenseName = "MIT",
            LicenseUrl = "https://opensource.org/licenses/MIT",
        });

        doc.Info!.Contact!.Name.Should().Be("Platform");
        doc.Info.Contact.Email.Should().Be("team@example.com");
        doc.Info.Contact.Url.Should().Be(new Uri("https://example.com"));
        doc.Info.License!.Name.Should().Be("MIT");
    }
}
#endif
