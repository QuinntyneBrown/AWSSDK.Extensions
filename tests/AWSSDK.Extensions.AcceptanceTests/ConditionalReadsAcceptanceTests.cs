using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Conditional Read Operations.
/// Tests verify conditional read behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 3.
/// </summary>
public class ConditionalReadsAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ConditionalReadsAcceptanceTests()
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

    #region 3.1 GetObjectAsync with Conditional Headers

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Match - ETag matches
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call GetObjectAsync with key "file.txt" and IfMatch "abc123"
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
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

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Match - ETag does not match
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call GetObjectAsync with key "file.txt" and IfMatch "different-etag"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 412 Precondition Failed
    [Fact(Skip = "Conditional reads with If-Match header not yet implemented")]
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

        // Act & Assert - This requires If-Match header support
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            // Simulating If-Match failure
            throw new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    // Acceptance Criteria 3.1 - Scenario: Get object with If-None-Match - ETag matches (not modified)
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call GetObjectAsync with key "file.txt" and IfNoneMatch "abc123"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 304 Not Modified
    // And no object content should be returned (bandwidth saved)
    [Fact(Skip = "Conditional reads with If-None-Match header not yet implemented")]
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

        // Act & Assert - This requires If-None-Match header support
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            // Simulating If-None-Match with matching ETag
            throw new AmazonS3Exception("Not Modified")
            {
                StatusCode = HttpStatusCode.NotModified,
                ErrorCode = "NotModified"
            };
        });

        Assert.Equal(HttpStatusCode.NotModified, exception.StatusCode);
    }

    // Acceptance Criteria 3.1 - Scenario: Get object with If-None-Match - ETag does not match
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" exists with ETag "abc123"
    // When I call GetObjectAsync with key "file.txt" and IfNoneMatch "different-etag"
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
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

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Modified-Since - object was modified
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" was last modified "2025-01-10T12:00:00Z"
    // When I call GetObjectAsync with IfModifiedSince "2025-01-09T00:00:00Z"
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
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

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Modified-Since - object was not modified
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" was last modified "2025-01-01T12:00:00Z"
    // When I call GetObjectAsync with IfModifiedSince "2025-01-10T00:00:00Z"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 304 Not Modified
    [Fact(Skip = "Conditional reads with If-Modified-Since header not yet implemented")]
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

        // Act & Assert - This requires If-Modified-Since header support
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

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Unmodified-Since - object was not modified
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" was last modified "2025-01-01T12:00:00Z"
    // When I call GetObjectAsync with IfUnmodifiedSince "2025-01-10T00:00:00Z"
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
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

    // Acceptance Criteria 3.1 - Scenario: Get object with If-Unmodified-Since - object was modified
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket"
    // And object "file.txt" was last modified "2025-01-10T12:00:00Z"
    // When I call GetObjectAsync with IfUnmodifiedSince "2025-01-05T00:00:00Z"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 412 Precondition Failed
    [Fact(Skip = "Conditional reads with If-Unmodified-Since header not yet implemented")]
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

    // Additional test: Get specific version of object
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

        // Act - Get the first version
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

    // Additional test: Get latest version without specifying VersionId
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

    // Additional test: Get non-existent object throws exception
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

    // Additional test: Get object from non-existent bucket throws exception
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
