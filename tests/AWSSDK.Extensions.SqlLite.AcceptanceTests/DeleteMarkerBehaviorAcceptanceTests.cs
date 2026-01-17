using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Delete Marker behavior using SqlLiteS3Client.
/// Tests verify the properties and behavior of delete markers.
/// </summary>
public class DeleteMarkerBehaviorAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public DeleteMarkerBehaviorAcceptanceTests()
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

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_Properties_HasCorrectAttributes()
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
            Key = "file.txt",
            ContentBody = "content"
        });

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        var listResponse = await _client.ListVersionsAsync(bucketName);

        var deleteMarker = listResponse.Versions.FirstOrDefault(v => v.Key == "file.txt" && v.IsDeleteMarker);
        Assert.NotNull(deleteMarker);

        Assert.Equal("file.txt", deleteMarker.Key);
        Assert.NotNull(deleteMarker.VersionId);
        Assert.True(deleteMarker.IsLatest);
        Assert.NotEqual(default, deleteMarker.LastModified);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task ListObjectsAsync_DoesNotReturnDeletedObjects()
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
            Key = "active-file.txt",
            ContentBody = "active content"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "deleted-file.txt",
            ContentBody = "deleted content"
        });
        await _client.DeleteObjectAsync(bucketName, "deleted-file.txt");

        var listResponse = await _client.ListObjectsAsync(bucketName);

        Assert.Contains(listResponse.S3Objects, o => o.Key == "active-file.txt");
        Assert.DoesNotContain(listResponse.S3Objects, o => o.Key == "deleted-file.txt");
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_OnlyRemainingVersion_IsExpiredDeleteMarker()
    {
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

        await _client.DeleteObjectAsync(bucketName, "file.txt");

        await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse.VersionId
        });

        var listResponse = await _client.ListVersionsAsync(bucketName);

        var fileVersions = listResponse.Versions.Where(v => v.Key == "file.txt" && !v.IsDeleteMarker).ToList();
        var deleteMarkers = listResponse.Versions.Where(v => v.Key == "file.txt" && v.IsDeleteMarker).ToList();

        Assert.Empty(fileVersions);
        Assert.Single(deleteMarkers);
        Assert.True(deleteMarkers[0].IsLatest);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_MultipleDeleteMarkers_OnlyMostRecentIsLatest()
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
            Key = "file.txt",
            ContentBody = "content"
        });

        var deleteResponse1 = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteResponse2 = await _client.DeleteObjectAsync(bucketName, "file.txt");

        var listResponse = await _client.ListVersionsAsync(bucketName);

        var deleteMarkers = listResponse.Versions.Where(v => v.Key == "file.txt" && v.IsDeleteMarker).ToList();

        Assert.Equal(2, deleteMarkers.Count);
        Assert.NotEqual(deleteMarkers[0].VersionId, deleteMarkers[1].VersionId);
        Assert.Single(deleteMarkers.Where(dm => dm.IsLatest));

        var latestMarker = deleteMarkers.First(dm => dm.IsLatest);
        Assert.Equal(deleteResponse2.VersionId, latestMarker.VersionId);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_HasCorrectLastModifiedTimestamp()
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
            Key = "file.txt",
            ContentBody = "content"
        });

        var beforeDelete = DateTime.UtcNow;
        await _client.DeleteObjectAsync(bucketName, "file.txt");
        var afterDelete = DateTime.UtcNow;

        var listResponse = await _client.ListVersionsAsync(bucketName);

        var deleteMarker = listResponse.Versions.FirstOrDefault(v => v.Key == "file.txt" && v.IsDeleteMarker);
        Assert.NotNull(deleteMarker);

        Assert.True(deleteMarker.LastModified >= beforeDelete.AddSeconds(-1));
        Assert.True(deleteMarker.LastModified <= afterDelete.AddSeconds(1));
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_CanBeIdentifiedInVersionListing()
    {
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file1.txt", ContentBody = "c1" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file2.txt", ContentBody = "c2" });
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = bucketName, Key = "file3.txt", ContentBody = "c3" });

        await _client.DeleteObjectAsync(bucketName, "file1.txt");
        await _client.DeleteObjectAsync(bucketName, "file3.txt");

        var listResponse = await _client.ListVersionsAsync(bucketName);

        Assert.Contains(listResponse.Versions, v => v.Key == "file1.txt" && v.IsDeleteMarker);
        Assert.Contains(listResponse.Versions, v => v.Key == "file3.txt" && v.IsDeleteMarker);
        Assert.DoesNotContain(listResponse.Versions, v => v.Key == "file2.txt" && v.IsDeleteMarker);

        Assert.Contains(listResponse.Versions, v => v.Key == "file1.txt" && !v.IsDeleteMarker);
        Assert.Contains(listResponse.Versions, v => v.Key == "file2.txt" && !v.IsDeleteMarker);
        Assert.Contains(listResponse.Versions, v => v.Key == "file3.txt" && !v.IsDeleteMarker);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task DeleteMarker_KeyMatchesDeletedObjectKey()
    {
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

        var listResponse = await _client.ListVersionsAsync(bucketName);

        var deleteMarker = listResponse.Versions.FirstOrDefault(v => v.IsDeleteMarker);
        Assert.NotNull(deleteMarker);
        Assert.Equal(objectKey, deleteMarker.Key);
    }
}
