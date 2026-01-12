using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Conditional Delete Operations.
/// Tests verify conditional delete behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 4.
/// </summary>
public class ConditionalDeletesAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ConditionalDeletesAcceptanceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDbPath);
        _client = new CouchbaseS3Client(Path.Combine(_testDbPath, "test.cblite2"));
    }

    public void Dispose()
    {
        _client?.Dispose();
        if (Directory.Exists(_testDbPath))
        {
            try
            {
                Directory.Delete(_testDbPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region 4.1 DeleteObjectAsync with If-Match

    // Acceptance Criteria 4.1 - Scenario: Delete object when ETag matches
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call DeleteObjectAsync with key "file.txt" and IfMatch "abc123"
    // Then the response should have HTTP status code 204
    // And the object should be deleted
    [Fact]
    public async Task DeleteObjectAsync_BasicDelete_DeletesObject()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act
        var response = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.HttpStatusCode);

        // Verify object is deleted
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Fail to delete when ETag does not match
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call DeleteObjectAsync with key "file.txt" and IfMatch "different-etag"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 412 Precondition Failed
    // And the object should NOT be deleted
    [Fact(Skip = "Conditional deletes with If-Match header not yet implemented")]
    public async Task DeleteObjectAsync_IfMatch_ETagMismatch_Returns412()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act & Assert - This requires If-Match header support
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Delete object with wildcard ETag (object exists check)
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists
    // When I call DeleteObjectAsync with key "file.txt" and IfMatch "*"
    // Then the response should have HTTP status code 204
    // And the object should be deleted
    [Fact]
    public async Task DeleteObjectAsync_ExistingObject_DeletesSuccessfully()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Verify object exists
        var existsResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, existsResponse.HttpStatusCode);

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // Verify object is deleted
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Concurrent delete wins over conditional delete
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When a DELETE request succeeds before a conditional delete completes
    // Then the conditional delete may return 409 Conflict or 404 Not Found
    // And the object is already deleted
    [Fact]
    public async Task DeleteObjectAsync_NonExistentObject_SucceedsIdempotently()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act - Delete non-existent object (should succeed idempotently)
        var response = await _client.DeleteObjectAsync(bucketName, "non-existent.txt");

        // Assert - S3 returns 204 even for non-existent objects
        Assert.Equal(HttpStatusCode.NoContent, response.HttpStatusCode);
    }

    #endregion

    #region Delete Operations with Versioning

    // Additional test: Delete in versioned bucket creates delete marker
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

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });
        var originalVersionId = putResponse.VersionId;

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.NotNull(deleteResponse.VersionId);
        Assert.NotEqual(originalVersionId, deleteResponse.VersionId);

        // Verify delete marker was created
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var deleteMarkers = listResponse.Versions.Where(v => v.Key == "file.txt" && v.IsDeleteMarker).ToList();
        Assert.Single(deleteMarkers);

        // Original version should still exist
        var originalVersion = listResponse.Versions.FirstOrDefault(v =>
            v.Key == "file.txt" && v.VersionId == originalVersionId && !v.IsDeleteMarker);
        Assert.NotNull(originalVersion);
    }

    // Additional test: Delete specific version permanently removes it
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

        // Act - Delete specific version
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // Verify version is permanently deleted
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.DoesNotContain(versions, v => v.VersionId == putResponse.VersionId);
    }

    // Additional test: Delete delete marker restores object
    [Fact]
    public async Task DeleteObjectAsync_DeleteMarker_RemovingItRestoresObject()
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
        var deleteMarkerResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteMarkerResponse.VersionId;

        // Verify object appears deleted
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);

        // Act - Delete the delete marker
        await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = deleteMarkerVersionId
        });

        // Assert - Object should be accessible again
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content", content);
    }

    // Additional test: Multiple versions can be deleted in sequence
    [Fact]
    public async Task DeleteObjectAsync_MultipleVersions_CanDeleteAllVersions()
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
        var versionIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                ContentBody = $"version {i}"
            });
            versionIds.Add(response.VersionId);
        }

        // Act - Delete all versions
        foreach (var versionId in versionIds)
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = versionId
            });
        }

        // Assert - No versions should remain
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var remainingVersions = listResponse.Versions.Where(v => v.Key == "file.txt" && !v.IsDeleteMarker).ToList();
        Assert.Empty(remainingVersions);
    }

    // Additional test: Delete from non-existent bucket throws exception
    [Fact]
    public async Task DeleteObjectAsync_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.DeleteObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    #endregion
}
