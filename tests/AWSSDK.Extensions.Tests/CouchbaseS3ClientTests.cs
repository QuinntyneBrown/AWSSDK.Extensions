using Amazon.S3;
using Amazon.S3.Model;
using AWSSDK.Extensions;
using System.Net;
using System.Text;

namespace AWSSDK.Extensions.Tests;

[TestFixture]
public class CouchbaseS3ClientTests
{
    private string _testDbPath = null!;
    private CouchbaseS3Client _client = null!;

    [SetUp]
    public void Setup()
    {
        // Create a unique database path for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDbPath);
        _client = new CouchbaseS3Client(Path.Combine(_testDbPath, "test.cblite2"));
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        
        // Clean up test database
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

    #region Bucket Tests

    [Test]
    public async Task PutBucket_CreatesBucket_Successfully()
    {
        // Arrange
        var bucketName = "test-bucket";

        // Act
        var response = await _client.PutBucketAsync(bucketName);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PutBucket_WithRequest_CreatesBucket_Successfully()
    {
        // Arrange
        var request = new PutBucketRequest { BucketName = "test-bucket-2" };

        // Act
        var response = await _client.PutBucketAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void PutBucket_DuplicateBucket_ThrowsException()
    {
        // Arrange
        var bucketName = "duplicate-bucket";
        _client.PutBucketAsync(bucketName).Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutBucketAsync(bucketName));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(ex.ErrorCode, Is.EqualTo("BucketAlreadyExists"));
    }

    [Test]
    public async Task ListBuckets_ReturnsEmptyList_WhenNoBuckets()
    {
        // Act
        var response = await _client.ListBucketsAsync();

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Buckets, Is.Empty);
    }

    [Test]
    public async Task ListBuckets_ReturnsBuckets_WhenBucketsExist()
    {
        // Arrange
        await _client.PutBucketAsync("bucket-1");
        await _client.PutBucketAsync("bucket-2");

        // Act
        var response = await _client.ListBucketsAsync();

        // Assert
        Assert.That(response.Buckets, Has.Count.EqualTo(2));
        Assert.That(response.Buckets.Select(b => b.BucketName), 
            Is.EquivalentTo(new[] { "bucket-1", "bucket-2" }));
    }

    [Test]
    public async Task DeleteBucket_RemovesBucket_Successfully()
    {
        // Arrange
        var bucketName = "bucket-to-delete";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.DeleteBucketAsync(bucketName);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        
        var listResponse = await _client.ListBucketsAsync();
        Assert.That(listResponse.Buckets, Is.Empty);
    }

    [Test]
    public void DeleteBucket_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.DeleteBucketAsync("non-existent"));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task DeleteBucket_NonEmptyBucket_ThrowsException()
    {
        // Arrange
        var bucketName = "non-empty-bucket";
        await _client.PutBucketAsync(bucketName);
        
        var putRequest = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "test-key",
            ContentBody = "test content"
        };
        await _client.PutObjectAsync(putRequest);

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.DeleteBucketAsync(bucketName));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(ex.ErrorCode, Is.EqualTo("BucketNotEmpty"));
    }

    #endregion

    #region Object Tests

    [Test]
    public async Task PutObject_StoresObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var request = new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "Hello, World!"
        };

        // Act
        var response = await _client.PutObjectAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task PutObject_WithStream_StoresObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var content = "Stream content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        
        var request = new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "stream-key",
            InputStream = stream
        };

        // Act
        var response = await _client.PutObjectAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null);
    }

    [Test]
    public void PutObject_BucketDoesNotExist_ThrowsException()
    {
        // Arrange
        var request = new PutObjectRequest
        {
            BucketName = "non-existent-bucket",
            Key = "test-key",
            ContentBody = "test"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutObjectAsync(request));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task GetObject_RetrievesObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var content = "Test content";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = content
        });

        // Act
        var response = await _client.GetObjectAsync("test-bucket", "test-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Key, Is.EqualTo("test-key"));
        
        using var reader = new StreamReader(response.ResponseStream);
        var retrievedContent = await reader.ReadToEndAsync();
        Assert.That(retrievedContent, Is.EqualTo(content));
    }

    [Test]
    public void GetObject_NonExistentObject_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetObjectAsync("test-bucket", "non-existent"));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchKey"));
    }

    [Test]
    public async Task DeleteObject_RemovesObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "test"
        });

        // Act
        var response = await _client.DeleteObjectAsync("test-bucket", "test-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        
        // Verify object is deleted
        Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetObjectAsync("test-bucket", "test-key"));
    }

    [Test]
    public async Task DeleteObjects_RemovesMultipleObjects_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "key1",
            ContentBody = "content1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "key2",
            ContentBody = "content2"
        });

        var request = new DeleteObjectsRequest
        {
            BucketName = "test-bucket"
        };
        request.AddKey("key1");
        request.AddKey("key2");

        // Act
        var response = await _client.DeleteObjectsAsync(request);

        // Assert
        Assert.That(response.DeletedObjects, Has.Count.EqualTo(2));
        Assert.That(response.DeleteErrors, Is.Empty);
    }

    [Test]
    public async Task ListObjectsV2_ReturnsObjects_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "key1",
            ContentBody = "content1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "key2",
            ContentBody = "content2"
        });

        // Act
        var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response.S3Objects, Has.Count.EqualTo(2));
        Assert.That(response.S3Objects.Select(o => o.Key), 
            Is.EquivalentTo(new[] { "key1", "key2" }));
    }

    [Test]
    public async Task ListObjectsV2_WithPrefix_FiltersObjects()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "dir1/file1.txt",
            ContentBody = "content1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "dir2/file2.txt",
            ContentBody = "content2"
        });

        // Act
        var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket",
            Prefix = "dir1/"
        });

        // Assert
        Assert.That(response.S3Objects, Has.Count.EqualTo(1));
        Assert.That(response.S3Objects[0].Key, Is.EqualTo("dir1/file1.txt"));
    }

    [Test]
    public async Task PutObject_WithMetadata_StoresMetadata()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var request = new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        };
        request.Metadata.Add("custom-key", "custom-value");

        // Act
        await _client.PutObjectAsync(request);
        var getResponse = await _client.GetObjectAsync("test-bucket", "test-key");

        // Assert
        Assert.That(getResponse.Metadata["custom-key"], Is.EqualTo("custom-value"));
    }

    #endregion

    #region Metadata Tests

    [Test]
    public async Task GetObjectMetadata_ReturnsMetadata_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var request = new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "Test content",
            ContentType = "text/plain"
        };
        request.Metadata.Add("custom-header", "custom-value");
        await _client.PutObjectAsync(request);

        // Act
        var response = await _client.GetObjectMetadataAsync("test-bucket", "test-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ContentLength, Is.EqualTo(12)); // "Test content" length
        Assert.That(response.ETag, Is.Not.Null.And.Not.Empty);
        Assert.That(response.Headers.ContentType, Is.EqualTo("text/plain"));
        Assert.That(response.Metadata["custom-header"], Is.EqualTo("custom-value"));
    }

    [Test]
    public async Task GetObjectMetadata_WithRequest_ReturnsMetadata_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        });

        var request = new GetObjectMetadataRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        };

        // Act
        var response = await _client.GetObjectMetadataAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ContentLength, Is.EqualTo(7)); // "content" length
    }

    [Test]
    public void GetObjectMetadata_NonExistentObject_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetObjectMetadataAsync("test-bucket", "non-existent"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchKey"));
    }

    [Test]
    public async Task GetObjectMetadata_ReturnsLastModified_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var beforePut = DateTime.UtcNow.AddSeconds(-1);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        });

        var afterPut = DateTime.UtcNow.AddSeconds(1);

        // Act
        var response = await _client.GetObjectMetadataAsync("test-bucket", "test-key");

        // Assert
        Assert.That(response.LastModified, Is.GreaterThan(beforePut).And.LessThan(afterPut));
    }

    #endregion

    #region HeadBucket Tests

    [Test]
    public async Task HeadBucket_ExistingBucket_ReturnsSuccess()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.HeadBucketAsync("test-bucket");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.BucketRegion, Is.EqualTo("local"));
    }

    [Test]
    public async Task HeadBucket_WithRequest_ExistingBucket_ReturnsSuccess()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var request = new HeadBucketRequest { BucketName = "test-bucket" };

        // Act
        var response = await _client.HeadBucketAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void HeadBucket_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.HeadBucketAsync("non-existent-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    #endregion

    #region DoesS3BucketExist Tests

    [Test]
    public async Task DoesS3BucketExist_ExistingBucket_ReturnsTrue()
    {
        // Arrange
        await _client.PutBucketAsync("existing-bucket");

        // Act
        var exists = await _client.DoesS3BucketExistAsync("existing-bucket");

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task DoesS3BucketExist_NonExistentBucket_ReturnsFalse()
    {
        // Act
        var exists = await _client.DoesS3BucketExistAsync("non-existent-bucket");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task DoesS3BucketExist_DeletedBucket_ReturnsFalse()
    {
        // Arrange
        await _client.PutBucketAsync("bucket-to-delete");
        await _client.DeleteBucketAsync("bucket-to-delete");

        // Act
        var exists = await _client.DoesS3BucketExistAsync("bucket-to-delete");

        // Assert
        Assert.That(exists, Is.False);
    }

    #endregion

    #region Copy Operations Tests

    [Test]
    public async Task CopyObject_CopiesObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        var content = "Test content to copy";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = content
        });

        // Act
        var response = await _client.CopyObjectAsync(
            "source-bucket", "source-key",
            "dest-bucket", "dest-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null.And.Not.Empty);

        // Verify the copy was successful
        var getResponse = await _client.GetObjectAsync("dest-bucket", "dest-key");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var copiedContent = await reader.ReadToEndAsync();
        Assert.That(copiedContent, Is.EqualTo(content));
    }

    [Test]
    public async Task CopyObject_WithRequest_CopiesObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "content"
        });

        var request = new CopyObjectRequest
        {
            SourceBucket = "source-bucket",
            SourceKey = "source-key",
            DestinationBucket = "dest-bucket",
            DestinationKey = "new-key"
        };

        // Act
        var response = await _client.CopyObjectAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null);
    }

    [Test]
    public async Task CopyObject_CopiesMetadata_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        var putRequest = new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "content"
        };
        putRequest.Metadata.Add("custom-key", "custom-value");
        await _client.PutObjectAsync(putRequest);

        // Act
        await _client.CopyObjectAsync("source-bucket", "source-key", "dest-bucket", "dest-key");

        // Assert
        var getResponse = await _client.GetObjectAsync("dest-bucket", "dest-key");
        Assert.That(getResponse.Metadata["custom-key"], Is.EqualTo("custom-value"));
    }

    [Test]
    public async Task CopyObject_WithReplaceMetadata_ReplacesMetadata()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        var putRequest = new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "content"
        };
        putRequest.Metadata.Add("original-key", "original-value");
        await _client.PutObjectAsync(putRequest);

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = "source-bucket",
            SourceKey = "source-key",
            DestinationBucket = "dest-bucket",
            DestinationKey = "dest-key",
            MetadataDirective = S3MetadataDirective.REPLACE
        };
        copyRequest.Metadata.Add("new-key", "new-value");

        // Act
        await _client.CopyObjectAsync(copyRequest);

        // Assert
        var getResponse = await _client.GetObjectAsync("dest-bucket", "dest-key");
        Assert.That(getResponse.Metadata.ContainsKey("original-key"), Is.False);
        Assert.That(getResponse.Metadata["new-key"], Is.EqualTo("new-value"));
    }

    [Test]
    public void CopyObject_SourceNotExists_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("dest-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(
                "source-bucket", "non-existent",
                "dest-bucket", "dest-key"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchKey"));
    }

    [Test]
    public async Task CopyObject_DestBucketNotExists_ThrowsException()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "content"
        });

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(
                "source-bucket", "source-key",
                "non-existent-bucket", "dest-key"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task CopyObject_SameBucket_DifferentKey_Succeeds()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "original-key",
            ContentBody = "content"
        });

        // Act
        var response = await _client.CopyObjectAsync(
            "test-bucket", "original-key",
            "test-bucket", "copied-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify both objects exist
        var listResponse = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "test-bucket"
        });
        Assert.That(listResponse.S3Objects, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task CopyObject_VerifyDataIntegrity_ETags_Match()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        var content = "Test content for ETag verification";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = content
        });

        // Act
        var copyResponse = await _client.CopyObjectAsync(
            "source-bucket", "source-key",
            "dest-bucket", "dest-key");

        // Assert
        var sourceMetadata = await _client.GetObjectMetadataAsync("source-bucket", "source-key");
        Assert.That(copyResponse.ETag, Is.EqualTo(sourceMetadata.ETag));
    }

    #endregion
}
