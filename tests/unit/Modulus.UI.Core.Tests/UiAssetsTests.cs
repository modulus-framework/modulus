using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for UiAssets.ContentBase: the layout's asset base URL must adapt to
/// the static-web-assets segment the consuming app actually registered —
/// package id (PackageReference: Cobytelabs.Modulus.UI.Core) vs assembly
/// name (ProjectReference: Modulus.UI.Core) — with the assembly-name path
/// as the fallback when nothing is registered.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiAssetsTests
{
    [Fact]
    public void Resolves_package_id_segment_when_present()
    {
        var provider = new TestWebRootProvider(
            "Cobytelabs.Modulus.UI.Core",
            Dir("Cobytelabs.Modulus.UI.Core"),
            Dir("Cobytelabs.Modulus.UI.Identity"));

        UiAssets.ContentBase(provider).Should().Be("/_content/Cobytelabs.Modulus.UI.Core");
    }

    [Fact]
    public void Resolves_assembly_name_segment_for_project_references()
    {
        var provider = new TestWebRootProvider("Modulus.UI.Core", Dir("Modulus.UI.Core"));

        UiAssets.ContentBase(provider).Should().Be("/_content/Modulus.UI.Core");
    }

    [Fact]
    public void Ignores_content_dirs_without_the_asset_marker()
    {
        var provider = new TestWebRootProvider(null, Dir("Some.Other.Package"));

        UiAssets.ContentBase(provider).Should().Be("/_content/Modulus.UI.Core");
    }

    [Fact]
    public void Falls_back_to_assembly_path_when_no_content_registered()
    {
        var provider = new TestWebRootProvider(null);

        UiAssets.ContentBase(provider).Should().Be("/_content/Modulus.UI.Core");
    }

    [Fact]
    public void Result_is_cached_per_file_provider()
    {
        var provider = new TestWebRootProvider(
            "Cobytelabs.Modulus.UI.Core", Dir("Cobytelabs.Modulus.UI.Core"));

        var first = UiAssets.ContentBase(provider);
        var second = UiAssets.ContentBase(provider);

        second.Should().Be(first);
    }

    [Fact]
    public void Throws_for_null_provider()
    {
        var act = () => UiAssets.ContentBase((IFileProvider)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static IFileInfo Dir(string name) => new TestDirectoryInfo(name);

    private sealed class TestWebRootProvider : IFileProvider
    {
        private readonly IFileInfo[] _entries;
        private readonly string? _markerSegment;

        public TestWebRootProvider(string? markerSegment, params IFileInfo[] entries)
        {
            _markerSegment = markerSegment;
            _entries = entries;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            if (subpath != "/_content")
            {
                return NotFoundDirectoryContents.Singleton;
            }

            return new TestDirectoryContents(_entries);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var marker = $"/_content/{_markerSegment}/modulus-ui/modulus-ui.js";
            return subpath == marker
                ? new TestFileInfo()
                : new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class TestDirectoryContents : IDirectoryContents
    {
        private readonly IFileInfo[] _entries;

        public TestDirectoryContents(IFileInfo[] entries) => _entries = entries;

        public bool Exists => true;

        public IEnumerator<IFileInfo> GetEnumerator() => _entries.AsEnumerable().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _entries.GetEnumerator();
    }

    private sealed class TestDirectoryInfo : IFileInfo
    {
        public TestDirectoryInfo(string name) => Name = name;

        public bool Exists => true;
        public long Length => 0;
        public string? PhysicalPath => null;
        public DateTimeOffset LastModified => DateTimeOffset.MinValue;
        public bool IsDirectory => true;
        public string Name { get; }

        public Stream CreateReadStream() => throw new InvalidOperationException("Directories cannot be streamed.");
    }

    private sealed class TestFileInfo : IFileInfo
    {
        public bool Exists => true;
        public long Length => 1;
        public string? PhysicalPath => null;
        public DateTimeOffset LastModified => DateTimeOffset.MinValue;
        public bool IsDirectory => false;
        public string Name => "modulus-ui.js";

        public Stream CreateReadStream() => new MemoryStream([1]);
    }
}
