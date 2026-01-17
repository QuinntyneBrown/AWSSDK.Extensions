using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for CopyObject operations with versioning using SqlLiteS3Client.
/// Tests verify the behavior of copying specific object versions.
/// </summary>
public class CopyObjectWithVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public CopyObjectWithVersioningAcceptanceTests()
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

    [Fact]
    public async Task CopyObjectAsync_SpecificVersion_CopiesToNewKey()
    {
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
            Key = "source.txt",
            ContentBody = "version1 content"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "version2 content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            SourceVersionId = putResponse1.VersionId,
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.VersionId);
        Assert.NotEqual(putResponse1.VersionId, copyResponse.VersionId);

        var getResponse = await _client.GetObjectAsync(bucketName, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version1 content", content);
    }

    [Fact]
    public async Task CopyObjectAsync_WithoutVersionId_CopiesCurrentVersion()
    {
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
            Key = "source.txt",
            ContentBody = "version1 content"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "source.txt",
            ContentBody = "version2 content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);

        var getResponse = await _client.GetObjectAsync(bucketName, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("version2 content", content);
    }

    [Fact]
    public async Task CopyObjectAsync_ExistingDestination_CreatesNewVersion()
    {
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
            Key = "source.txt",
            ContentBody = "source content"
        });

        var destPutResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "dest.txt",
            ContentBody = "original dest content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotEqual(destPutResponse.VersionId, copyResponse.VersionId);

        var listResponse = await _client.ListVersionsAsync(bucketName);
        var destVersions = listResponse.Versions.Where(v => v.Key == "dest.txt").ToList();
        Assert.Equal(2, destVersions.Count);

        var currentVersion = destVersions.First(v => v.IsLatest);
        Assert.Equal(copyResponse.VersionId, currentVersion.VersionId);
    }

    [Fact]
    public async Task CopyObjectAsync_ToNonVersionedBucket_HasNullVersionId()
    {
        var sourceBucket = "source-bucket";
        var destBucket = "dest-bucket";

        await _client.PutBucketAsync(sourceBucket);
        await _client.PutBucketAsync(destBucket);

        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = sourceBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = sourceBucket,
            Key = "source.txt",
            ContentBody = "source content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = "source.txt",
            SourceVersionId = putResponse.VersionId,
            DestinationBucket = destBucket,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(copyResponse.VersionId) || copyResponse.VersionId == "null");

        var getResponse = await _client.GetObjectAsync(destBucket, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("source content", content);
    }

    [Fact]
    public async Task CopyObjectAsync_DeleteMarkerAsCurrent_ThrowsNoSuchKeyException()
    {
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
            Key = "deleted.txt",
            ContentBody = "content"
        });
        await _client.DeleteObjectAsync(bucketName, "deleted.txt");

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = "deleted.txt",
                DestinationBucket = bucketName,
                DestinationKey = "dest.txt"
            }));

        Assert.Equal("NoSuchKey", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task CopyObjectAsync_PreservesMetadataFromSourceVersion()
    {
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
            Key = "source.txt",
            ContentBody = "test content",
            ContentType = "text/plain",
            Metadata = { ["custom-key"] = "custom-value" }
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);

        var metadata = await _client.GetObjectMetadataAsync(bucketName, "dest.txt");
        Assert.Equal("text/plain", metadata.Headers.ContentType);
    }

    [Fact]
    public async Task CopyObjectAsync_BetweenVersionedBuckets_CreatesNewVersion()
    {
        var sourceBucket = "source-bucket";
        var destBucket = "dest-bucket";

        await _client.PutBucketAsync(sourceBucket);
        await _client.PutBucketAsync(destBucket);

        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = sourceBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = destBucket,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = sourceBucket,
            Key = "source.txt",
            ContentBody = "source content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = "source.txt",
            SourceVersionId = putResponse.VersionId,
            DestinationBucket = destBucket,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.VersionId);
        Assert.NotEqual("null", copyResponse.VersionId);

        var getResponse = await _client.GetObjectAsync(destBucket, "dest.txt");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("source content", content);
    }

    [Fact]
    public async Task CopyObjectAsync_ReturnsETag()
    {
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
            Key = "source.txt",
            ContentBody = "content"
        });

        var copyResponse = await _client.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucketName,
            SourceKey = "source.txt",
            DestinationBucket = bucketName,
            DestinationKey = "dest.txt"
        });

        Assert.Equal(HttpStatusCode.OK, copyResponse.HttpStatusCode);
        Assert.NotNull(copyResponse.ETag);
        Assert.NotEmpty(copyResponse.ETag);
    }
}
