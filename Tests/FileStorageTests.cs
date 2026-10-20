using System.Security.Cryptography;
using System.Text;
using EduShelf.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace EduShelf.Tests;

public class FileStorageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "EduShelfTests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage storage;
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF\n");

    public FileStorageTests() => storage = new LocalFileStorage(new TestEnvironment(directory), Options.Create(new StorageOptions { RootPath = "uploads", MaxFileSizeMb = 1 }));

    [Fact]
    public async Task Upload_uses_generated_key_sanitizes_original_name_and_hashes_actual_bytes()
    {
        var file = Upload(Pdf, "../../private\\lesson.PDF");
        var stored = await storage.SaveAsync(file);
        Assert.Matches("^[a-f0-9]{32}\\.pdf$", stored.StoredFileName);
        Assert.Equal("lesson.PDF", stored.OriginalFileName);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(Pdf.LongLength, stored.FileSize);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Pdf)), stored.FileHash);
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(storage.GetPhysicalPath(stored.StoredFileName)));
        Assert.True(storage.Exists(stored.StoredFileName));
        storage.Delete(stored.StoredFileName);
        Assert.False(storage.Exists(stored.StoredFileName));
    }

    [Theory]
    [InlineData("%HTML-fake PDF", "fake.pdf")]
    [InlineData("%PDF-1.7 fake", "file.exe")]
    [InlineData("", "empty.pdf")]
    [InlineData("%PDF-1.7 fake", "bad\nname.pdf")]
    public async Task Invalid_signature_extension_empty_or_control_name_is_rejected(string bytes, string name)
    {
        await Assert.ThrowsAsync<FileValidationException>(() => storage.SaveAsync(Upload(Encoding.ASCII.GetBytes(bytes), name)));
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Oversized_advertised_length_is_rejected_before_opening_stream()
    {
        await Assert.ThrowsAsync<FileValidationException>(() => storage.SaveAsync(new DeclaredLengthFile(Pdf, 1024 * 1024 + 1)));
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Stream_cannot_bypass_size_limit_by_lying_about_length()
    {
        var bytes = new byte[1024 * 1024 + 1];
        Pdf.CopyTo(bytes, 0);
        await Assert.ThrowsAsync<FileValidationException>(() => storage.SaveAsync(new DeclaredLengthFile(bytes, 10)));
        AssertNoStoredFiles();
    }

    [Fact]
    public async Task Truncated_stream_is_rejected_and_partial_file_is_removed()
    {
        await Assert.ThrowsAsync<FileValidationException>(() => storage.SaveAsync(new DeclaredLengthFile(Pdf, Pdf.Length + 20)));
        AssertNoStoredFiles();
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("C:\\private\\secret.pdf")]
    [InlineData("00000000000000000000000000000000.pdf:stream")]
    [InlineData("arbitrary.pdf")]
    public void Stored_key_cannot_escape_upload_directory(string key)
    {
        Assert.Throws<FileValidationException>(() => storage.GetPhysicalPath(key));
        Assert.Throws<FileValidationException>(() => storage.Delete(key));
        Assert.False(storage.Exists(key));
    }

    [Theory]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/uploads")]
    public void Public_webroot_cannot_be_used_as_storage(string path)
        => Assert.Throws<InvalidOperationException>(() => new LocalFileStorage(new TestEnvironment(directory), Options.Create(new StorageOptions { RootPath = path })));

    private void AssertNoStoredFiles() => Assert.Empty(Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories) : []);

    private static IFormFile Upload(byte[] bytes, string name) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };

    public void Dispose()
    {
        // The target is a fresh GUID child of the explicit temp test root.
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed class DeclaredLengthFile(byte[] bytes, long declaredLength) : IFormFile
    {
        public string ContentType => "application/pdf";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length => declaredLength;
        public string Name => "file";
        public string FileName => "lesson.pdf";
        public Stream OpenReadStream() => new MemoryStream(bytes);
        public void CopyTo(Stream target) => target.Write(bytes);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => target.WriteAsync(bytes, cancellationToken).AsTask();
    }
}

internal sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "EduShelf.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
