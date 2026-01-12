using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Delete Marker behavior.
/// Tests verify the properties and behavior of delete markers.
/// </summary>
public class DeleteMarkerBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public DeleteMarkerBehaviorAcceptanceTests()
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

    // Acceptance Criteria 8.1 - Scenario: Delete marker properties
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has been deleted creating a delete marker
    // When I call ListVersionsAsync with bucket "versioned-bucket"
    // Then the delete marker should appear in DeleteMarkers collection
    // And the delete marker should have Key, VersionId, IsLatest, LastModified, Owner
    // And the delete marker should NOT have ETag, Size, StorageClass
    [Fact]
    public async Task DeleteMarker_Properties_HasCorrectAttributes()
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

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        var deleteMarker = listResponse.DeleteMarkers.FirstOrDefault(dm => dm.Key == "file.txt");
        Assert.NotNull(deleteMarker);

        // Should have these properties
        Assert.Equal("file.txt", deleteMarker.Key);
        Assert.NotNull(deleteMarker.VersionId);
        Assert.True(deleteMarker.IsLatest);
        Assert.NotEqual(default, deleteMarker.LastModified);
        Assert.NotNull(deleteMarker.Owner);
    }

    // Acceptance Criteria 8.1 - Scenario: ListObjects does not return objects with delete marker as current version
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted-file.txt" current version is a delete marker
    // And object "active-file.txt" exists without delete marker
    // When I call ListObjectsAsync with bucket "versioned-bucket"
    // Then the response should contain "active-file.txt"
    // And the response should NOT contain "deleted-file.txt"
    [Fact]
    public async Task ListObjectsAsync_DoesNotReturnDeletedObjects()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create active file
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "active-file.txt",
            ContentBody = "active content"
        });

        // Create and delete another file
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "deleted-file.txt",
            ContentBody = "deleted content"
        });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act
        var listResponse = await _client.ListObjectsAsync(bucketName);

        // Assert
        Assert.Contains(listResponse.S3Objects, o => o.Key == "active-file.txt");
        Assert.DoesNotContain(listResponse.S3Objects, o => o.Key == "deleted-file.txt");
    }

    // Acceptance Criteria 8.1 - Scenario: Expired delete marker (only remaining version)
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has only a delete marker (all other versions deleted)
    // Then the delete marker is considered an "expired delete marker"
    [Fact]
    public async Task DeleteMarker_OnlyRemainingVersion_IsExpiredDeleteMarker()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete object
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Delete the actual version, leaving only the delete marker
        await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse.VersionId
        });

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        // Should only have delete marker, no versions
        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        var deleteMarkers = listResponse.DeleteMarkers.Where(dm => dm.Key == "file.txt").ToList();

        Assert.Empty(fileVersions);
        Assert.Single(deleteMarkers);
        Assert.True(deleteMarkers[0].IsLatest);
    }

    // Acceptance Criteria 8.1 - Scenario: Multiple delete markers for same object
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has been deleted twice without specifying VersionId
    // When I call ListVersionsAsync with bucket "versioned-bucket"
    // Then the response should contain two delete markers for "file.txt"
    // And each delete marker should have a unique VersionId
    // And only the most recent delete marker should have IsLatest set to true
    [Fact]
    public async Task DeleteMarker_MultipleDeleteMarkers_OnlyMostRecentIsLatest()
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

        // Delete twice to create two delete markers
        var deleteResponse1 = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteResponse2 = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        var deleteMarkers = listResponse.DeleteMarkers.Where(dm => dm.Key == "file.txt").ToList();

        Assert.Equal(2, deleteMarkers.Count);

        // Each should have unique VersionId
        Assert.NotEqual(deleteMarkers[0].VersionId, deleteMarkers[1].VersionId);

        // Only one should be latest
        Assert.Single(deleteMarkers.Where(dm => dm.IsLatest));

        // The second delete marker should be the latest
        var latestMarker = deleteMarkers.First(dm => dm.IsLatest);
        Assert.Equal(deleteResponse2.VersionId, latestMarker.VersionId);
    }

    // Additional test: Delete marker has correct LastModified timestamp
    [Fact]
    public async Task DeleteMarker_HasCorrectLastModifiedTimestamp()
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

        var beforeDelete = DateTime.UtcNow;
        await _client.DeleteObjectAsync(bucketName, "file.txt");
        var afterDelete = DateTime.UtcNow;

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        var deleteMarker = listResponse.DeleteMarkers.FirstOrDefault(dm => dm.Key == "file.txt");
        Assert.NotNull(deleteMarker);

        // LastModified should be between before and after delete
        Assert.True(deleteMarker.LastModified >= beforeDelete.AddSeconds(-1));
        Assert.True(deleteMarker.LastModified <= afterDelete.AddSeconds(1));
    }

    // Additional test: Delete marker can be identified in version listing
    [Fact]
    public async Task DeleteMarker_CanBeIdentifiedInVersionListing()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create multiple objects with versions and delete markers
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file1.txt", ContentBody = "c1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file2.txt", ContentBody = "c2" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file3.txt", ContentBody = "c3" });

        await _client.DeleteObjectAsync(bucketName, "file1.txt");
        await _client.DeleteObjectAsync(bucketName, "file3.txt");

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        // File1 and file3 should have delete markers
        Assert.Contains(listResponse.DeleteMarkers, dm => dm.Key == "file1.txt");
        Assert.Contains(listResponse.DeleteMarkers, dm => dm.Key == "file3.txt");

        // File2 should not have delete marker
        Assert.DoesNotContain(listResponse.DeleteMarkers, dm => dm.Key == "file2.txt");

        // All files should still have versions in Versions collection
        Assert.Contains(listResponse.Versions, v => v.Key == "file1.txt");
        Assert.Contains(listResponse.Versions, v => v.Key == "file2.txt");
        Assert.Contains(listResponse.Versions, v => v.Key == "file3.txt");
    }

    // Additional test: Delete marker key matches the deleted object key
    [Fact]
    public async Task DeleteMarker_KeyMatchesDeletedObjectKey()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var objectKey = "folder/subfolder/test-file.txt";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            ContentBody = "content"
        });

        await _client.DeleteObjectAsync(bucketName, objectKey);

        // Act
        var listResponse = await _client.ListVersionsAsync(bucketName);

        // Assert
        var deleteMarker = listResponse.DeleteMarkers.FirstOrDefault();
        Assert.NotNull(deleteMarker);
        Assert.Equal(objectKey, deleteMarker.Key);
    }
}
