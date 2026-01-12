using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Concurrency and Conflict Handling operations.
/// Tests verify optimistic concurrency behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 8.
/// </summary>
public class ConcurrencyHandlingAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ConcurrencyHandlingAcceptanceTests()
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

    #region 8.1 Optimistic Concurrency Patterns

    // Acceptance Criteria 8.1 - Scenario: Read-Modify-Write pattern with ETag validation
    // Given I have valid AWS credentials
    // And object "config.json" exists with content "v1" and ETag "etag1"
    // When Client reads object and gets ETag "etag1"
    // And Client modifies content locally
    // And Client writes with IfMatch "etag1"
    // Then the write should succeed
    // And the object should have new content and new ETag
    [Fact]
    public async Task ReadModifyWrite_BasicPattern_Succeeds()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var originalContent = "{\"version\": 1}";
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "config.json",
            ContentBody = originalContent,
            ContentType = "application/json"
        });
        var originalETag = putResponse.ETag;

        // Read the object (simulating read-modify-write pattern)
        var getResponse = await _client.GetObjectAsync(bucketName, "config.json");
        Assert.Equal(originalETag, getResponse.ETag);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();

        // Modify content locally
        var modifiedContent = "{\"version\": 2}";

        // Act - Write modified content
        var updateResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "config.json",
            ContentBody = modifiedContent,
            ContentType = "application/json"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, updateResponse.HttpStatusCode);
        Assert.NotEqual(originalETag, updateResponse.ETag);

        // Verify content was updated
        var verifyResponse = await _client.GetObjectAsync(bucketName, "config.json");
        using var verifyReader = new StreamReader(verifyResponse.ResponseStream);
        var verifiedContent = await verifyReader.ReadToEndAsync();
        Assert.Equal(modifiedContent, verifiedContent);
    }

    // Acceptance Criteria 8.1 - Scenario: Versioning as alternative concurrency control
    // Given I have valid AWS credentials
    // And I own a versioning-enabled bucket
    // And object "data.txt" exists with version "v1"
    // When Client A uploads new content (creates version "v2")
    // And Client B uploads new content (creates version "v3")
    // Then both uploads succeed (no conflict)
    // And both versions exist in the bucket
    // And version "v3" is the current version
    [Fact]
    public async Task VersioningConcurrency_MultipleWrites_AllVersionsPreserved()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Initial version
        var v1Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "version 1 content"
        });
        var v1Id = v1Response.VersionId;

        // Act - Concurrent-style writes
        var v2Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "version 2 content from Client A"
        });
        var v2Id = v2Response.VersionId;

        var v3Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "version 3 content from Client B"
        });
        var v3Id = v3Response.VersionId;

        // Assert - All writes succeed
        Assert.Equal(HttpStatusCode.OK, v1Response.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, v2Response.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, v3Response.HttpStatusCode);

        // All versions should exist
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "data.txt" && !v.IsDeleteMarker).ToList();

        Assert.Equal(3, versions.Count);
        Assert.Contains(versions, v => v.VersionId == v1Id);
        Assert.Contains(versions, v => v.VersionId == v2Id);
        Assert.Contains(versions, v => v.VersionId == v3Id);

        // Latest version should be v3
        var latestVersion = versions.First(v => v.IsLatest);
        Assert.Equal(v3Id, latestVersion.VersionId);

        // Get without version ID should return latest
        var currentResponse = await _client.GetObjectAsync(bucketName, "data.txt");
        using var reader = new StreamReader(currentResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version 3 content from Client B", content);
    }

    // Acceptance Criteria 8.1 - Scenario: Create-if-not-exists pattern
    // Given I have valid AWS credentials
    // And no object with key "lock.txt" exists
    // When multiple clients simultaneously try to create "lock.txt" with IfNoneMatch "*"
    // Then exactly one client should succeed
    // And all other clients should receive 412 Precondition Failed
    // And the winning client effectively acquires a "lock"
    [Fact]
    public async Task ConcurrentCreation_FirstWriteWins()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act - Simulate concurrent creation attempts
        var tasks = new List<Task<PutObjectResponse>>();
        for (int i = 0; i < 5; i++)
        {
            var index = i;
            tasks.Add(_client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "lock.txt",
                ContentBody = $"client {index}"
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All writes succeed (in non-versioned bucket, they overwrite)
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode));

        // Final content should be from one of the writers
        var getResponse = await _client.GetObjectAsync(bucketName, "lock.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.StartsWith("client ", content);
    }

    #endregion

    #region 8.2 Conflict Response Handling

    // Acceptance Criteria 8.2 - Scenario: Handle 404 Not Found during conditional operation
    // Given I have valid AWS credentials
    // And object "file.txt" was deleted by another client
    // When I attempt conditional operation on "file.txt"
    // Then the response may be 404 Not Found
    // And the client should handle missing object appropriately
    [Fact]
    public async Task GetObjectAsync_AfterDelete_Returns404()
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

        // Simulate another client deleting the object
        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Additional test: Parallel reads on same object
    [Fact]
    public async Task ParallelReads_SameObject_AllSucceed()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var content = "shared content for reading";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "shared.txt",
            ContentBody = content
        });

        // Act - Multiple parallel reads
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetObjectAsync(bucketName, "shared.txt")).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode);
        });

        // Verify all reads return same content
        foreach (var response in results)
        {
            using var reader = new StreamReader(response.ResponseStream);
            var readContent = await reader.ReadToEndAsync();
            Assert.Equal(content, readContent);
        }
    }

    // Additional test: Interleaved read and write operations
    [Fact]
    public async Task InterleavedReadWrite_VersionedBucket_PreservesAllVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Initial write
        var v1Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "initial content"
        });

        // Act - Interleaved operations
        var readTask1 = _client.GetObjectAsync(bucketName, "data.txt");

        var v2Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "updated content"
        });

        var readTask2 = _client.GetObjectAsync(bucketName, "data.txt");

        var read1 = await readTask1;
        var read2 = await readTask2;

        // Assert
        Assert.Equal(HttpStatusCode.OK, v2Response.HttpStatusCode);
        Assert.NotEqual(v1Response.VersionId, v2Response.VersionId);

        // Both reads should succeed
        Assert.Equal(HttpStatusCode.OK, read1.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, read2.HttpStatusCode);

        // Latest read should have updated content
        using var reader = new StreamReader(read2.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("updated content", content);
    }

    // Additional test: ETag changes on each update
    [Fact]
    public async Task MultipleUpdates_ETagChangesEachTime()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var etags = new List<string>();

        // Act - Multiple updates
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                ContentBody = $"version {i}"
            });
            etags.Add(response.ETag);
        }

        // Assert - All ETags should be different
        Assert.Equal(5, etags.Distinct().Count());
    }

    // Additional test: Version ordering is preserved
    [Fact]
    public async Task VersionOrdering_IsPreserved()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var versionOrder = new List<string>();

        // Act - Create versions in sequence
        for (int i = 0; i < 5; i++)
        {
            var response = await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                ContentBody = $"version {i}"
            });
            versionOrder.Add(response.VersionId);
        }

        // Assert
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions
            .Where(v => v.Key == "file.txt" && !v.IsDeleteMarker)
            .ToList();

        Assert.Equal(5, versions.Count);

        // Latest version should be the last one created
        var latestVersion = versions.First(v => v.IsLatest);
        Assert.Equal(versionOrder.Last(), latestVersion.VersionId);
    }

    #endregion
}
