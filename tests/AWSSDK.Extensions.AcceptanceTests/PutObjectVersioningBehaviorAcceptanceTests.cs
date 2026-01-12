using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for PutObject versioning behavior.
/// Tests verify how PutObject behaves with different versioning configurations.
/// </summary>
public class PutObjectVersioningBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public PutObjectVersioningBehaviorAcceptanceTests()
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

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-enabled bucket creates new version
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" already exists with version "v1"
    // When I call PutObjectAsync with bucket "versioned-bucket" and key "file.txt" with new content
    // Then the response should have HTTP status code 200
    // And the response should contain a new VersionId different from "v1"
    // And both version "v1" and the new version should exist
    // And the new version should be marked as IsLatest
    [Fact]
    public async Task PutObjectAsync_VersioningEnabled_CreatesNewVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create first version
        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version1"
        });

        // Act - Create second version
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "version2"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse2.HttpStatusCode);
        Assert.NotEqual(putResponse1.VersionId, putResponse2.VersionId);

        // Verify both versions exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.Equal(2, fileVersions.Count);

        // New version should be marked as latest
        var latestVersion = fileVersions.First(v => v.IsLatest);
        Assert.Equal(putResponse2.VersionId, latestVersion.VersionId);
    }

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-enabled bucket - first upload
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "new-file.txt" does not exist
    // When I call PutObjectAsync with bucket "versioned-bucket" and key "new-file.txt"
    // Then the response should have HTTP status code 200
    // And the response should contain a unique VersionId (not null)
    // And the object should be created as the first and current version
    [Fact]
    public async Task PutObjectAsync_VersioningEnabled_FirstUpload_CreatesVersionedObject()
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
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "new-file.txt",
            ContentBody = "content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.HttpStatusCode);
        Assert.NotNull(putResponse.VersionId);
        Assert.NotEqual("null", putResponse.VersionId);

        // Verify object is the only and current version
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "new-file.txt").ToList();
        Assert.Single(fileVersions);
        Assert.True(fileVersions[0].IsLatest);
    }

    // Acceptance Criteria 6.1 - Scenario: Put object in non-versioned bucket has null VersionId
    // Given I have valid AWS credentials
    // And I own a bucket "non-versioned-bucket" without versioning enabled
    // When I call PutObjectAsync with bucket "non-versioned-bucket" and key "file.txt"
    // Then the response should have HTTP status code 200
    // And the response VersionId should be null or not present
    // And subsequent PutObject with same key overwrites the object
    [Fact]
    public async Task PutObjectAsync_NonVersionedBucket_HasNullVersionId()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content1"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(putResponse.VersionId) || putResponse.VersionId == "null");

        // Subsequent put should overwrite
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content2"
        });

        // Verify only one version exists
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content2", content);
    }

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-suspended bucket has null VersionId
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // When I call PutObjectAsync with bucket "suspended-bucket" and key "new-file.txt"
    // Then the response should have HTTP status code 200
    // And the response VersionId should be null
    [Fact]
    public async Task PutObjectAsync_VersioningSuspended_HasNullVersionId()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable then suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Act
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "new-file.txt",
            ContentBody = "content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(putResponse.VersionId) || putResponse.VersionId == "null");
    }

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-suspended bucket overwrites existing null version
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // And object "file.txt" exists with null VersionId
    // When I call PutObjectAsync with bucket "suspended-bucket" and key "file.txt" with new content
    // Then the response should have HTTP status code 200
    // And the object with null VersionId should be overwritten
    // And there should still be only one version with null VersionId
    [Fact]
    public async Task PutObjectAsync_VersioningSuspended_OverwritesNullVersion()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable then suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Create object with null version
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "original"
        });

        // Act - Overwrite with new content
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "updated"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.HttpStatusCode);

        // Verify content was updated
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("updated", content);

        // Verify only one null version exists
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var nullVersions = listResponse.Versions.Where(v => v.Key == "file.txt" && v.VersionId == "null").ToList();
        Assert.Single(nullVersions);
    }

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-suspended bucket does not overwrite versioned objects
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // And object "file.txt" has versions "v1" and "v2" from when versioning was enabled
    // When I call PutObjectAsync with bucket "suspended-bucket" and key "file.txt" with new content
    // Then the response should have HTTP status code 200
    // And a new version with null VersionId should be created as current
    // And versions "v1" and "v2" should still exist as noncurrent versions
    [Fact]
    public async Task PutObjectAsync_VersioningSuspended_PreservesExistingVersions()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning and create versions
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

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

        // Suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Act - Put new object while suspended
        var putResponse3 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "suspended version"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse3.HttpStatusCode);

        // List versions and verify all exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();

        // Should have 3 versions: v1, v2, and null
        Assert.Equal(3, fileVersions.Count);

        // Null version should be current
        var currentVersion = fileVersions.First(v => v.IsLatest);
        Assert.Equal("null", currentVersion.VersionId);

        // Previous versions should still exist
        Assert.Contains(fileVersions, v => v.VersionId == putResponse1.VersionId);
        Assert.Contains(fileVersions, v => v.VersionId == putResponse2.VersionId);
    }

    // Acceptance Criteria 6.1 - Scenario: Verify VersionId format characteristics
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // When I call PutObjectAsync with bucket "versioned-bucket" and key "file.txt"
    // Then the response VersionId should be unique and not null
    [Fact]
    public async Task PutObjectAsync_VersioningEnabled_VersionIdIsUnique()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act - Create multiple versions
        var versionIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                ContentBody = $"content{i}"
            });
            versionIds.Add(response.VersionId);
        }

        // Assert - All version IDs should be unique
        Assert.Equal(5, versionIds.Distinct().Count());

        // All version IDs should not be null
        Assert.All(versionIds, id =>
        {
            Assert.NotNull(id);
            Assert.NotEqual("null", id);
        });
    }

    // Additional test: Verify ETag is returned for each put
    [Fact]
    public async Task PutObjectAsync_VersioningEnabled_ReturnsETag()
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
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "test content"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, putResponse.HttpStatusCode);
        Assert.NotNull(putResponse.ETag);
        Assert.NotEmpty(putResponse.ETag);
    }
}
