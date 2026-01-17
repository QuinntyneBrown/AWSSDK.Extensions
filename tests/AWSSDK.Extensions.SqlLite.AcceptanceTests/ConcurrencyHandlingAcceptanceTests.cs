using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Concurrency and Conflict Handling operations using SqlLiteS3Client.
/// Tests verify optimistic concurrency behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 8.
/// </summary>
public class ConcurrencyHandlingAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ConcurrencyHandlingAcceptanceTests()
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

    #region 8.1 Optimistic Concurrency Patterns

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

        var getResponse = await _client.GetObjectAsync(bucketName, "config.json");
        Assert.Equal(originalETag, getResponse.ETag);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();

        var modifiedContent = "{\"version\": 2}";

        // Act
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

        var verifyResponse = await _client.GetObjectAsync(bucketName, "config.json");
        using var verifyReader = new StreamReader(verifyResponse.ResponseStream);
        var verifiedContent = await verifyReader.ReadToEndAsync();
        Assert.Equal(modifiedContent, verifiedContent);
    }

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

        var v1Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "version 1 content"
        });
        var v1Id = v1Response.VersionId;

        // Act
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

        // Assert
        Assert.Equal(HttpStatusCode.OK, v1Response.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, v2Response.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, v3Response.HttpStatusCode);

        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "data.txt" && !v.IsDeleteMarker).ToList();

        Assert.Equal(3, versions.Count);
        Assert.Contains(versions, v => v.VersionId == v1Id);
        Assert.Contains(versions, v => v.VersionId == v2Id);
        Assert.Contains(versions, v => v.VersionId == v3Id);

        var latestVersion = versions.First(v => v.IsLatest);
        Assert.Equal(v3Id, latestVersion.VersionId);

        var currentResponse = await _client.GetObjectAsync(bucketName, "data.txt");
        using var reader = new StreamReader(currentResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version 3 content from Client B", content);
    }

    [Fact]
    public async Task ConcurrentCreation_FirstWriteWins()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
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

        // Assert
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode));

        var getResponse = await _client.GetObjectAsync(bucketName, "lock.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.StartsWith("client ", content);
    }

    #endregion

    #region 8.2 Conflict Response Handling

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

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

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

        // Act
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetObjectAsync(bucketName, "shared.txt")).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r =>
        {
            Assert.Equal(HttpStatusCode.OK, r.HttpStatusCode);
        });

        foreach (var response in results)
        {
            using var reader = new StreamReader(response.ResponseStream);
            var readContent = await reader.ReadToEndAsync();
            Assert.Equal(content, readContent);
        }
    }

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

        var v1Response = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "data.txt",
            ContentBody = "initial content"
        });

        // Act
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

        Assert.Equal(HttpStatusCode.OK, read1.HttpStatusCode);
        Assert.Equal(HttpStatusCode.OK, read2.HttpStatusCode);

        using var reader = new StreamReader(read2.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("updated content", content);
    }

    [Fact]
    public async Task MultipleUpdates_ETagChangesEachTime()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var etags = new List<string>();

        // Act
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

        // Assert
        Assert.Equal(5, etags.Distinct().Count());
    }

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

        // Act
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

        var latestVersion = versions.First(v => v.IsLatest);
        Assert.Equal(versionOrder.Last(), latestVersion.VersionId);
    }

    #endregion
}
