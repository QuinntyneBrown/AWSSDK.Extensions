using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetObjectMetadata operations with VersionId using SqlLiteS3Client.
/// Tests verify the behavior of retrieving metadata for specific object versions.
/// </summary>
public class GetObjectMetadataWithVersionIdAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public GetObjectMetadataWithVersionIdAcceptanceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"sqlite_test_{Guid.NewGuid()}.db");
        _client = new SqlLiteS3Client(_testDbPath);
    }

    public void Dispose()
    {
        _client?.Dispose();
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task GetObjectMetadataAsync_WithoutVersionId_ReturnsCurrentVersionMetadata()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create two versions
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2content" });

        // Act
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse2.VersionId, response.VersionId);
        Assert.True(response.ContentLength > 0);
        Assert.NotNull(response.ETag);
        Assert.NotEqual(default, response.LastModified);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_WithOlderVersionId_ReturnsOlderVersionMetadata()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create two versions
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2" });

        // Act - Get metadata for older version
        var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse1.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse1.VersionId, response.VersionId);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_CurrentVersionIsDeleteMarker_ThrowsException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "deleted-file.txt", ContentBody = "content" });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectMetadataAsync(bucketName, "deleted-file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_DeleteMarkerVersionId_ReturnsMetadata()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act - Get metadata for the delete marker
        var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = deleteResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("true", response.DeleteMarker);
        Assert.NotEqual(default, response.LastModified);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_NonExistentVersionId_ThrowsException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = "non-existent-version"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_VersionedBucket_ReturnsAllMetadataProperties()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "test content"
        });

        // Act
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse.VersionId, response.VersionId);
        Assert.NotNull(response.ETag);
        Assert.True(response.ContentLength > 0);
        Assert.NotEqual(default, response.LastModified);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_NonVersionedBucket_ReturnsMetadataWithNullVersionId()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });

        // Act
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(response.VersionId) || response.VersionId == "null");
    }
}
