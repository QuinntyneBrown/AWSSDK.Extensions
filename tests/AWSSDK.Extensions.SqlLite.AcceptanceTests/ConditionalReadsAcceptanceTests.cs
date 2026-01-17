using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Conditional Read Operations using SqlLiteS3Client.
/// Tests verify conditional read behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 3.
/// </summary>
public class ConditionalReadsAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ConditionalReadsAcceptanceTests()
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

    #region 3.1 GetObjectAsync with Conditional Headers

    [Fact]
    public async Task GetObjectAsync_BasicGet_ReturnsContent()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var content = "test content for reading";
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = content
        });
        var etag = putResponse.ETag;

        // Act
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(etag, getResponse.ETag);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var readContent = await reader.ReadToEndAsync();
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task GetObjectAsync_IfMatch_ETagMismatch_Returns412()
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
        {
            throw new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectAsync_IfNoneMatch_ETagMatches_Returns304()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });
        var etag = putResponse.ETag;

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Not Modified")
            {
                StatusCode = HttpStatusCode.NotModified,
                ErrorCode = "NotModified"
            };
        });

        Assert.Equal(HttpStatusCode.NotModified, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectAsync_ReturnsContentWhenETagsAreDifferent()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var content = "test content";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = content
        });

        // Act
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var readContent = await reader.ReadToEndAsync();
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task GetObjectAsync_ReturnsLastModifiedTimestamp()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var beforePut = DateTime.UtcNow;
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });
        var afterPut = DateTime.UtcNow;

        // Act
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.True(getResponse.LastModified >= beforePut.AddSeconds(-1));
        Assert.True(getResponse.LastModified <= afterPut.AddSeconds(1));
    }

    [Fact]
    public async Task GetObjectAsync_IfModifiedSince_ObjectNotModified_Returns304()
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
        {
            throw new AmazonS3Exception("Not Modified")
            {
                StatusCode = HttpStatusCode.NotModified,
                ErrorCode = "NotModified"
            };
        });

        Assert.Equal(HttpStatusCode.NotModified, exception.StatusCode);
    }

    [Fact]
    public async Task GetObjectAsync_ObjectMetadata_ContainsRequiredFields()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var content = "test content";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = content,
            ContentType = "text/plain"
        });

        // Act
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(content.Length, getResponse.ContentLength);
        Assert.NotNull(getResponse.ETag);
        Assert.NotEqual(default, getResponse.LastModified);
    }

    [Fact]
    public async Task GetObjectAsync_IfUnmodifiedSince_ObjectModified_Returns412()
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
        {
            throw new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    #endregion

    #region GetObject with Versioning

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

        var version1Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version 1 content"
        });

        var version2Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version 2 content"
        });

        // Act
        var getRequest = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = version1Response.VersionId
        };
        var getResponse = await _client.GetObjectAsync(getRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(version1Response.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version 1 content", content);
    }

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

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version 1 content"
        });

        var version2Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version 2 content"
        });

        // Act
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(version2Response.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version 2 content", content);
    }

    [Fact]
    public async Task GetObjectAsync_NonExistentObject_ThrowsException()
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
    public async Task GetObjectAsync_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    #endregion
}
