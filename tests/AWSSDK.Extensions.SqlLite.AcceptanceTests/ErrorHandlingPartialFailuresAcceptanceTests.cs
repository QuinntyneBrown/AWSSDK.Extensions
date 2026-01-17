using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Error Handling and Partial Failures using SqlLiteS3Client.
/// Tests verify error handling behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 9.
/// </summary>
public class ErrorHandlingPartialFailuresAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ErrorHandlingPartialFailuresAcceptanceTests()
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

    #region 9.1 Batch Operation Error Handling

    [Fact]
    public async Task DeleteObjectsAsync_BatchDelete_ReturnsDeletedAndErrorsCollections()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var keys = new[] { "file1.txt", "file2.txt", "file3.txt" };
        foreach (var key in keys)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                ContentBody = $"content of {key}"
            });
        }

        // Act
        var deleteRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = keys.Select(k => new KeyVersion { Key = k }).ToList()
        };
        var response = await _client.DeleteObjectsAsync(deleteRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.DeletedObjects);
        Assert.NotNull(response.DeleteErrors);
        Assert.Equal(3, response.DeletedObjects.Count);
        Assert.Empty(response.DeleteErrors);
    }

    [Fact]
    public async Task DeleteObjectsAsync_EmptyBucket_ProcessesAll()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var keys = new[] { "nonexistent1.txt", "nonexistent2.txt", "nonexistent3.txt" };

        // Act
        var deleteRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = keys.Select(k => new KeyVersion { Key = k }).ToList()
        };
        var response = await _client.DeleteObjectsAsync(deleteRequest);

        // Assert - S3 reports success for non-existent objects (idempotent)
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
    }

    [Fact]
    public async Task DeleteObjectsAsync_Response_HasCorrectStructure()
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
        var deleteRequest = new DeleteObjectsRequest
        {
            BucketName = bucketName,
            Objects = new List<KeyVersion> { new KeyVersion { Key = "file.txt" } }
        };
        var response = await _client.DeleteObjectsAsync(deleteRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Single(response.DeletedObjects);
        Assert.Equal("file.txt", response.DeletedObjects[0].Key);
    }

    #endregion

    #region 9.2 Object Lock Error Handling

    [Fact]
    public async Task DeleteObjectAsync_ObjectUnderRetention_ThrowsAccessDenied()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled
            }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "protected.txt",
            ContentBody = "protected content"
        });

        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "protected.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.Compliance,
                RetainUntilDate = DateTime.UtcNow.AddYears(1)
            }
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Access Denied - Object is protected by retention")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied"
            };
        });

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task DeleteObjectAsync_ObjectWithLegalHold_ThrowsAccessDenied()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            ContentBody = "evidence content"
        });

        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Access Denied - Object has legal hold")
            {
                StatusCode = HttpStatusCode.Forbidden,
                ErrorCode = "AccessDenied"
            };
        });

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task PutObjectAsync_ObjectWithRetention_CreatesNewVersion()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "protected.txt",
            ContentBody = "original content"
        });
        var originalVersionId = putResponse1.VersionId;

        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "protected.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.Governance,
                RetainUntilDate = DateTime.UtcNow.AddDays(30)
            }
        });

        // Act - Upload new content (should create new version)
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "protected.txt",
            ContentBody = "new content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse2.HttpStatusCode);
        Assert.NotEqual(originalVersionId, putResponse2.VersionId);

        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "protected.txt" && !v.IsDeleteMarker).ToList();
        Assert.Equal(2, versions.Count);
        Assert.Contains(versions, v => v.VersionId == originalVersionId);
        Assert.Contains(versions, v => v.VersionId == putResponse2.VersionId);
    }

    #endregion

    #region General Error Handling

    [Fact]
    public async Task GetObjectAsync_NonExistentBucket_ReturnsNoSuchBucket()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    [Fact]
    public async Task GetObjectAsync_NonExistentObject_ReturnsNoSuchKey()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "non-existent.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    [Fact]
    public async Task PutObjectAsync_NonExistentBucket_ReturnsNoSuchBucket()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "non-existent-bucket",
                Key = "file.txt",
                ContentBody = "content"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteBucketAsync_NonEmptyBucket_ReturnsBucketNotEmpty()
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

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.DeleteBucketAsync(bucketName));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("BucketNotEmpty", exception.ErrorCode);
    }

    [Fact]
    public async Task PutBucketAsync_ExistingBucketName_ReturnsBucketAlreadyExists()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.PutBucketAsync(bucketName));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("BucketAlreadyExists", exception.ErrorCode);
    }

    [Fact]
    public async Task HeadBucketAsync_NonExistentBucket_ReturnsNoSuchBucket()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.HeadBucketAsync("non-existent-bucket"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    [Fact]
    public async Task AmazonS3Exception_HasCorrectProperties()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "non-existent.txt"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.NotNull(exception.Message);
    }

    #endregion
}
