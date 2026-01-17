using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for DeleteObject versioning behavior using SqlLiteS3Client.
/// Tests verify how DeleteObject behaves with different versioning configurations.
/// </summary>
public class DeleteObjectVersioningBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public DeleteObjectVersioningBehaviorAcceptanceTests()
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

    // Acceptance Criteria 7.1 - Scenario: Delete object in versioned bucket creates delete marker
    [Fact]
    public async Task DeleteObjectAsync_VersionedBucket_CreatesDeleteMarker()
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

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.Equal("true", deleteResponse.DeleteMarker);
        Assert.NotNull(deleteResponse.VersionId);
        Assert.NotEqual("null", deleteResponse.VersionId);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete specific version permanently removes it
    [Fact]
    public async Task DeleteObjectAsync_WithVersionId_PermanentlyDeletesVersion()
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
            ContentBody = "content"
        });

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt", putResponse.VersionId);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.Equal(putResponse.VersionId, deleteResponse.VersionId);

        // Verify version is permanently deleted
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt", putResponse.VersionId));
        Assert.Equal("NoSuchVersion", exception.ErrorCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object in non-versioned bucket permanently removes it
    [Fact]
    public async Task DeleteObjectAsync_NonVersionedBucket_PermanentlyDeletes()
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

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // Verify object is deleted
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object preserves other versions
    [Fact]
    public async Task DeleteObjectAsync_PreservesOtherVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "v1"
        });

        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "v2"
        });

        // Act - Delete without version ID (creates delete marker)
        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert - Previous versions should still be accessible
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt", putResponse1.VersionId);
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("v1", content);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete in suspended bucket creates null delete marker
    [Fact]
    public async Task DeleteObjectAsync_SuspendedBucket_CreatesNullDeleteMarker()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.Equal("true", deleteResponse.DeleteMarker);
        Assert.Equal("null", deleteResponse.VersionId);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete from non-existent bucket
    [Fact]
    public async Task DeleteObjectAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.DeleteObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete non-existent object succeeds (idempotent)
    [Fact]
    public async Task DeleteObjectAsync_NonExistentObject_SucceedsIdempotently()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act - Delete non-existent object
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "non-existent.txt");

        // Assert - S3 returns success for non-existent objects
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete delete marker restores access to previous version
    [Fact]
    public async Task DeleteObjectAsync_DeleteMarker_RestoresAccessToPreviousVersion()
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

        // Create delete marker
        var deleteResponse1 = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteResponse1.VersionId;

        // Act - Delete the delete marker
        await _client.DeleteObjectAsync(bucketName, "file.txt", deleteMarkerVersionId);

        // Assert - Object should be accessible again
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content", content);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete all versions and delete markers removes object completely
    [Fact]
    public async Task DeleteObjectAsync_AllVersionsAndDeleteMarkers_RemovesObjectCompletely()
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
            ContentBody = "content"
        });

        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteResponse.VersionId;

        // Act - Delete both the original version and the delete marker
        await _client.DeleteObjectAsync(bucketName, "file.txt", putResponse.VersionId);
        await _client.DeleteObjectAsync(bucketName, "file.txt", deleteMarkerVersionId);

        // Assert - ListVersions should show no versions for this key
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.Empty(fileVersions);
    }
}
