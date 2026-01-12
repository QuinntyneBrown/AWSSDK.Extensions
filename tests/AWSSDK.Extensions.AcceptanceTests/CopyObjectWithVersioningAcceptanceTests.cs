using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for CopyObject operations with versioning.
/// Tests verify the behavior of copying specific object versions.
/// </summary>
public class CopyObjectWithVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public CopyObjectWithVersioningAcceptanceTests()
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

    // Acceptance Criteria 9.1 - Scenario: Copy specific version to new key
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "source.txt" has versions "v1" and "v2"
    // When I call CopyObjectAsync from "source.txt" version "v1" to "dest.txt"
    // Then the response should have HTTP status code 200
    // And "dest.txt" should be created with content from "source.txt" version "v1"
    // And "dest.txt" should have a new unique VersionId (if bucket is versioned)
    [Fact]
    public async Task CopyObjectAsync_SpecificVersion_CopiesToNewKey()
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
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "version1 content"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "version2 content"
        });

        // Act - Copy version 1 to new key
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            SourceVersionId = putResponse1.VersionId,
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.VersionId);
        Assert.NotEqual(putResponse1.VersionId, copyResponse.VersionId);

        // Verify content matches v1
        var getResponse = await _client.GetObjectAsync(bucketName, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version1 content", content);
    }

    // Acceptance Criteria 9.1 - Scenario: Copy without specifying version copies current version
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "source.txt" has versions "v1" (older) and "v2" (current)
    // When I call CopyObjectAsync from "source.txt" without VersionId to "dest.txt"
    // Then "dest.txt" should be created with content from "source.txt" version "v2"
    [Fact]
    public async Task CopyObjectAsync_WithoutVersionId_CopiesCurrentVersion()
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
            ContentBody = "version1 content"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "version2 content"
        });

        // Act - Copy without version (should copy current)
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);

        // Verify content matches v2 (current)
        var getResponse = await _client.GetObjectAsync(bucketName, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version2 content", content);
    }

    // Acceptance Criteria 9.1 - Scenario: Copy creates new version in destination bucket
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "dest.txt" already exists with version "d1"
    // When I call CopyObjectAsync from "source.txt" to "dest.txt"
    // Then a new version should be created for "dest.txt"
    // And version "d1" should become a noncurrent version
    [Fact]
    public async Task CopyObjectAsync_ExistingDestination_CreatesNewVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create source
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "source content"
        });

        // Create existing destination
        var destPutResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "dest.txt",
            ContentBody = "original dest content"
        });

        // Act
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotEqual(destPutResponse.VersionId, copyResponse.VersionId);

        // Both versions should exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var destVersions = listResponse.Versions.Where(v => v.Key == "dest.txt").ToList();
        Assert.Equal(2, destVersions.Count);

        // New version should be current
        var currentVersion = destVersions.First(v => v.IsLatest);
        Assert.Equal(copyResponse.VersionId, currentVersion.VersionId);
    }

    // Acceptance Criteria 9.1 - Scenario: Copy from versioning-enabled to non-versioned bucket
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "source-bucket"
    // And I own a non-versioned bucket "dest-bucket"
    // And object "source.txt" has version "v1"
    // When I call CopyObjectAsync from "source-bucket/source.txt" version "v1" to "dest-bucket/dest.txt"
    // Then "dest.txt" should be created in "dest-bucket"
    // And "dest.txt" should have null VersionId
    [Fact]
    public async Task CopyObjectAsync_ToNonVersionedBucket_HasNullVersionId()
    {
        // Arrange
        var sourceBucket = "source-bucket";
        var destBucket = "dest-bucket";

        await _client.PutBucketAsync(sourceBucket);
        await _client.PutBucketAsync(destBucket);

        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = sourceBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = sourceBucket,
            Key = "source.txt",
            ContentBody = "source content"
        });

        // Act
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = "source.txt",
            SourceVersionId = putResponse.VersionId,
            DestinationBucket = destBucket,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(copyResponse.VersionId) || copyResponse.VersionId == "null");

        // Verify content was copied
        var getResponse = await _client.GetObjectAsync(destBucket, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("source content", content);
    }

    // Acceptance Criteria 9.1 - Scenario: Copy cannot copy a delete marker
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted.txt" current version is a delete marker
    // When I call CopyObjectAsync from "deleted.txt" without VersionId
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchKey"
    // And the HTTP status code should be 404
    [Fact]
    public async Task CopyObjectAsync_DeleteMarkerAsCurrent_ThrowsNoSuchKeyException()
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
            async () => await _client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = "deleted.txt",
                DestinationBucket = bucketName,
                DestinationKey = "dest.txt"
            }));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Additional test: Copy preserves metadata from source version
    [Fact]
    public async Task CopyObjectAsync_PreservesMetadataFromSourceVersion()
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
            ContentBody = "test content",
            ContentType = "text/plain",
            Metadata = { ["custom-key"] = "custom-value" }
        });

        // Act
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);

        var metadata = await _client.GetObjectMetadataAsync(bucketName, "dest.txt");
        Assert.Equal("text/plain", metadata.Headers.ContentType);
    }

    // Additional test: Copy between different buckets with versioning
    [Fact]
    public async Task CopyObjectAsync_BetweenVersionedBuckets_CreatesNewVersion()
    {
        // Arrange
        var sourceBucket = "source-bucket";
        var destBucket = "dest-bucket";

        await _client.PutBucketAsync(sourceBucket);
        await _client.PutBucketAsync(destBucket);

        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = sourceBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = destBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = sourceBucket,
            Key = "source.txt",
            ContentBody = "source content"
        });

        // Act
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = "source.txt",
            SourceVersionId = putResponse.VersionId,
            DestinationBucket = destBucket,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.VersionId);
        Assert.NotEqual("null", copyResponse.VersionId);

        // Verify content
        var getResponse = await _client.GetObjectAsync(destBucket, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("source content", content);
    }

    // Additional test: Copy returns ETag
    [Fact]
    public async Task CopyObjectAsync_ReturnsETag()
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

        // Act
        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.ETag);
        Assert.NotEmpty(copyResponse.ETag);
    }
}
