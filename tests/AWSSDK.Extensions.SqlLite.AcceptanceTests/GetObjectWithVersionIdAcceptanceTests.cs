using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetObject operations with version ID using SqlLiteS3Client.
/// Tests verify the behavior of retrieving specific object versions.
/// </summary>
public class GetObjectWithVersionIdAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public GetObjectWithVersionIdAcceptanceTests()
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

    // Acceptance Criteria 4.1 - Scenario: Get specific version of an object
    [Fact]
    public async Task GetObjectAsync_WithVersionId_ReturnsSpecificVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create multiple versions
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version1-content"
        });

        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version2-content"
        });

        // Act - Get first version specifically
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt", putResponse1.VersionId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(putResponse1.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version1-content", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get latest version without specifying version ID
    [Fact]
    public async Task GetObjectAsync_WithoutVersionId_ReturnsLatestVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create multiple versions
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version1-content"
        });

        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version2-content"
        });

        // Act - Get without version ID (should return latest)
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(putResponse2.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version2-content", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get non-existent version
    [Fact]
    public async Task GetObjectAsync_NonExistentVersion_ThrowsNoSuchVersionException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt", "non-existent-version-id"));

        Assert.Equal("NoSuchVersion", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get object from non-existent bucket
    [Fact]
    public async Task GetObjectAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get non-existent object
    [Fact]
    public async Task GetObjectAsync_NonExistentObject_ThrowsNoSuchKeyException()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "non-existent-key"));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get null version from non-versioned bucket
    [Fact]
    public async Task GetObjectAsync_NullVersionId_ReturnsObject()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act - Get with "null" version ID
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt", "null");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get delete marker returns method not allowed
    [Fact]
    public async Task GetObjectAsync_DeleteMarkerVersion_ThrowsMethodNotAllowedException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Delete object (creates delete marker)
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteResponse.VersionId;

        // Act & Assert - Getting delete marker should fail
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt", deleteMarkerVersionId));

        Assert.Equal("MethodNotAllowed", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get object when current version is delete marker (without version ID)
    [Fact]
    public async Task GetObjectAsync_CurrentVersionIsDeleteMarker_ThrowsNoSuchKeyException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Delete object (creates delete marker as current)
        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act & Assert - Getting without version ID should return 404
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt"));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Additional test: Verify ETag matches for specific version
    [Fact]
    public async Task GetObjectAsync_WithVersionId_ReturnsCorrectETag()
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
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt", putResponse.VersionId);

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(putResponse.ETag, getResponse.ETag);
    }
}
