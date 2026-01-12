using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for DeleteObject versioning behavior.
/// Tests verify how DeleteObject behaves with different versioning configurations.
/// </summary>
public class DeleteObjectVersioningBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public DeleteObjectVersioningBehaviorAcceptanceTests()
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

    // Acceptance Criteria 7.1 - Scenario: Delete object in versioning-enabled bucket creates delete marker (simple delete)
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" exists with version "v1"
    // When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 204
    // And the response should contain a new VersionId for the delete marker
    // And the response header x-amz-delete-marker should be "true"
    // And version "v1" should still exist as a noncurrent version
    // And GetObject without VersionId should return 404 NoSuchKey
    [Fact]
    public async Task DeleteObjectAsync_VersioningEnabled_CreatesDeleteMarker()
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
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.NotNull(deleteResponse.VersionId);
        Assert.True(deleteResponse.DeleteMarker);

        // Version v1 should still exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        Assert.Contains(listResponse.Versions, v => v.VersionId == putResponse.VersionId);

        // GetObject without VersionId should fail
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete specific version permanently removes that version
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" and "v2"
    // When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    // Then the response should have HTTP status code 204
    // And the response header x-amz-version-id should be "v1"
    // And version "v1" should be permanently deleted
    // And version "v2" should still exist
    [Fact]
    public async Task DeleteObjectAsync_SpecificVersion_PermanentlyDeletesVersion()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
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

        // Act - Delete specific version
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse1.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.Equal(putResponse1.VersionId, deleteResponse.VersionId);

        // v1 should be gone, v2 should still exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();

        Assert.DoesNotContain(fileVersions, v => v.VersionId == putResponse1.VersionId);
        Assert.Contains(fileVersions, v => v.VersionId == putResponse2.VersionId);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete the current (latest) version makes previous version current
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has versions "v1" (noncurrent) and "v2" (current)
    // When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v2"
    // Then the response should have HTTP status code 204
    // And version "v2" should be permanently deleted
    // And version "v1" should become the current version
    [Fact]
    public async Task DeleteObjectAsync_CurrentVersion_MakesPreviousVersionCurrent()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse1 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "v1 content"
        });
        var putResponse2 = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "v2 content"
        });

        // Act - Delete the current (latest) version
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse2.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // v1 should now be current
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(putResponse1.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("v1 content", content);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete a delete marker permanently removes it
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" has a delete marker "dm-123" as current version
    // And there is a previous version "v1"
    // When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    // Then the response should have HTTP status code 204
    // And the response header x-amz-delete-marker should be "true"
    // And the delete marker should be removed
    // And version "v1" should become the current version
    // And GetObject without VersionId should succeed and return version "v1"
    [Fact]
    public async Task DeleteObjectAsync_DeleteMarker_RemovesDeleteMarkerAndRestoresAccess()
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
            ContentBody = "original content"
        });

        // Create delete marker
        var deleteMarkerResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act - Delete the delete marker
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = deleteMarkerResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.True(deleteResponse.DeleteMarker);

        // Object should now be accessible again
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);
        Assert.Equal(putResponse.VersionId, getResponse.VersionId);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("original content", content);
    }

    // Acceptance Criteria 7.1 - Scenario: Simple delete when current version is already a delete marker creates another delete marker
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" current version is a delete marker "dm-1"
    // When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 204
    // And a new delete marker "dm-2" should be created
    // And both delete markers should exist in version history
    [Fact]
    public async Task DeleteObjectAsync_AlreadyDeleteMarker_CreatesAnotherDeleteMarker()
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

        // Create first delete marker
        var deleteMarker1 = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act - Create second delete marker
        var deleteMarker2 = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteMarker2.HttpStatusCode);
        Assert.True(deleteMarker2.DeleteMarker);
        Assert.NotEqual(deleteMarker1.VersionId, deleteMarker2.VersionId);

        // Both delete markers should exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var deleteMarkers = listResponse.DeleteMarkers.Where(dm => dm.Key == "file.txt").ToList();
        Assert.Equal(2, deleteMarkers.Count);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object in non-versioned bucket permanently removes object
    // Given I have valid AWS credentials
    // And I own a bucket "non-versioned-bucket" without versioning enabled
    // And object "file.txt" exists
    // When I call DeleteObjectAsync with bucket "non-versioned-bucket" and key "file.txt"
    // Then the response should have HTTP status code 204
    // And the object "file.txt" should be permanently deleted
    // And GetObject should return 404 NoSuchKey
    [Fact]
    public async Task DeleteObjectAsync_NonVersionedBucket_PermanentlyDeletesObject()
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
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object in versioning-suspended bucket - removes null version and creates delete marker
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // And object "file.txt" exists with null VersionId
    // When I call DeleteObjectAsync with bucket "suspended-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 204
    // And the null version should be removed
    // And a delete marker with null VersionId should be created
    [Fact]
    public async Task DeleteObjectAsync_VersioningSuspended_RemovesNullVersionAndCreatesDeleteMarker()
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

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.True(deleteResponse.DeleteMarker);
        Assert.True(string.IsNullOrEmpty(deleteResponse.VersionId) || deleteResponse.VersionId == "null");

        // Verify delete marker exists with null version
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var deleteMarker = listResponse.DeleteMarkers.FirstOrDefault(dm => dm.Key == "file.txt");
        Assert.NotNull(deleteMarker);
        Assert.Equal("null", deleteMarker.VersionId);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object in versioning-suspended bucket - no null version exists
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // And object "file.txt" only has versioned objects "v1", "v2" (no null version)
    // When I call DeleteObjectAsync with bucket "suspended-bucket" and key "file.txt" without VersionId
    // Then the response should have HTTP status code 204
    // And a delete marker with null VersionId should be created
    // And versions "v1" and "v2" should still exist
    [Fact]
    public async Task DeleteObjectAsync_VersioningSuspended_NoNullVersion_CreatesDeleteMarkerPreservesVersions()
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

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.True(deleteResponse.DeleteMarker);

        // Verify versions still exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        Assert.Contains(listResponse.Versions, v => v.Key == "file.txt" && v.VersionId == putResponse1.VersionId);
        Assert.Contains(listResponse.Versions, v => v.Key == "file.txt" && v.VersionId == putResponse2.VersionId);

        // Verify delete marker with null version exists
        var deleteMarker = listResponse.DeleteMarkers.FirstOrDefault(dm => dm.Key == "file.txt");
        Assert.NotNull(deleteMarker);
        Assert.Equal("null", deleteMarker.VersionId);
    }

    // Acceptance Criteria 7.1 - Scenario: Delete object with non-existent VersionId
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "file.txt" exists with version "v1"
    // When I call DeleteObjectAsync with VersionId "non-existent-version"
    // Then the response should have HTTP status code 204
    // And no object should be deleted (operation is idempotent)
    [Fact]
    public async Task DeleteObjectAsync_NonExistentVersionId_IsIdempotent()
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
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = "non-existent-version"
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // Original version should still exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        Assert.Contains(listResponse.Versions, v => v.VersionId == putResponse.VersionId);
    }
}
