using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for PutObject versioning behavior using SqlLiteS3Client.
/// Tests verify how PutObject behaves with different versioning configurations.
/// </summary>
public class PutObjectVersioningBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public PutObjectVersioningBehaviorAcceptanceTests()
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

    // Acceptance Criteria 6.1 - Scenario: Put object in versioning-enabled bucket creates new version
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
