using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Storage;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>An upload larger than the app's limit is refused before the storage backend receives it.</summary>
[Trait("Category", "Unit")]
public sealed class SizeLimitedFileStorageTests
{
    [Fact]
    public async Task A_declared_length_over_the_limit_is_refused_before_anything_is_written()
    {
        var inner = Substitute.For<IFileStorage>();
        var storage = new SizeLimitedFileStorage(inner, maxBytes: 10);

        Func<Task> act = () => storage.UploadAsync("big.bin", new MemoryStream(new byte[11]));

        await act.Should().ThrowAsync<ValidationException>();
        await inner.DidNotReceiveWithAnyArgs().UploadAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task A_stream_of_unknown_length_is_refused_once_it_passes_the_limit()
    {
        var inner = Substitute.For<IFileStorage>();
        inner.UploadAsync(default!, default!, default, default).ReturnsForAnyArgs(async callInfo =>
        {
            // Drains the stream the way a backend would, so the cap is reached.
            var content = callInfo.ArgAt<Stream>(1);
            var buffer = new byte[64];
            while (await content.ReadAsync(buffer, 0, buffer.Length) > 0)
            {
            }
        });
        var storage = new SizeLimitedFileStorage(inner, maxBytes: 10);

        Func<Task> act = () => storage.UploadAsync("stream.bin", new NonSeekable(new MemoryStream(new byte[25])));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_file_within_the_limit_is_passed_through()
    {
        var inner = Substitute.For<IFileStorage>();
        var storage = new SizeLimitedFileStorage(inner, maxBytes: 10);

        await storage.UploadAsync("small.bin", new MemoryStream(new byte[10]), "application/octet-stream");

        await inner.Received(1).UploadAsync("small.bin", Arg.Any<Stream>(), "application/octet-stream", Arg.Any<CancellationToken>());
    }

    /// <summary>Hides the length and the position, as a network stream does.</summary>
    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
