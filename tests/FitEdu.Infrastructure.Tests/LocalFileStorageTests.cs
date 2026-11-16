using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Infrastructure.ApiServices;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fitedu-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(_root);
        var config = new ConfigurationBuilder().Build();
        _storage = new LocalFileStorage(env, config, NullLogger<LocalFileStorage>.Instance);
    }

    private static MemoryStream Stream(int bytes = 16) => new(new byte[bytes]);

    // P05-2 / P05-7: a stored file is named by a fresh GUID plus the extension of its validated content type;
    // nothing the client called the file reaches the path (or the log line).

    [Fact]
    public async Task SaveFile_NamesTheFileByAGuidAndReturnsTheRelativePath()
    {
        var path = await _storage.SaveFileAsync(Stream(), "image/png", "avatars");

        path.ShouldMatch("^avatars/[0-9a-f]{32}\\.png$");
        _storage.FileExists(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/jpg", ".jpg")]
    [InlineData("IMAGE/PNG", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/webm", ".webm")]
    [InlineData("video/ogg", ".ogv")]
    [InlineData("image/png; charset=binary", ".png")]
    public async Task SaveFile_TakesTheExtensionFromTheContentType(string contentType, string expectedExtension)
    {
        var path = await _storage.SaveFileAsync(Stream(), contentType, "avatars", maxBytes: 1024);

        path.ShouldEndWith(expectedExtension);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("image/avif")]
    [InlineData("application/octet-stream")]
    [InlineData("   ")]
    public async Task SaveFile_ContentTypeOutsideTheUploadSets_Throws(string contentType)
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _storage.SaveFileAsync(Stream(), contentType, "avatars"));
    }

    [Fact]
    public async Task SaveFile_AvatarsAreCappedAtTheImageLimit_CoversAtTheLargerCoverLimit()
    {
        const int sixMegabytes = 6 * 1024 * 1024;

        await Should.ThrowAsync<InvalidOperationException>(
            () => _storage.SaveFileAsync(Stream(sixMegabytes), "image/png", "avatars"));

        var coverPath = await _storage.SaveFileAsync(Stream(sixMegabytes), "image/png", "blog/covers");
        _storage.FileExists(coverPath).ShouldBeTrue();
    }

    [Fact]
    public async Task GetFile_ServesAnOldGreekNamedFileWithItsContentType()
    {
        // Files stored before the GUID names keep the client's name; they still have to be served.
        var oldPath = "blog/covers/φωτογραφία_0123456789abcdef.JPG";
        Directory.CreateDirectory(Path.Combine(_root, "App_Data", "Uploads", "blog", "covers"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "App_Data", "Uploads", "blog", "covers", "φωτογραφία_0123456789abcdef.JPG"), [1, 2, 3]);

        var (stream, contentType) = await _storage.GetFileAsync(oldPath);
        await using (stream)
        {
            contentType.ShouldBe("image/jpeg");
        }
    }

    [Fact]
    public async Task SaveFile_FolderWithTraversal_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _storage.SaveFileAsync(Stream(), "image/png", "../escape"));
    }

    [Fact]
    public async Task SaveFile_ExceedsExplicitMax_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => _storage.SaveFileAsync(Stream(100), "image/png", "avatars", maxBytes: 10));
    }

    [Fact]
    public async Task SaveFile_UnknownFolderWithoutCap_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => _storage.SaveFileAsync(Stream(), "image/png", "mystery-folder"));
    }

    [Fact]
    public async Task SaveFile_FolderHeuristicWithinCap_Succeeds()
    {
        var path = await _storage.SaveFileAsync(Stream(1024), "application/pdf", "lessons/pdf");

        _storage.FileExists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task GetFile_Missing_Throws()
    {
        await Should.ThrowAsync<FileNotFoundException>(
            () => _storage.GetFileAsync("avatars/missing.png"));
    }

    [Fact]
    public async Task GetFile_InvalidPath_Throws()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _storage.GetFileAsync("../secret"));
    }

    [Fact]
    public async Task GetFile_ReturnsStreamAndContentTypeByExtension()
    {
        var path = await _storage.SaveFileAsync(Stream(), "image/png", "avatars");

        var (stream, contentType) = await _storage.GetFileAsync(path);
        await using (stream)
        {
            contentType.ShouldBe("image/png");
        }
    }

    [Fact]
    public async Task DeleteFile_RemovesExisting_AndIgnoresMissing()
    {
        var path = await _storage.SaveFileAsync(Stream(), "image/png", "avatars");

        await _storage.DeleteFileAsync(path);
        _storage.FileExists(path).ShouldBeFalse();

        await Should.NotThrowAsync(() => _storage.DeleteFileAsync(path)); // already gone
    }

    [Fact]
    public void FileExists_InvalidPath_ReturnsFalse()
    {
        _storage.FileExists("../nope").ShouldBeFalse();
    }

    // P05-6: a copy that fails or is cancelled leaves no half-written file behind.

    [Fact]
    public async Task SaveFile_CancelledMidCopy_LeavesNoFile()
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new FailingSecondReadStream(cancellation);

        await Should.ThrowAsync<OperationCanceledException>(
            () => _storage.SaveFileAsync(source, "image/png", "avatars", cancellationToken: cancellation.Token));

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveFile_SourceFailsMidCopy_LeavesNoFileAndRethrows()
    {
        await using var source = new FailingSecondReadStream(cancellation: null);

        await Should.ThrowAsync<IOException>(() => _storage.SaveFileAsync(source, "image/png", "avatars"));

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <summary>Gives some bytes on the first read, then cancels the token (when given) or fails on the second.</summary>
    private sealed class FailingSecondReadStream(CancellationTokenSource? cancellation) : Stream
    {
        private int _reads;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _reads++;
            if (_reads == 1)
            {
                buffer.Span[..10].Clear();
                return ValueTask.FromResult(10);
            }

            if (cancellation is not null)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new IOException("the source broke");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // P05-5: every path the storage takes is resolved against the uploads folder and must stay inside it.
    // The absolute path, the drive/stream colon and the control character cases all used to get past the
    // ".." check, because Path.Combine drops the base folder when its second part is rooted.

    [Theory]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\file.png")]
    [InlineData("avatars/photo.png:hidden")]
    [InlineData("avatars/photo\u0000.png")]
    [InlineData("avatars/photo\n.png")]
    [InlineData("avatars/../../outside.png")]
    public async Task GetFile_PathOutsideTheUploadsFolder_Throws(string path)
    {
        await Should.ThrowAsync<ArgumentException>(() => _storage.GetFileAsync(path));
    }

    [Theory]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("avatars/photo.png:hidden")]
    [InlineData("avatars/photo\u0000.png")]
    public void FileExists_PathOutsideTheUploadsFolder_ReturnsFalse(string path)
    {
        _storage.FileExists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteFile_AbsolutePathOfAFileOutsideTheUploadsFolder_ThrowsAndKeepsTheFile()
    {
        var outside = Path.Combine(Path.GetTempPath(), "fitedu-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "keep me");
        try
        {
            await Should.ThrowAsync<ArgumentException>(() => _storage.DeleteFileAsync(outside));

            File.Exists(outside).ShouldBeTrue();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("C:/escape")]
    [InlineData("/escape")]
    [InlineData("avatars/../../escape")]
    [InlineData("avatars:stream")]
    public async Task SaveFile_FolderOutsideTheUploadsFolder_Throws(string folder)
    {
        await Should.ThrowAsync<ArgumentException>(
            () => _storage.SaveFileAsync(Stream(), "image/png", folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
