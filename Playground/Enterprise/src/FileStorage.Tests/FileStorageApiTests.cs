using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FileStorage.Models;
using NUnit.Framework;

namespace FileStorage.Tests;

[TestFixture]
public class FileStorageApiTests
{
    private FileStorageWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new FileStorageWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    #region Bucket Operations

    [Test]
    public async Task CreateBucket_ShouldReturnCreated()
    {
        // Act
        var response = await _client.PostAsync("/api/files/buckets/test-bucket", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task EnableVersioning_ShouldReturnOk()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/versioned-bucket", null);

        // Act
        var response = await _client.PostAsync("/api/files/buckets/versioned-bucket/versioning", null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("Enabled"));
    }

    #endregion

    #region File Upload

    [Test]
    public async Task UploadFile_ShouldReturnCreated()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/upload-bucket", null);
        var content = new StringContent("Hello, World!", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/upload-bucket/test.txt", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task UploadFile_ShouldReturnFileMetadata()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/metadata-bucket", null);
        var content = new StringContent("Test content", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/metadata-bucket/document.txt", content);

        // Assert
        var metadata = await response.Content.ReadFromJsonAsync<StoredFile>();
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata!.Key, Is.EqualTo("document.txt"));
        Assert.That(metadata.BucketName, Is.EqualTo("metadata-bucket"));
        Assert.That(metadata.Size, Is.GreaterThan(0));
    }

    [Test]
    public async Task UploadFile_WithNestedPath_ShouldSucceed()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/nested-bucket", null);
        var content = new StringContent("Nested file content", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/nested-bucket/folder/subfolder/file.txt", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var metadata = await response.Content.ReadFromJsonAsync<StoredFile>();
        Assert.That(metadata!.Key, Is.EqualTo("folder/subfolder/file.txt"));
    }

    #endregion

    #region File Download

    [Test]
    public async Task GetFile_ShouldReturnFileContent()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/download-bucket", null);
        var uploadContent = "Download me!";
        await _client.PostAsync("/api/files/download-bucket/download.txt",
            new StringContent(uploadContent, Encoding.UTF8, "text/plain"));

        // Act
        var response = await _client.GetAsync("/api/files/download-bucket/download.txt");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo(uploadContent));
    }

    [Test]
    public async Task GetFile_NonExistent_ShouldReturnNotFound()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/empty-bucket", null);

        // Act
        var response = await _client.GetAsync("/api/files/empty-bucket/nonexistent.txt");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetFile_WithVersionId_ShouldReturnSpecificVersion()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/version-bucket", null);
        await _client.PostAsync("/api/files/buckets/version-bucket/versioning", null);

        // Upload first version
        var response1 = await _client.PostAsync("/api/files/version-bucket/versioned.txt",
            new StringContent("Version 1", Encoding.UTF8, "text/plain"));
        var metadata1 = await response1.Content.ReadFromJsonAsync<StoredFile>();

        // Upload second version
        await _client.PostAsync("/api/files/version-bucket/versioned.txt",
            new StringContent("Version 2", Encoding.UTF8, "text/plain"));

        // Act - Get first version
        var response = await _client.GetAsync($"/api/files/version-bucket/versioned.txt?versionId={metadata1!.VersionId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Version 1"));
    }

    #endregion

    #region File Listing

    [Test]
    public async Task ListFiles_ShouldReturnAllFiles()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/list-bucket", null);
        await _client.PostAsync("/api/files/list-bucket/file1.txt",
            new StringContent("Content 1", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/list-bucket/file2.txt",
            new StringContent("Content 2", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/list-bucket/file3.txt",
            new StringContent("Content 3", Encoding.UTF8, "text/plain"));

        // Act
        var response = await _client.GetAsync("/api/files/list-bucket");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var files = await response.Content.ReadFromJsonAsync<List<StoredFile>>();
        Assert.That(files, Is.Not.Null);
        Assert.That(files!.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task ListFiles_WithPrefix_ShouldReturnFilteredFiles()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/prefix-bucket", null);
        await _client.PostAsync("/api/files/prefix-bucket/docs/readme.txt",
            new StringContent("Readme", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/prefix-bucket/docs/guide.txt",
            new StringContent("Guide", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/prefix-bucket/images/logo.png",
            new StringContent("Logo", Encoding.UTF8, "image/png"));

        // Act
        var response = await _client.GetAsync("/api/files/prefix-bucket?prefix=docs/");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var files = await response.Content.ReadFromJsonAsync<List<StoredFile>>();
        Assert.That(files, Is.Not.Null);
        Assert.That(files!.Count, Is.EqualTo(2));
        Assert.That(files.All(f => f.Key.StartsWith("docs/")), Is.True);
    }

    [Test]
    public async Task ListFiles_EmptyBucket_ShouldReturnEmptyList()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/empty-list-bucket", null);

        // Act
        var response = await _client.GetAsync("/api/files/empty-list-bucket");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var files = await response.Content.ReadFromJsonAsync<List<StoredFile>>();
        Assert.That(files, Is.Not.Null);
        Assert.That(files!.Count, Is.EqualTo(0));
    }

    #endregion

    #region File Deletion

    [Test]
    public async Task DeleteFile_ShouldReturnNoContent()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/delete-bucket", null);
        await _client.PostAsync("/api/files/delete-bucket/to-delete.txt",
            new StringContent("Delete me", Encoding.UTF8, "text/plain"));

        // Act
        var response = await _client.DeleteAsync("/api/files/delete-bucket/to-delete.txt");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteFile_NonExistent_ShouldReturnNotFound()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/delete-nonexist-bucket", null);

        // Act
        var response = await _client.DeleteAsync("/api/files/delete-nonexist-bucket/nonexistent.txt");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteFile_FileNoLongerAccessible()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/verify-delete-bucket", null);
        await _client.PostAsync("/api/files/verify-delete-bucket/deleted.txt",
            new StringContent("Will be deleted", Encoding.UTF8, "text/plain"));

        // Act
        await _client.DeleteAsync("/api/files/verify-delete-bucket/deleted.txt");
        var getResponse = await _client.GetAsync("/api/files/verify-delete-bucket/deleted.txt");

        // Assert
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteFile_WithVersioning_CreatesDeleteMarker()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/delete-marker-bucket", null);
        await _client.PostAsync("/api/files/buckets/delete-marker-bucket/versioning", null);
        await _client.PostAsync("/api/files/delete-marker-bucket/marked.txt",
            new StringContent("Versioned delete", Encoding.UTF8, "text/plain"));

        // Act
        await _client.DeleteAsync("/api/files/delete-marker-bucket/marked.txt");

        // The file should appear deleted but versions should exist
        var getResponse = await _client.GetAsync("/api/files/delete-marker-bucket/marked.txt");

        // Assert
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region Version Management

    [Test]
    public async Task GetFileVersions_ShouldReturnAllVersions()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/versions-bucket", null);
        await _client.PostAsync("/api/files/buckets/versions-bucket/versioning", null);

        // Upload multiple versions
        await _client.PostAsync("/api/files/versions-bucket/multi-version.txt",
            new StringContent("Version 1", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/versions-bucket/multi-version.txt",
            new StringContent("Version 2", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/versions-bucket/multi-version.txt",
            new StringContent("Version 3", Encoding.UTF8, "text/plain"));

        // Act
        var response = await _client.GetAsync("/api/files/versions/versions-bucket/multi-version.txt");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var versions = await response.Content.ReadFromJsonAsync<List<FileVersion>>();
        Assert.That(versions, Is.Not.Null);
        Assert.That(versions!.Count, Is.EqualTo(3));
    }

    [Test]
    public async Task GetFileVersions_ShouldShowLatestVersion()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/latest-bucket", null);
        await _client.PostAsync("/api/files/buckets/latest-bucket/versioning", null);

        await _client.PostAsync("/api/files/latest-bucket/latest.txt",
            new StringContent("Old", Encoding.UTF8, "text/plain"));
        await _client.PostAsync("/api/files/latest-bucket/latest.txt",
            new StringContent("New", Encoding.UTF8, "text/plain"));

        // Act
        var response = await _client.GetAsync("/api/files/versions/latest-bucket/latest.txt");

        // Assert
        var versions = await response.Content.ReadFromJsonAsync<List<FileVersion>>();
        Assert.That(versions!.Count(v => v.IsLatest), Is.EqualTo(1));
    }

    #endregion

    #region Content Type Handling

    [Test]
    public async Task UploadFile_PreservesContentType()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/content-type-bucket", null);
        var content = new StringContent("{\"key\": \"value\"}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/files/content-type-bucket/data.json", content);

        // Assert
        var metadata = await response.Content.ReadFromJsonAsync<StoredFile>();
        Assert.That(metadata!.ContentType, Is.EqualTo("application/json; charset=utf-8"));
    }

    [Test]
    public async Task UploadAndDownload_BinaryContent()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/binary-bucket", null);
        var binaryData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD };
        var content = new ByteArrayContent(binaryData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        // Upload
        await _client.PostAsync("/api/files/binary-bucket/binary.bin", content);

        // Act - Download
        var response = await _client.GetAsync("/api/files/binary-bucket/binary.bin");

        // Assert
        var downloadedData = await response.Content.ReadAsByteArrayAsync();
        Assert.That(downloadedData, Is.EqualTo(binaryData));
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task UploadFile_EmptyFile()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/empty-file-bucket", null);
        var content = new StringContent("", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/empty-file-bucket/empty.txt", content);

        // Assert - Empty content should still be created
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task UploadFile_LargeFileName()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/long-name-bucket", null);
        var longName = new string('a', 200) + ".txt";
        var content = new StringContent("Content", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync($"/api/files/long-name-bucket/{longName}", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task UploadFile_SpecialCharactersInKey()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/special-chars-bucket", null);
        var content = new StringContent("Special", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/special-chars-bucket/file-with_underscore.txt", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task OverwriteFile_WithoutVersioning_ReplacesContent()
    {
        // Arrange
        await _client.PostAsync("/api/files/buckets/overwrite-bucket", null);
        await _client.PostAsync("/api/files/overwrite-bucket/overwrite.txt",
            new StringContent("Original", Encoding.UTF8, "text/plain"));

        // Act
        await _client.PostAsync("/api/files/overwrite-bucket/overwrite.txt",
            new StringContent("Updated", Encoding.UTF8, "text/plain"));
        var response = await _client.GetAsync("/api/files/overwrite-bucket/overwrite.txt");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Updated"));
    }

    #endregion

    #region Default Bucket

    [Test]
    public async Task DefaultBucket_ShouldExist()
    {
        // Act
        var response = await _client.GetAsync("/api/files/default");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DefaultBucket_CanUploadFiles()
    {
        // Arrange
        var content = new StringContent("Default bucket file", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/api/files/default/default-file.txt", content);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }

    #endregion
}
