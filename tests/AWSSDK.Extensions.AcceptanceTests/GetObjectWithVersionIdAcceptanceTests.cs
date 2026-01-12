using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetObject operations with VersionId.
/// Tests verify the behavior of retrieving specific object versions.
/// </summary>
public class GetObjectWithVersionIdAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public GetObjectWithVersionIdAcceptanceTests()
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

    // Acceptance Criteria 4.1 - Scenario: Get current version of object without specifying VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" (older) and "v2" (current)
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 200
    // And the response should contain the content of version "v2"
    // And the response header x-amz-version-id should be "v2"
    [Fact]
    public async Task GetObjectAsync_WithoutVersionId_ReturnsCurrentVersion()
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
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2" });

        // Act
        var response = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse2.VersionId, response.VersionId);

        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version2", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get specific older version of object by VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" (older) and "v2" (current)
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    // Then the response should have HTTP status code 200
    // And the response should contain the content of version "v1"
    // And the response header x-amz-version-id should be "v1"
    [Fact]
    public async Task GetObjectAsync_WithOlderVersionId_ReturnsOlderVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create two versions and capture version IDs
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2" });

        // Act - Get the older version
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse1.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse1.VersionId, response.VersionId);

        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version1", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get object when current version is a delete marker (without VersionId)
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted-file.txt" current version is a delete marker
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "deleted-file.txt" without VersionId
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchKey"
    // And the HTTP status code should be 404
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

        // Create and then delete the object
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "deleted-file.txt", ContentBody = "content" });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "deleted-file.txt"));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get specific version when current version is a delete marker
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted-file.txt" has version "v1" and current version is a delete marker
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "deleted-file.txt" and VersionId "v1"
    // Then the response should have HTTP status code 200
    // And the response should contain the content of version "v1"
    // And the response header x-amz-version-id should be "v1"
    [Fact]
    public async Task GetObjectAsync_SpecificVersionWhenDeleteMarkerExists_ReturnsVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create version and then delete
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "deleted-file.txt", ContentBody = "version1" });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act - Get the specific version even though delete marker exists
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = "deleted-file.txt",
            VersionId = putResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(putResponse.VersionId, response.VersionId);

        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version1", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Attempt to GET a delete marker by its VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has a delete marker with VersionId "dm-123"
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    // Then the response should throw AmazonS3Exception
    // And the HTTP status code should be 405 (Method Not Allowed)
    [Fact]
    public async Task GetObjectAsync_DeleteMarkerVersionId_ThrowsMethodNotAllowedException()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete to get a delete marker
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act & Assert - Try to GET the delete marker itself
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = deleteResponse.VersionId
            }));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get object with non-existent VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" exists with version "v1"
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "non-existent-version"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchVersion"
    // And the HTTP status code should be 404
    [Fact]
    public async Task GetObjectAsync_NonExistentVersionId_ThrowsNoSuchVersionException()
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
            async () => await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = "non-existent-version"
            }));

        Assert.Equal("NoSuchVersion", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 4.1 - Scenario: Get object from non-versioned bucket with VersionId parameter
    // Given I have valid AWS credentials
    // And I own a bucket "non-versioned-bucket" without versioning enabled
    // And object "file.txt" exists in the bucket
    // When I call GetObjectAsync with bucket "non-versioned-bucket" and key "file.txt" and VersionId "null"
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
    [Fact]
    public async Task GetObjectAsync_NonVersionedBucketWithNullVersionId_ReturnsObject()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "test content" });

        // Act
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = "null"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("test content", content);
    }

    // Acceptance Criteria 4.1 - Scenario: Get current version only requires s3:GetObject permission
    // Given I have valid AWS credentials
    // And I have s3:GetObject permission but not s3:GetObjectVersion
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" exists with current version
    // When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 200
    // And the response should contain the object content
    [Fact]
    public async Task GetObjectAsync_CurrentVersionWithGetObjectPermission_Succeeds()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "current content" });

        // Act
        var response = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        using var reader = new StreamReader(response.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("current content", content);
    }

    // Acceptance Criteria 4.1 - Additional test: Verify version ID is returned in response
    // When getting an object from a versioned bucket, the response should include the version ID
    [Fact]
    public async Task GetObjectAsync_VersionedBucket_ReturnsVersionIdInResponse()
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
        var response = await _client.GetObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.VersionId);
        Assert.Equal(putResponse.VersionId, response.VersionId);
    }
}
