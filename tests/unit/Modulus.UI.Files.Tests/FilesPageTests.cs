using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Modulus.Storage;
using Modulus.UI.Files.Pages.Files;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Modulus.UI.Files.Tests;

/// <summary>
/// Spec for the path-addressed file manager: existence lookup, guarded
/// upload (path + size + traversal), download streaming, and delete.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FilesPageTests
{
    private static IndexModel Build(
        IFileStorage storage,
        FilesUiOptions? options = null)
        => new(
            storage,
            Options.Create(options ?? new FilesUiOptions()),
            new TestLocalizer());

    private static FormFile UploadFile(string name, string text, string contentType = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public async Task Lookup_ExistingPath_ReportsExists()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync("docs/a.txt", Arg.Any<CancellationToken>()).Returns(true);
        var model = Build(storage);
        model.Path = "docs/a.txt";

        await model.OnGetAsync(default);

        model.Exists.Should().BeTrue();
    }

    [Fact]
    public async Task Lookup_MissingPath_ReportsMissing()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var model = Build(storage);
        model.Path = "docs/nope.txt";

        await model.OnGetAsync(default);

        model.Exists.Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_TraversalPath_RendersError()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ArgumentException("escapes root"));
        var model = Build(storage);
        model.Path = "../../secret";

        await model.OnGetAsync(default);

        model.ModelState.IsValid.Should().BeFalse();
        model.Exists.Should().BeNull();
    }

    [Fact]
    public async Task Upload_ValidFile_StreamsToStore_Redirects()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage);
        model.Path = "docs/a.txt";
        var file = UploadFile("a.txt", "hello");

        var result = await model.OnPostUploadAsync(file, default);

        result.Should().BeOfType<RedirectToPageResult>();
        await storage.Received(1).UploadAsync(
            "docs/a.txt",
            Arg.Is<Stream>(s => s.CanRead && s.Length == 5),
            "text/plain",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_WithoutFile_RendersError_WithoutStoring()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage);
        model.Path = "docs/a.txt";

        var result = await model.OnPostUploadAsync(null, default);

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await storage.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task Upload_OversizedFile_RendersError_WithoutStoring()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage, new FilesUiOptions { MaxUploadBytes = 4 });
        model.Path = "docs/a.txt";
        var file = UploadFile("a.txt", "hello");

        var result = await model.OnPostUploadAsync(file, default);

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await storage.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task Download_ExistingFile_StreamsOctetStream()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.DownloadAsync("docs/a.txt", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("hello")));
        var model = Build(storage);

        var result = await model.OnGetDownloadAsync("docs/a.txt", default);

        var file = result.Should().BeOfType<FileStreamResult>().Subject;
        file.ContentType.Should().Be("application/octet-stream");
        file.FileDownloadName.Should().Be("a.txt");
    }

    [Fact]
    public async Task Download_MissingFile_ReturnsNotFound()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FileNotFoundException());
        var model = Build(storage);

        (await model.OnGetDownloadAsync("docs/nope.txt", default))
            .Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_DelegatesToStore_Redirects()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage);

        var result = await model.OnPostDeleteAsync("docs/a.txt", default);

        result.Should().BeOfType<RedirectToPageResult>();
        await storage.Received(1).DeleteAsync("docs/a.txt", Arg.Any<CancellationToken>());
    }

    private static void AsHtmx(IndexModel model)
    {
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new PageActionDescriptor()));
        model.Request.Headers["HX-Request"] = "true";
    }

    private static string HxTrigger(IndexModel model)
        => model.Response.Headers["HX-Trigger"].ToString();

    [Fact]
    public async Task Upload_Htmx_Valid_ReturnsResultCard_WithToast()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync("docs/a.txt", Arg.Any<CancellationToken>()).Returns(true);
        var model = Build(storage);
        model.Path = "docs/a.txt";
        AsHtmx(model);
        var file = UploadFile("a.txt", "hello");

        var result = await model.OnPostUploadAsync(file, default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_FileResult");
        partial.Model.Should().BeOfType<FileResultView>()
            .Which.Should().Be(new FileResultView("docs/a.txt", true));
        HxTrigger(model).Should().Contain("modulusToast");
    }

    [Fact]
    public async Task Upload_Htmx_WithoutFile_ReturnsCard_WithErrors()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage);
        model.Path = "docs/a.txt";
        AsHtmx(model);

        var result = await model.OnPostUploadAsync(null, default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_FileResult");
        partial.ViewData!.ModelState.IsValid.Should().BeFalse();
        model.ModelState.IsValid.Should().BeFalse();
        await storage.DidNotReceiveWithAnyArgs().UploadAsync(
            default!, default!, default!, default);
    }

    [Fact]
    public async Task Delete_Htmx_ReturnsMissingCard_WithToast()
    {
        var storage = Substitute.For<IFileStorage>();
        var model = Build(storage);
        AsHtmx(model);

        var result = await model.OnPostDeleteAsync("docs/a.txt", default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_FileResult");
        partial.Model.Should().Be(new FileResultView("docs/a.txt", false));
        HxTrigger(model).Should().Contain("modulusToast");
        await storage.Received(1).DeleteAsync("docs/a.txt", Arg.Any<CancellationToken>());
    }
}
