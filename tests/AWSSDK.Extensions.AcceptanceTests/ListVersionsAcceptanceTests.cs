using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for ListVersions/ListObjectVersions operations.
/// Tests verify the behavior of listing object versions in a bucket.
/// </summary>
public class ListVersionsAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ListVersionsAcceptanceTests()
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

    // Acceptance Criteria 3.1 - Scenario: List versions in a versioning-enabled bucket with multiple object versions
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains object "file.txt" with versions "v1", "v2", "v3"
    // When I call ListVersionsAsync with bucket name "versioned-bucket"
    // Then the response should have HTTP status code 200
    // And the response should contain 3 versions for key "file.txt"
    // And each version should have a unique VersionId
    // And each version should have an IsLatest property
    // And only one version should have IsLatest set to true
    // And versions should be ordered with the latest first
    [Fact]
    public async Task ListVersionsAsync_MultipleVersions_ReturnsAllVersionsWithCorrectProperties()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create 3 versions
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "v1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "v2" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "v3" });

        // Act
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var versions = response.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.Equal(3, versions.Count);

        // Each version should have a unique VersionId
        var versionIds = versions.Select(v => v.VersionId).Distinct().ToList();
        Assert.Equal(3, versionIds.Count);

        // Only one version should have IsLatest set to true
        Assert.Single(versions.Where(v => v.IsLatest));
    }

    // Acceptance Criteria 3.1 - Scenario: List versions in a bucket with delete markers
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And object "deleted-file.txt" has been deleted (has a delete marker as current version)
    // When I call ListVersionsAsync with bucket name "versioned-bucket"
    // Then the response should contain DeleteMarkers collection
    // And the delete marker for "deleted-file.txt" should have IsLatest set to true
    // And the delete marker should have a VersionId
    [Fact]
    public async Task ListVersionsAsync_WithDeleteMarkers_ReturnsDeleteMarkersCollection()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create and delete an object
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "deleted-file.txt", ContentBody = "content" });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        // Act
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.DeleteMarkers);

        var deleteMarker = response.DeleteMarkers.FirstOrDefault(dm => dm.Key == "deleted-file.txt");
        Assert.NotNull(deleteMarker);
        Assert.True(deleteMarker.IsLatest);
        Assert.NotNull(deleteMarker.VersionId);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions with prefix filter
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains objects "folder1/file1.txt", "folder1/file2.txt", "folder2/file3.txt"
    // When I call ListVersionsAsync with bucket name "versioned-bucket" and Prefix "folder1/"
    // Then the response should only contain versions for keys starting with "folder1/"
    // And the response should not contain versions for "folder2/file3.txt"
    [Fact]
    public async Task ListVersionsAsync_WithPrefixFilter_ReturnsOnlyMatchingVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder1/file1.txt", ContentBody = "c1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder1/file2.txt", ContentBody = "c2" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder2/file3.txt", ContentBody = "c3" });

        // Act
        var response = await _client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucketName,
            Prefix = "folder1/"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.All(response.Versions, v => Assert.StartsWith("folder1/", v.Key));
        Assert.DoesNotContain(response.Versions, v => v.Key.StartsWith("folder2/"));
    }

    // Acceptance Criteria 3.1 - Scenario: List versions with delimiter for folder-like structure
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains objects "folder1/file1.txt", "folder1/file2.txt", "folder2/file3.txt"
    // When I call ListVersionsAsync with bucket name "versioned-bucket" and Delimiter "/"
    // Then the response should contain CommonPrefixes for "folder1/" and "folder2/"
    [Fact]
    public async Task ListVersionsAsync_WithDelimiter_ReturnsCommonPrefixes()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder1/file1.txt", ContentBody = "c1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder1/file2.txt", ContentBody = "c2" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "folder2/file3.txt", ContentBody = "c3" });

        // Act
        var response = await _client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucketName,
            Delimiter = "/"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotNull(response.CommonPrefixes);
        Assert.Contains("folder1/", response.CommonPrefixes);
        Assert.Contains("folder2/", response.CommonPrefixes);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions with pagination using MaxKeys
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains more than 100 object versions
    // When I call ListVersionsAsync with bucket name "versioned-bucket" and MaxKeys 50
    // Then the response should contain at most 50 versions
    // And the response IsTruncated should be true
    // And the response should contain NextKeyMarker
    // And the response should contain NextVersionIdMarker
    [Fact]
    public async Task ListVersionsAsync_WithMaxKeys_ReturnsPaginatedResults()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create 60 objects
        for (int i = 0; i < 60; i++)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = $"file{i:D3}.txt",
                ContentBody = $"content{i}"
            });
        }

        // Act
        var response = await _client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucketName,
            MaxKeys = 50
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.True(response.Versions.Count <= 50);
        Assert.True(response.IsTruncated);
        Assert.NotNull(response.NextKeyMarker);
        Assert.NotNull(response.NextVersionIdMarker);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions with pagination continuation
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains more than 100 object versions
    // And I have the NextKeyMarker and NextVersionIdMarker from a previous response
    // When I call ListVersionsAsync with KeyMarker and VersionIdMarker set to the previous values
    // Then the response should contain the next page of versions
    // And the response should not duplicate versions from the previous page
    [Fact]
    public async Task ListVersionsAsync_WithPaginationContinuation_ReturnsNextPageWithoutDuplicates()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create 60 objects
        for (int i = 0; i < 60; i++)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = $"file{i:D3}.txt",
                ContentBody = $"content{i}"
            });
        }

        // Get first page
        var firstPage = await _client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucketName,
            MaxKeys = 30
        });

        // Act - Get second page
        var secondPage = await _client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucketName,
            MaxKeys = 30,
            KeyMarker = firstPage.NextKeyMarker,
            VersionIdMarker = firstPage.NextVersionIdMarker
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondPage.HttpStatusCode);

        // No duplicate version IDs between pages
        var firstPageIds = firstPage.Versions.Select(v => v.VersionId).ToHashSet();
        var secondPageIds = secondPage.Versions.Select(v => v.VersionId).ToHashSet();
        Assert.Empty(firstPageIds.Intersect(secondPageIds));
    }

    // Acceptance Criteria 3.1 - Scenario: List versions in a non-versioned bucket
    // Given I have valid AWS credentials
    // And I own a bucket "non-versioned-bucket" without versioning enabled
    // And the bucket contains object "file.txt"
    // When I call ListVersionsAsync with bucket name "non-versioned-bucket"
    // Then the response should have HTTP status code 200
    // And the object "file.txt" should have VersionId of "null"
    [Fact]
    public async Task ListVersionsAsync_NonVersionedBucket_ReturnsNullVersionId()
    {
        // Arrange
        var bucketName = "non-versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "content" });

        // Act
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        var version = response.Versions.FirstOrDefault(v => v.Key == "file.txt");
        Assert.NotNull(version);
        Assert.Equal("null", version.VersionId);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions in a versioning-suspended bucket
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // And the bucket contains objects uploaded before and after suspension
    // When I call ListVersionsAsync with bucket name "suspended-bucket"
    // Then the response should contain versions with unique VersionIds (uploaded when enabled)
    // And the response should contain versions with null VersionId (uploaded when suspended)
    [Fact]
    public async Task ListVersionsAsync_SuspendedBucket_ReturnsVersionedAndNullVersions()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Upload with versioning enabled
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "versioned-file.txt", ContentBody = "v1" });

        // Suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Upload with versioning suspended
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "suspended-file.txt", ContentBody = "s1" });

        // Act
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var versionedFile = response.Versions.FirstOrDefault(v => v.Key == "versioned-file.txt");
        var suspendedFile = response.Versions.FirstOrDefault(v => v.Key == "suspended-file.txt");

        Assert.NotNull(versionedFile);
        Assert.NotEqual("null", versionedFile.VersionId);

        Assert.NotNull(suspendedFile);
        Assert.Equal("null", suspendedFile.VersionId);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions for empty bucket
    // Given I have valid AWS credentials
    // And I own an empty versioning-enabled bucket "empty-bucket"
    // When I call ListVersionsAsync with bucket name "empty-bucket"
    // Then the response should have HTTP status code 200
    // And the Versions collection should be empty
    // And the DeleteMarkers collection should be empty
    [Fact]
    public async Task ListVersionsAsync_EmptyBucket_ReturnsEmptyCollections()
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
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Empty(response.Versions);
        Assert.Empty(response.DeleteMarkers);
    }

    // Acceptance Criteria 3.1 - Scenario: Fail to list versions on non-existent bucket
    // Given I have valid AWS credentials
    // And no bucket named "non-existent-bucket" exists
    // When I call ListVersionsAsync with bucket name "non-existent-bucket"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchBucket"
    // And the HTTP status code should be 404
    [Fact]
    public async Task ListVersionsAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Arrange & Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.ListVersionsAsync("non-existent-bucket"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 3.1 - Scenario: List versions response structure validation
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket "versioned-bucket"
    // And the bucket contains object "file.txt" with multiple versions
    // When I call ListVersionsAsync with bucket name "versioned-bucket"
    // Then each S3ObjectVersion should contain Key, VersionId, IsLatest, LastModified, ETag, Size, StorageClass, Owner
    [Fact]
    public async Task ListVersionsAsync_ResponseStructure_ContainsAllRequiredProperties()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file.txt", ContentBody = "version2" });

        // Act
        var response = await _client.ListVersionsAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.NotEmpty(response.Versions);

        foreach (var version in response.Versions)
        {
            Assert.NotNull(version.Key);
            Assert.NotNull(version.VersionId);
            Assert.NotEqual(default, version.LastModified);
            Assert.NotNull(version.ETag);
            Assert.True(version.Size >= 0);
        }
    }
}
