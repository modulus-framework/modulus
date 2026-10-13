namespace Modulus.Cli.Tests;

using System.IO.Compression;
using System.Net;
using Modulus.Cli.Services;

/// <summary>
/// A small Modulus.Ui.Templates package written to a temp cache, so resolver tests run offline and do not depend on the
/// machine's ~/.modulus/cache or on what nuget.org serves.
/// </summary>
internal sealed class TemplatePackageFixture : IDisposable
{
    public const string PackageId = "Synvro.Modulus.Ui.Templates";
    public const string Version = "0.9.0";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"modulus-templates-{Guid.NewGuid():N}");

    public TemplatePackageFixture()
    {
        CacheDir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(CacheDir);
        NupkgPath = Path.Combine(CacheDir, $"{PackageId}.{Version}.nupkg");
        WritePackage();
    }

    public string CacheDir { get; }

    /// <summary>The downloaded package that the resolver extracts from. Deleting it proves a later call did not download.</summary>
    public string NupkgPath { get; }

    /// <summary>A resolver whose cache is the fixture and whose network answers 404, so nothing reaches a real feed.</summary>
    public TemplatePackageResolver CreateResolver() =>
        new(CacheDir, new HttpClient(new StatusHandler(HttpStatusCode.NotFound)), ["https://feed.invalid/v3/index.json"]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WritePackage()
    {
        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(staging);

        File.WriteAllText(Path.Combine(staging, "ui-kit.json"), """
            {
              "version": "0.9.0",
              "engines": ["mvc", "razor-pages", "blazor"],
              "features": {
                "crud": {
                  "files": {
                    "mvc": ["Views/[Entities]/Index.cshtml"],
                    "razor-pages": [
                      { "path": "Pages/[Entities]/Index.cshtml", "template": "crud-index.cshtml.sbn" },
                      "Pages/[Entities]/Index.cshtml.cs"
                    ],
                    "blazor": ["Components/Pages/[Entities]/Index.razor"]
                  }
                }
              }
            }
            """);

        // The package identity: the resolver checks that a download is the package it asked for.
        File.WriteAllText(Path.Combine(staging, PackageId + ".nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{PackageId}</id>
                <version>{Version}</version>
              </metadata>
            </package>
            """);

        WriteTemplate(staging, "mvc", "crud-index.cshtml.sbn");
        WriteTemplate(staging, "razor-pages", "crud-index.cshtml.sbn");
        WriteTemplate(staging, "blazor", "crud-index.razor.sbn");

        ZipFile.CreateFromDirectory(staging, NupkgPath);
    }

    private static void WriteTemplate(string staging, string engine, string fileName)
    {
        var dir = Path.Combine(staging, "templates", engine);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), "<h1>{{ Entity }}</h1>");
    }

    /// <summary>The package bytes, as a feed serves them.</summary>
    public byte[] NupkgBytes => File.ReadAllBytes(NupkgPath);

    /// <summary>
    /// A resolver that downloads from one feed, served in memory: a v3 service index naming a flat container, which
    /// serves <paramref name="package"/> at the flat-container address. Every URL requested is recorded.
    /// </summary>
    public (TemplatePackageResolver Resolver, List<string> Requested) CreateFeedResolver(string cacheDir, byte[]? package = null)
    {
        var requested = new List<string>();
        var handler = new FeedHandler(requested, package ?? NupkgBytes);
        var resolver = new TemplatePackageResolver(cacheDir, new HttpClient(handler), [FeedIndex]);
        return (resolver, requested);
    }

    public const string FeedIndex = "https://feed.test/v3/index.json";

    private sealed class FeedHandler(List<string> requested, byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            requested.Add(url);
            if (url == FeedIndex)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"version":"3.0.0","resources":[{"@id":"https://feed.test/flat/","@type":"PackageBaseAddress/3.0.0"}]}"""),
                });
            }

            if (url == $"https://feed.test/flat/{PackageId.ToLowerInvariant()}/{Version}/{PackageId.ToLowerInvariant()}.{Version}.nupkg")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
