using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetObjectMetadata operations with VersionId.
/// Tests verify the behavior of retrieving metadata for specific object versions.
/// </summary>
public class GetObjectMetadataWithVersionIdAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public GetObjectMetadataWithVersionIdAcceptanceTests()
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

    // Acceptance Criteria 5.1 - Scenario: Get metadata for current version without VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" and "v2" (current)
    // When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 200
    // And the response VersionId should be "v2"
    // And the response should include ContentLength, ContentType, ETag, and LastModified
    [Fact]
    public async Task GetObjectMetadataAsync_WithoutVersionId_ReturnsCurrentVersionMetadata()
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
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2content" });

        // Act
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse2.VersionId, response.VersionId);
        Assert.True(response.ContentLength > 0);
        Assert.NotNull(response.ETag);
        Assert.NotEqual(default, response.LastModified);
    }

    // Acceptance Criteria 5.1 - Scenario: Get metadata for specific older version
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" (older) and "v2" (current)
    // When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    // Then the response should have HTTP status code 200
    // And the response VersionId should be "v1"
    [Fact]
    public async Task GetObjectMetadataAsync_WithOlderVersionId_ReturnsOlderVersionMetadata()
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
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2" });

        // Act - Get metadata for older version
        var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse1.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse1.VersionId, response.VersionId);
    }

    // Acceptance Criteria 5.1 - Scenario: Get metadata when current version is a delete marker
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted-file.txt" current version is a delete marker
    // When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "deleted-file.txt" without VersionId
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 404
    [Fact]
    public async Task GetObjectMetadataAsync_CurrentVersionIsDeleteMarker_ThrowsException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "deleted-file.txt", ContentBody = "content" });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectMetadataAsync(bucketName, "deleted-file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 5.1 - Scenario: Get metadata for a delete marker by VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has a delete marker with VersionId "dm-123"
    // When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    // Then the response should have HTTP status code 200
    // And the response should include LastModified
    [Fact]
    public async Task GetObjectMetadataAsync_DeleteMarkerVersionId_ReturnsMetadata()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act - Get metadata for the delete marker
        var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = deleteResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("true", response.DeleteMarker);
        Assert.NotEqual(default, response.LastModified);
    }

    // Acceptance Criteria 5.1 - Scenario: Get metadata with non-existent VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" exists
    // When I call GetObjectMetadataAsync with VersionId "non-existent-version"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 404
    [Fact]
    public async Task GetObjectMetadataAsync_NonExistentVersionId_ThrowsException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = "non-existent-version"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Additional test: Verify metadata includes all required properties
    [Fact]
    public async Task GetObjectMetadataAsync_VersionedBucket_ReturnsAllMetadataProperties()
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
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse.VersionId, response.VersionId);
        Assert.NotNull(response.ETag);
        Assert.True(response.ContentLength > 0);
        Assert.NotEqual(default, response.LastModified);
    }

    // Additional test: Get metadata for non-versioned bucket
    [Fact]
    public async Task GetObjectMetadataAsync_NonVersionedBucket_ReturnsMetadataWithNullVersionId()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });

        // Act
        var response = await _client.GetObjectMetadataAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(response.VersionId) || response.VersionId == "null");
    }
}
