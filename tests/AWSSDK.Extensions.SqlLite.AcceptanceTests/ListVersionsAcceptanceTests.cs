using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for ListVersions operations using SqlLiteS3Client.
/// Tests verify the behavior of listing object versions in a bucket.
/// </summary>
public class ListVersionsAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ListVersionsAcceptanceTests()
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

    // Acceptance Criteria 5.1 - Scenario: List all versions in a versioned bucket
    [Fact]
    public async Task ListVersionsAsync_VersionedBucket_ReturnsAllVersions()
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

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Equal(bucketName, listResponse.Name);

        var versions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.Equal(2, versions.Count);
        Assert.Contains(versions, v => v.VersionId == putResponse1.VersionId);
        Assert.Contains(versions, v => v.VersionId == putResponse2.VersionId);

        // Only one should be marked as latest
        var latestVersions = versions.Where(v => v.IsLatest).ToList();
        Assert.Single(latestVersions);
        Assert.Equal(putResponse2.VersionId, latestVersions[0].VersionId);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions includes delete markers
    [Fact]
    public async Task ListVersionsAsync_IncludesDeleteMarkers()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create object and then delete it
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);

        var fileEntries = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.Equal(2, fileEntries.Count);

        // One should be delete marker
        var deleteMarkers = fileEntries.Where(v => v.IsDeleteMarker).ToList();
        Assert.Single(deleteMarkers);
        Assert.True(deleteMarkers[0].IsLatest);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions with prefix filter
    [Fact]
    public async Task ListVersionsAsync_WithPrefix_FiltersResults()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create objects with different prefixes
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "folder1/file1.txt",
            ContentBody = "content1"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "folder1/file2.txt",
            ContentBody = "content2"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "folder2/file3.txt",
            ContentBody = "content3"
        });

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName, "folder1/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Equal(2, listResponse.Versions.Count);
        Assert.All(listResponse.Versions, v => Assert.StartsWith("folder1/", v.Key));
    }

    // Acceptance Criteria 5.1 - Scenario: List versions in non-existent bucket
    [Fact]
    public async Task ListVersionsAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.ListVersionsAsync("non-existent-bucket"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions in empty bucket
    [Fact]
    public async Task ListVersionsAsync_EmptyBucket_ReturnsEmptyList()
    {
        // Arrange
        var bucketName = "empty-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Empty(listResponse.Versions);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions returns versions sorted by key then date
    [Fact]
    public async Task ListVersionsAsync_ReturnsSortedByKeyThenDate()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create objects
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "b-file.txt",
            ContentBody = "b-content"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "a-file.txt",
            ContentBody = "a-content"
        });

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert - Should be sorted by key alphabetically
        Assert.Equal("a-file.txt", listResponse.Versions[0].Key);
        Assert.Equal("b-file.txt", listResponse.Versions[1].Key);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions returns version metadata
    [Fact]
    public async Task ListVersionsAsync_ReturnsVersionMetadata()
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
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Single(listResponse.Versions);

        var version = listResponse.Versions[0];
        Assert.Equal("file.txt", version.Key);
        Assert.Equal(putResponse.VersionId, version.VersionId);
        Assert.True(version.IsLatest);
        Assert.NotNull(version.ETag);
        Assert.True(version.Size > 0);
        Assert.True(version.LastModified > DateTime.MinValue);
    }

    // Acceptance Criteria 5.1 - Scenario: List versions in non-versioned bucket
    [Fact]
    public async Task ListVersionsAsync_NonVersionedBucket_ReturnsNullVersions()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Single(listResponse.Versions);
        Assert.Equal("null", listResponse.Versions[0].VersionId);
    }

    // Acceptance Criteria 5.1 - Scenario: List multiple versions of same object
    [Fact]
    public async Task ListVersionsAsync_MultipleVersionsSameObject_ReturnsAllVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create 5 versions of the same object
        var versionIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                ContentBody = $"version {i}"
            });
            versionIds.Add(response.VersionId);
        }

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, listResponse.HttpStatusCode);
        Assert.Equal(5, listResponse.Versions.Count);

        // All version IDs should be present
        foreach (var versionId in versionIds)
        {
            Assert.Contains(listResponse.Versions, v => v.VersionId == versionId);
        }

        // Only latest should be marked as IsLatest
        var latestVersions = listResponse.Versions.Where(v => v.IsLatest).ToList();
        Assert.Single(latestVersions);
        Assert.Equal(versionIds.Last(), latestVersions[0].VersionId);
    }
}
