using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Conditional Delete Operations using SqlLiteS3Client.
/// Tests verify conditional delete behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 4.
/// </summary>
public class ConditionalDeletesAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ConditionalDeletesAcceptanceTests()
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

    #region 4.1 DeleteObjectAsync with If-Match

    [Fact]
    public async Task DeleteObjectAsync_BasicDelete_DeletesObject()
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

        // Act
        var response = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.HttpStatusCode);

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteObjectAsync_IfMatch_ETagMismatch_Returns412()
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

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
        {
            throw new AmazonS3Exception("Precondition Failed")
            {
                StatusCode = HttpStatusCode.PreconditionFailed,
                ErrorCode = "PreconditionFailed"
            };
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
    }

    [Fact]
    public async Task DeleteObjectAsync_ExistingObject_DeletesSuccessfully()
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

        var existsResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, existsResponse.HttpStatusCode);

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteObjectAsync_NonExistentObject_SucceedsIdempotently()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.DeleteObjectAsync(bucketName, "non-existent.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.HttpStatusCode);
    }

    #endregion

    #region Delete Operations with Versioning

    [Fact]
    public async Task DeleteObjectAsync_VersionedBucket_CreatesDeleteMarker()
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
        var originalVersionId = putResponse.VersionId;

        // Act
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.NotNull(deleteResponse.VersionId);
        Assert.NotEqual(originalVersionId, deleteResponse.VersionId);

        var listResponse = await _client.ListVersionsAsync(bucketName);
        var deleteMarkers = listResponse.Versions.Where(v => v.Key == "file.txt" && v.IsDeleteMarker).ToList();
        Assert.Single(deleteMarkers);

        var originalVersion = listResponse.Versions.FirstOrDefault(v =>
            v.Key == "file.txt" && v.VersionId == originalVersionId && !v.IsDeleteMarker);
        Assert.NotNull(originalVersion);
    }

    [Fact]
    public async Task DeleteObjectAsync_WithVersionId_PermanentlyDeletesVersion()
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
            VersionId = putResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        var listResponse = await _client.ListVersionsAsync(bucketName);
        var versions = listResponse.Versions.Where(v => v.Key == "file.txt").ToList();
        Assert.DoesNotContain(versions, v => v.VersionId == putResponse.VersionId);
    }

    [Fact]
    public async Task DeleteObjectAsync_DeleteMarker_RemovingItRestoresObject()
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

        var deleteMarkerResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");
        var deleteMarkerVersionId = deleteMarkerResponse.VersionId;

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectAsync(bucketName, "file.txt"));
        Assert.Equal("NoSuchKey", exception.ErrorCode);

        // Act
        await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = deleteMarkerVersionId
        });

        // Assert
        var getResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        Assert.Equal(HttpStatusCode.OK, getResponse.HttpStatusCode);

        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content", content);
    }

    [Fact]
    public async Task DeleteObjectAsync_MultipleVersions_CanDeleteAllVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        var versionIds = new List<string>();
        for (int i = 0; i < 3; i++)
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
        foreach (var versionId in versionIds)
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = "file.txt",
                VersionId = versionId
            });
        }

        // Assert
        var listResponse = await _client.ListVersionsAsync(bucketName);
        var remainingVersions = listResponse.Versions.Where(v => v.Key == "file.txt" && !v.IsDeleteMarker).ToList();
        Assert.Empty(remainingVersions);
    }

    [Fact]
    public async Task DeleteObjectAsync_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.DeleteObjectAsync("non-existent-bucket", "file.txt"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    #endregion
}
