using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for versioning error handling using SqlLiteS3Client.
/// Tests verify proper error responses for versioning operations.
/// </summary>
public class VersioningErrorHandlingAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public VersioningErrorHandlingAcceptanceTests()
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
    public async Task GetObjectAsync_NoSuchVersion_ThrowsNoSuchVersionException()
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
            async () => await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = "non-existent-version-id"
            }));

        Assert.Equal("NoSuchVersion", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectAsync_OnDeleteMarker_ThrowsMethodNotAllowedException()
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

        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = deleteResponse.VersionId
            }));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, exception.StatusCode);
    }

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
            Key = "deleted.txt",
            ContentBody = "content"
        });
        await _client.DeleteObjectAsync(bucketName, "deleted.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "deleted.txt"));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task PutObjectAsync_ConcurrentWrites_AllSucceedWithSeparateVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var tasks = new List<Task<PutObjectResponse>>();
        var versionIds = new List<string>();

        // Act - Simulate concurrent writes
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                return await _client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = "concurrent-file.txt",
                    ContentBody = $"content-{index}"
                });
            }));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert - All requests succeeded
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode));

        // All versions are unique
        versionIds = responses.Select(r => r.VersionId).ToList();
        Assert.Equal(5, versionIds.Distinct().Count());

        // All versions exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "concurrent-file.txt").ToList();
        Assert.Equal(5, fileVersions.Count);
    }

    [Fact]
    public async Task GetBucketVersioningAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync("non-existent-bucket"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task PutBucketVersioningAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
            {
                BucketName = "non-existent-bucket",
                VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
            }));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ListVersionsAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.ListVersionsAsync("non-existent-bucket"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectMetadataAsync_NoSuchVersion_ThrowsException()
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
            async () => await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = "invalid-version"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task CopyObjectAsync_NoSuchSourceVersion_ThrowsException()
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
            Key = "source.txt",
            ContentBody = "content"
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = "source.txt",
                SourceVersionId = "invalid-version",
                DestinationBucket = bucketName,
                DestinationKey = "dest.txt"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectAsync_NonExistentObject_ThrowsNoSuchKeyException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "non-existent-file.txt"));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ErrorResponse_ContainsProperExceptionDetails()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync("non-existent-bucket"));

        Assert.NotNull(exception.ErrorCode);
        Assert.NotNull(exception.Message);
        Assert.True(exception.StatusCode != 0);
    }

    [Fact]
    public async Task DeleteObjectAsync_NonExistentVersion_IsIdempotent()
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

        // Act - Delete a non-existent version (should be idempotent)
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = "non-existent-version-id"
        });

        // Assert - Should succeed (idempotent)
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
    }

    [Fact]
    public async Task VersioningStatus_ProperlyReportedAfterOperations()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        // Initially no versioning
        var initialResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.True(string.IsNullOrEmpty(initialResponse.VersioningConfig?.Status?.Value));

        // Enable versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var enabledResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Enabled, enabledResponse.VersioningConfig.Status);

        // Suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        var suspendedResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Suspended, suspendedResponse.VersioningConfig.Status);
    }
}
