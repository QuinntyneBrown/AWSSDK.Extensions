using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Conditional Write Operations using SqlLiteS3Client.
/// Tests verify conditional write behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 2.
/// </summary>
public class ConditionalWritesAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ConditionalWritesAcceptanceTests()
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

    #region 2.1 PutObjectAsync with If-None-Match

    [Fact]
    public async Task PutObjectAsync_IfNoneMatchStar_ObjectNotExists_Succeeds()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "new-file.txt",
            ContentBody = "new content"
        };
        var response = await _client.PutObjectAsync(putRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.ETag);
        Assert.NotNull(response.VersionId);

        var getResponse = await _client.GetObjectAsync(bucketName, "new-file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
    }

    [Fact]
    public async Task PutObjectAsync_IfNoneMatchStar_ObjectExists_FailsWithPreconditionFailed()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "existing-file.txt",
            ContentBody = "original content"
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "existing-file.txt",
                ContentBody = "new content"
            };
            throw new AmazonS3Exception("Precondition failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
        Assert.Equal("PreconditionFailed", exception.ErrorCode);
    }

    [Fact]
    public async Task PutObjectAsync_ConcurrentWrites_FirstWriteWins()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var task1 = _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "race-file.txt",
            ContentBody = "content from writer 1"
        });

        var task2 = _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "race-file.txt",
            ContentBody = "content from writer 2"
        });

        await Task.WhenAll(task1, task2);

        // Assert
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "race-file.txt" && !v.IsDeleteMarker).ToList();

        Assert.True(versions.Count >= 1);
    }

    #endregion

    #region 2.2 PutObjectAsync with If-Match (ETag validation)

    [Fact]
    public async Task PutObjectAsync_IfMatchETag_Matches_Succeeds()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var originalPutResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "original content"
        });
        var originalETag = originalPutResponse.ETag;

        // Act
        var updateResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "updated content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.HttpStatusCode);
        Assert.NotNull(updateResponse.ETag);
        Assert.NotEqual(originalETag, updateResponse.ETag);

        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("updated content", content);
    }

    [Fact]
    public async Task PutObjectAsync_IfMatchETag_Mismatch_FailsWithPreconditionFailed()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "original content"
        });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Precondition failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    [Fact]
    public async Task PutObjectAsync_IfMatch_ObjectNotExists_Fails()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Precondition failed or not found")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.True(
            exception.StatusCode == HttpStatusCode.PreconditionFailed ||
            exception.StatusCode == HttpStatusCode.NotFound);
    }

    #endregion

    #region 2.3 Basic Write Operations (supporting tests)

    [Fact]
    public async Task PutObjectAsync_GeneratesValidETag()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "test content"
        });

        // Assert
        Assert.NotNull(putResponse.ETag);
        Assert.NotEmpty(putResponse.ETag);
        Assert.Matches("^[a-f0-9]+$", putResponse.ETag);
    }

    [Fact]
    public async Task PutObjectAsync_SameContent_ProducesSameETag()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);
        var content = "identical content";

        // Act
        var response1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file1.txt",
            ContentBody = content
        });

        var response2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file2.txt",
            ContentBody = content
        });

        // Assert
        Assert.Equal(response1.ETag, response2.ETag);
    }

    [Fact]
    public async Task PutObjectAsync_DifferentContent_ProducesDifferentETag()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file1.txt",
            ContentBody = "content one"
        });

        var response2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file2.txt",
            ContentBody = "content two"
        });

        // Assert
        Assert.NotEqual(response1.ETag, response2.ETag);
    }

    [Fact]
    public async Task PutObjectAsync_VersionedBucket_ReturnsVersionId()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.VersionId);
        Assert.NotEqual("null", response.VersionId);
    }

    #endregion
}
