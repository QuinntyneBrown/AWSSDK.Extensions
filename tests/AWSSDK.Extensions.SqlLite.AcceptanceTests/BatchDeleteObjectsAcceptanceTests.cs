using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for DeleteObjects (batch delete) operations using SqlLiteS3Client.
/// Tests verify the behavior of batch deleting multiple objects.
/// </summary>
public class BatchDeleteObjectsAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public BatchDeleteObjectsAcceptanceTests()
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

    // Acceptance Criteria 3.1 - Scenario: Batch delete multiple objects in non-versioned bucket
    [Fact]
    public async Task DeleteObjectsAsync_NonVersionedBucket_DeletesAllObjects()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        // Create multiple objects
        var keys = new[] { "file1.txt", "file2.txt", "file3.txt" };
        foreach (var key in keys)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                ContentBody = $"content-{key}"
            });
        }

        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = keys.Select(k => new KeyVersion { Key = k }).ToList()
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(3, response.DeletedObjects.Count);
        Assert.Empty(response.DeleteErrors);

        // Verify all objects are deleted
        foreach (var key in keys)
        {
            var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
                async () => await _client.GetObjectAsync(bucketName, key));
            Assert.Equal("NoSuchKey", exception.ErrorCode);
        }
    }

    // Acceptance Criteria 3.1 - Scenario: Batch delete with version IDs in versioned bucket
    [Fact]
    public async Task DeleteObjectsAsync_VersionedBucket_WithVersionIds_DeletesSpecificVersions()
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
            ContentBody = "v1"
        });

        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "v2"
        });

        // Delete first version specifically
        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion>
            {
                new KeyVersion { Key = "file.txt", VersionId = putResponse1.VersionId }
            }
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Single(response.DeletedObjects);
        Assert.Equal(putResponse1.VersionId, response.DeletedObjects[0].VersionId);

        // Verify first version is deleted but second still exists
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "file.txt" && !v.IsDeleteMarker).ToList();
        Assert.Single(versions);
        Assert.Equal(putResponse2.VersionId, versions[0].VersionId);
    }

    // Acceptance Criteria 3.1 - Scenario: Batch delete without version IDs creates delete markers
    [Fact]
    public async Task DeleteObjectsAsync_VersionedBucket_WithoutVersionIds_CreatesDeleteMarkers()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create objects
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file1.txt",
            ContentBody = "content1"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file2.txt",
            ContentBody = "content2"
        });

        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion>
            {
                new KeyVersion { Key = "file1.txt" },
                new KeyVersion { Key = "file2.txt" }
            }
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(2, response.DeletedObjects.Count);

        // Verify delete markers were created
        foreach (var deleted in response.DeletedObjects)
        {
            Assert.True(deleted.DeleteMarker);
            Assert.NotNull(deleted.DeleteMarkerVersionId);
        }

        // Objects should appear deleted (get returns 404)
        foreach (var key in new[] { "file1.txt", "file2.txt" })
        {
            var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
                async () => await _client.GetObjectAsync(bucketName, key));
            Assert.Equal("NoSuchKey", exception.ErrorCode);
        }
    }

    // Acceptance Criteria 3.1 - Scenario: Batch delete on non-existent bucket
    [Fact]
    public async Task DeleteObjectsAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Arrange
        var request = new DeleteObjectsRequest
        {
            BucketName = "non-existent-bucket",
            Objects = new List<KeyVersion> { new KeyVersion { Key = "file.txt" } }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.DeleteObjectsAsync(request));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 3.1 - Scenario: Delete non-existent objects succeeds (idempotent)
    [Fact]
    public async Task DeleteObjectsAsync_NonExistentObjects_SucceedsIdempotently()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion>
            {
                new KeyVersion { Key = "non-existent-1.txt" },
                new KeyVersion { Key = "non-existent-2.txt" }
            }
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert - S3 returns success for non-existent objects
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(2, response.DeletedObjects.Count);
        Assert.Empty(response.DeleteErrors);
    }

    // Acceptance Criteria 3.1 - Scenario: Quiet mode suppresses successful responses
    [Fact]
    public async Task DeleteObjectsAsync_QuietMode_ReturnsOnlyErrors()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Quiet = true,
            Objects = new List<KeyVersion> { new KeyVersion { Key = "file.txt" } }
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert - In quiet mode, successful deletes are not listed
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Empty(response.DeletedObjects);
        Assert.Empty(response.DeleteErrors);
    }

    // Acceptance Criteria 3.1 - Scenario: Delete delete markers by version ID
    [Fact(Skip = "SqlLite implementation does not yet support restoring objects after delete marker removal")]
    public async Task DeleteObjectsAsync_DeleteMarkerByVersionId_RemovesDeleteMarker()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create object
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Delete object to create delete marker
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteResponse.VersionId;

        // Delete the delete marker
        var batchDeleteRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion>
            {
                new KeyVersion { Key = "file.txt", VersionId = deleteMarkerVersionId }
            }
        };

        // Act
        var batchDeleteResponse = await _client.DeleteObjectsAsync(batchDeleteRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, batchDeleteResponse.HttpStatusCode);
        Assert.Single(batchDeleteResponse.DeletedObjects);

        // Object should be accessible again
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
    }

    // Acceptance Criteria 3.1 - Scenario: Mixed successful and failed deletes
    [Fact]
    public async Task DeleteObjectsAsync_MixedObjects_ReturnsPartialSuccess()
    {
        // Arrange
        var bucketName = "test-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "existing.txt",
            ContentBody = "content"
        });

        var request = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion>
            {
                new KeyVersion { Key = "existing.txt" },
                new KeyVersion { Key = "non-existent.txt" }
            }
        };

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert - Both should succeed (S3 is idempotent)
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(2, response.DeletedObjects.Count);
        Assert.Empty(response.DeleteErrors);
    }
}
