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
        Assert.That(getResponse.Metadata.Keys.Contains("original-key"), Is.False);
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

    #region Pre-signed URL Tests

    [Test]
    public void GetPreSignedURL_GeneratesValidURL_ForGET()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };

        // Act
        var url = _client.GetPreSignedURL(request);

        // Assert
        Assert.That(url, Is.Not.Null.And.Not.Empty);
        Assert.That(url, Does.StartWith("cblite://test-bucket/test-key"));
        Assert.That(url, Does.Contain("X-Expires="));
        Assert.That(url, Does.Contain("X-Verb=GET"));
        Assert.That(url, Does.Contain("X-Signature="));
    }

    [Test]
    public void GetPreSignedURL_GeneratesValidURL_ForPUT()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "upload-key",
            Expires = DateTime.UtcNow.AddMinutes(30),
            Verb = HttpVerb.PUT
        };

        // Act
        var url = _client.GetPreSignedURL(request);

        // Assert
        Assert.That(url, Does.Contain("X-Verb=PUT"));
    }

    [Test]
    public void GetPreSignedURL_GeneratesValidURL_ForDELETE()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "delete-key",
            Expires = DateTime.UtcNow.AddMinutes(15),
            Verb = HttpVerb.DELETE
        };

        // Act
        var url = _client.GetPreSignedURL(request);

        // Assert
        Assert.That(url, Does.Contain("X-Verb=DELETE"));
    }

    [Test]
    public async Task GetPreSignedURLAsync_GeneratesValidURL()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "async-key",
            Expires = DateTime.UtcNow.AddHours(2),
            Verb = HttpVerb.GET
        };

        // Act
        var url = await _client.GetPreSignedURLAsync(request);

        // Assert
        Assert.That(url, Is.Not.Null.And.Not.Empty);
        Assert.That(url, Does.StartWith("cblite://"));
    }

    [Test]
    public void GetPreSignedURL_IncludesVersionId_WhenSpecified()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "versioned-key",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET,
            VersionId = "v123456"
        };

        // Act
        var url = _client.GetPreSignedURL(request);

        // Assert
        Assert.That(url, Does.Contain("versionId=v123456"));
    }

    [Test]
    public void GetPreSignedURL_EncodesSpecialCharacters_InKey()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "folder/file with spaces.txt",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };

        // Act
        var url = _client.GetPreSignedURL(request);

        // Assert
        Assert.That(url, Does.Contain("folder%2Ffile%20with%20spaces.txt"));
    }

    [Test]
    public void GetPreSignedURL_ThrowsException_WhenBucketNameMissing()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            Key = "test-key",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _client.GetPreSignedURL(request));
    }

    [Test]
    public void GetPreSignedURL_ThrowsException_WhenKeyMissing()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _client.GetPreSignedURL(request));
    }

    [Test]
    public void ValidatePreSignedURL_ReturnsTrue_ForValidURL()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };
        var url = _client.GetPreSignedURL(request);

        // Act
        var isValid = _client.ValidatePreSignedURL(url);

        // Assert
        Assert.That(isValid, Is.True);
    }

    [Test]
    public void ValidatePreSignedURL_ReturnsFalse_ForExpiredURL()
    {
        // Arrange - create a URL that expired in the past
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Expires = DateTime.UtcNow.AddSeconds(-1), // Already expired
            Verb = HttpVerb.GET
        };
        var url = _client.GetPreSignedURL(request);

        // Act
        var isValid = _client.ValidatePreSignedURL(url);

        // Assert
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void ValidatePreSignedURL_ReturnsFalse_ForTamperedSignature()
    {
        // Arrange
        var request = new GetPreSignedUrlRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };
        var url = _client.GetPreSignedURL(request);

        // Tamper with the signature
        var tamperedUrl = url.Replace("X-Signature=", "X-Signature=TAMPERED");

        // Act
        var isValid = _client.ValidatePreSignedURL(tamperedUrl);

        // Assert
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void ValidatePreSignedURL_ReturnsFalse_ForInvalidURL()
    {
        // Act
        var isValid = _client.ValidatePreSignedURL("not-a-valid-url");

        // Assert
        Assert.That(isValid, Is.False);
    }

    [Test]
    public void GeneratePreSignedURL_GeneratesValidURL()
    {
        // Arrange
        var additionalProperties = new Dictionary<string, object>
        {
            { "Verb", HttpVerb.GET }
        };

        // Act
        var url = _client.GeneratePreSignedURL(
            "test-bucket",
            "test-key",
            DateTime.UtcNow.AddHours(1),
            additionalProperties);

        // Assert
        Assert.That(url, Is.Not.Null.And.Not.Empty);
        Assert.That(url, Does.StartWith("cblite://"));
    }

    #endregion

    #region ListObjects (V1 API) Tests

    [Test]
    public async Task ListObjects_ReturnsObjects_Successfully()
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
        var response = await _client.ListObjectsAsync("test-bucket");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.S3Objects, Has.Count.EqualTo(2));
        Assert.That(response.Name, Is.EqualTo("test-bucket"));
    }

    [Test]
    public async Task ListObjects_WithPrefix_FiltersObjects()
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
        var response = await _client.ListObjectsAsync("test-bucket", "dir1/");

        // Assert
        Assert.That(response.S3Objects, Has.Count.EqualTo(1));
        Assert.That(response.S3Objects[0].Key, Is.EqualTo("dir1/file1.txt"));
        Assert.That(response.Prefix, Is.EqualTo("dir1/"));
    }

    [Test]
    public async Task ListObjects_WithRequest_ReturnsObjects()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        });

        var request = new ListObjectsRequest
        {
            BucketName = "test-bucket"
        };

        // Act
        var response = await _client.ListObjectsAsync(request);

        // Assert
        Assert.That(response.S3Objects, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ListObjects_WithMarkerPagination_ReturnsCorrectObjects()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        for (int i = 1; i <= 5; i++)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "test-bucket",
                Key = $"key{i:D2}",
                ContentBody = $"content{i}"
            });
        }

        // Act - Get first page with 2 items
        var request1 = new ListObjectsRequest
        {
            BucketName = "test-bucket",
            MaxKeys = 2
        };
        var response1 = await _client.ListObjectsAsync(request1);

        // Assert first page
        Assert.That(response1.S3Objects, Has.Count.EqualTo(2));
        Assert.That(response1.IsTruncated, Is.True);
        Assert.That(response1.NextMarker, Is.Not.Null);

        // Act - Get second page using marker
        var request2 = new ListObjectsRequest
        {
            BucketName = "test-bucket",
            MaxKeys = 2,
            Marker = response1.NextMarker
        };
        var response2 = await _client.ListObjectsAsync(request2);

        // Assert second page
        Assert.That(response2.S3Objects, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListObjects_WithDelimiter_ReturnsCommonPrefixes()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/2023/image1.jpg",
            ContentBody = "image1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/2024/image2.jpg",
            ContentBody = "image2"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "documents/file.txt",
            ContentBody = "file"
        });

        var request = new ListObjectsRequest
        {
            BucketName = "test-bucket",
            Delimiter = "/"
        };

        // Act
        var response = await _client.ListObjectsAsync(request);

        // Assert
        Assert.That(response.CommonPrefixes, Has.Count.EqualTo(2));
        Assert.That(response.CommonPrefixes,
            Is.EquivalentTo(new[] { "photos/", "documents/" }));
        Assert.That(response.S3Objects, Is.Empty); // All objects are grouped under prefixes
    }

    [Test]
    public async Task ListObjects_WithPrefixAndDelimiter_ReturnsSubdirectories()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/2023/jan/image1.jpg",
            ContentBody = "image1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/2023/feb/image2.jpg",
            ContentBody = "image2"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/readme.txt",
            ContentBody = "readme"
        });

        var request = new ListObjectsRequest
        {
            BucketName = "test-bucket",
            Prefix = "photos/",
            Delimiter = "/"
        };

        // Act
        var response = await _client.ListObjectsAsync(request);

        // Assert
        Assert.That(response.CommonPrefixes, Has.Count.EqualTo(1));
        Assert.That(response.CommonPrefixes[0], Is.EqualTo("photos/2023/"));
        Assert.That(response.S3Objects, Has.Count.EqualTo(1));
        Assert.That(response.S3Objects[0].Key, Is.EqualTo("photos/readme.txt"));
    }

    [Test]
    public void ListObjects_BucketNotExists_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.ListObjectsAsync("non-existent-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task ListObjects_EmptyBucket_ReturnsEmptyList()
    {
        // Arrange
        await _client.PutBucketAsync("empty-bucket");

        // Act
        var response = await _client.ListObjectsAsync("empty-bucket");

        // Assert
        Assert.That(response.S3Objects, Is.Empty);
        Assert.That(response.IsTruncated, Is.False);
    }

    [Test]
    public async Task ListObjects_MaxKeys_LimitsResults()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        for (int i = 0; i < 10; i++)
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "test-bucket",
                Key = $"key{i}",
                ContentBody = "content"
            });
        }

        var request = new ListObjectsRequest
        {
            BucketName = "test-bucket",
            MaxKeys = 3
        };

        // Act
        var response = await _client.ListObjectsAsync(request);

        // Assert
        Assert.That(response.S3Objects, Has.Count.EqualTo(3));
        Assert.That(response.MaxKeys, Is.EqualTo(3));
        Assert.That(response.IsTruncated, Is.True);
    }

    #endregion

    #region Versioning Configuration Tests

    [Test]
    public async Task GetBucketVersioning_NewBucket_ReturnsNullStatus()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.GetBucketVersioningAsync("test-bucket");

        // Assert - AWS S3 returns null status for buckets that have never had versioning configured
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.VersioningConfig.Status, Is.Null);
    }

    [Test]
    public async Task PutBucketVersioning_EnableVersioning_Success()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var request = new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        };

        // Act
        var response = await _client.PutBucketVersioningAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await _client.GetBucketVersioningAsync("test-bucket");
        Assert.That(getResponse.VersioningConfig.Status, Is.EqualTo(VersionStatus.Enabled));
    }

    [Test]
    public async Task PutBucketVersioning_SuspendVersioning_Success()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await _client.GetBucketVersioningAsync("test-bucket");
        Assert.That(getResponse.VersioningConfig.Status, Is.EqualTo(VersionStatus.Suspended));
    }

    [Test]
    public async Task PutBucketVersioning_ReEnableAfterSuspend_Success()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Act
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = "test-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getResponse = await _client.GetBucketVersioningAsync("test-bucket");
        Assert.That(getResponse.VersioningConfig.Status, Is.EqualTo(VersionStatus.Enabled));
    }

    [Test]
    public void GetBucketVersioning_BucketNotExists_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync("non-existent-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public void PutBucketVersioning_BucketNotExists_ThrowsException()
    {
        // Arrange
        var request = new PutBucketVersioningRequest
        {
            BucketName = "non-existent-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutBucketVersioningAsync(request));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    #endregion

    #region ListVersions Tests

    [Test]
    public async Task ListVersions_ReturnsVersions_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        });

        // Act
        var response = await _client.ListVersionsAsync("test-bucket");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Versions, Has.Count.EqualTo(1));
        Assert.That(response.Versions[0].Key, Is.EqualTo("test-key"));
        Assert.That(response.Versions[0].IsLatest, Is.True);
    }

    [Test]
    public async Task ListVersions_WithPrefix_FiltersVersions()
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
        var response = await _client.ListVersionsAsync("test-bucket", "dir1/");

        // Assert
        Assert.That(response.Versions, Has.Count.EqualTo(1));
        Assert.That(response.Versions[0].Key, Is.EqualTo("dir1/file1.txt"));
    }

    [Test]
    public async Task ListVersions_WithRequest_ReturnsVersions()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = "content"
        });

        var request = new ListVersionsRequest { BucketName = "test-bucket" };

        // Act
        var response = await _client.ListVersionsAsync(request);

        // Assert
        Assert.That(response.Versions, Has.Count.EqualTo(1));
        Assert.That(response.Name, Is.EqualTo("test-bucket"));
    }

    [Test]
    public async Task ListVersions_WithDelimiter_ReturnsCommonPrefixes()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "photos/image1.jpg",
            ContentBody = "image1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "documents/file.txt",
            ContentBody = "file"
        });

        var request = new ListVersionsRequest
        {
            BucketName = "test-bucket",
            Delimiter = "/"
        };

        // Act
        var response = await _client.ListVersionsAsync(request);

        // Assert
        Assert.That(response.CommonPrefixes, Has.Count.EqualTo(2));
    }

    [Test]
    public void ListVersions_BucketNotExists_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.ListVersionsAsync("non-existent-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task ListVersions_EmptyBucket_ReturnsEmptyList()
    {
        // Arrange
        await _client.PutBucketAsync("empty-bucket");

        // Act
        var response = await _client.ListVersionsAsync("empty-bucket");

        // Assert
        Assert.That(response.Versions, Is.Empty);
    }

    #endregion

    #region Multipart Upload Tests

    [Test]
    public async Task InitiateMultipartUpload_ReturnsUploadId()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.InitiateMultipartUploadAsync("test-bucket", "large-file.dat");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.UploadId, Is.Not.Null.And.Not.Empty);
        Assert.That(response.BucketName, Is.EqualTo("test-bucket"));
        Assert.That(response.Key, Is.EqualTo("large-file.dat"));
    }

    [Test]
    public async Task UploadPart_ReturnsETag()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var initResponse = await _client.InitiateMultipartUploadAsync("test-bucket", "large-file.dat");

        var partContent = new byte[1024];
        new Random().NextBytes(partContent);

        var uploadRequest = new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 1,
            InputStream = new MemoryStream(partContent)
        };

        // Act
        var response = await _client.UploadPartAsync(uploadRequest);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null.And.Not.Empty);
        Assert.That(response.PartNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task CompleteMultipartUpload_CreatesObject()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var initResponse = await _client.InitiateMultipartUploadAsync("test-bucket", "large-file.dat");

        // Upload two parts
        var part1Content = Encoding.UTF8.GetBytes("Part1Content");
        var part1Response = await _client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 1,
            InputStream = new MemoryStream(part1Content)
        });

        var part2Content = Encoding.UTF8.GetBytes("Part2Content");
        var part2Response = await _client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 2,
            InputStream = new MemoryStream(part2Content)
        });

        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId
        };
        completeRequest.AddPartETags(
            new PartETag(1, part1Response.ETag),
            new PartETag(2, part2Response.ETag)
        );

        // Act
        var response = await _client.CompleteMultipartUploadAsync(completeRequest);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Does.EndWith("-2")); // Multipart ETag format

        // Verify object was created
        var getResponse = await _client.GetObjectAsync("test-bucket", "large-file.dat");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.That(content, Is.EqualTo("Part1ContentPart2Content"));
    }

    [Test]
    public async Task AbortMultipartUpload_DeletesPartsAndUpload()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var initResponse = await _client.InitiateMultipartUploadAsync("test-bucket", "large-file.dat");

        // Upload a part
        await _client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 1,
            InputStream = new MemoryStream(Encoding.UTF8.GetBytes("content"))
        });

        // Act
        var response = await _client.AbortMultipartUploadAsync("test-bucket", "large-file.dat", initResponse.UploadId);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify upload was deleted
        var listResponse = await _client.ListMultipartUploadsAsync("test-bucket");
        Assert.That(listResponse.MultipartUploads, Is.Empty);
    }

    [Test]
    public async Task ListMultipartUploads_ReturnsInProgressUploads()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var upload1 = await _client.InitiateMultipartUploadAsync("test-bucket", "file1.dat");
        var upload2 = await _client.InitiateMultipartUploadAsync("test-bucket", "file2.dat");

        // Act
        var response = await _client.ListMultipartUploadsAsync("test-bucket");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.MultipartUploads, Has.Count.EqualTo(2));

        // Clean up
        await _client.AbortMultipartUploadAsync("test-bucket", "file1.dat", upload1.UploadId);
        await _client.AbortMultipartUploadAsync("test-bucket", "file2.dat", upload2.UploadId);
    }

    [Test]
    public async Task ListParts_ReturnsUploadedParts()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var initResponse = await _client.InitiateMultipartUploadAsync("test-bucket", "large-file.dat");

        await _client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 1,
            InputStream = new MemoryStream(Encoding.UTF8.GetBytes("Part1"))
        });

        await _client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "large-file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 2,
            InputStream = new MemoryStream(Encoding.UTF8.GetBytes("Part2"))
        });

        // Act
        var response = await _client.ListPartsAsync("test-bucket", "large-file.dat", initResponse.UploadId);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Parts, Has.Count.EqualTo(2));
        Assert.That(response.Parts[0].PartNumber, Is.EqualTo(1));
        Assert.That(response.Parts[1].PartNumber, Is.EqualTo(2));

        // Clean up
        await _client.AbortMultipartUploadAsync("test-bucket", "large-file.dat", initResponse.UploadId);
    }

    [Test]
    public void UploadPart_InvalidPartNumber_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();
        var initResponse = _client.InitiateMultipartUploadAsync("test-bucket", "file.dat").Result;

        var request = new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "file.dat",
            UploadId = initResponse.UploadId,
            PartNumber = 0, // Invalid - must be 1-10000
            InputStream = new MemoryStream(new byte[10])
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.UploadPartAsync(request));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.ErrorCode, Is.EqualTo("InvalidArgument"));
    }

    [Test]
    public void UploadPart_NonExistentUpload_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        var request = new UploadPartRequest
        {
            BucketName = "test-bucket",
            Key = "file.dat",
            UploadId = "non-existent-upload-id",
            PartNumber = 1,
            InputStream = new MemoryStream(new byte[10])
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.UploadPartAsync(request));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchUpload"));
    }

    #endregion

    #region ACL and Policy Tests

    [Test]
    public async Task GetACL_Bucket_ReturnsDefaultACL()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.GetACLAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.AccessControlList, Is.Not.Null);
        Assert.That(response.AccessControlList.Owner, Is.Not.Null);
        Assert.That(response.AccessControlList.Grants.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task GetACL_Object_ReturnsDefaultACL()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });

        // Act
        var response = await _client.GetACLAsync(new GetACLRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.AccessControlList, Is.Not.Null);
    }

    [Test]
    public void GetACL_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetACLAsync("non-existent-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task PutACL_SetsACLSuccessfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var request = new PutACLRequest
        {
            BucketName = "test-bucket",
            CannedACL = S3CannedACL.PublicRead
        };

        // Act
        var response = await _client.PutACLAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PutBucketPolicy_SetsPolicy_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var policy = @"{""Version"":""2012-10-17"",""Statement"":[{""Effect"":""Allow"",""Principal"":""*"",""Action"":""s3:GetObject"",""Resource"":""arn:aws:s3:::test-bucket/*""}]}";

        // Act
        var response = await _client.PutBucketPolicyAsync("test-bucket", policy);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetBucketPolicy_ReturnsPolicy_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var policy = @"{""Version"":""2012-10-17"",""Statement"":[]}";
        await _client.PutBucketPolicyAsync("test-bucket", policy);

        // Act
        var response = await _client.GetBucketPolicyAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Policy, Is.EqualTo(policy));
    }

    [Test]
    public void GetBucketPolicy_NoPolicySet_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetBucketPolicyAsync("test-bucket"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucketPolicy"));
    }

    [Test]
    public async Task DeleteBucketPolicy_RemovesPolicy_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var policy = @"{""Version"":""2012-10-17"",""Statement"":[]}";
        await _client.PutBucketPolicyAsync("test-bucket", policy);

        // Act
        var response = await _client.DeleteBucketPolicyAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify policy is deleted
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetBucketPolicyAsync("test-bucket"));
        Assert.That(ex!.ErrorCode, Is.EqualTo("NoSuchBucketPolicy"));
    }

    [Test]
    public void PutBucketPolicy_InvalidJSON_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();
        var invalidPolicy = "not valid json";

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutBucketPolicyAsync("test-bucket", invalidPolicy));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(ex.ErrorCode, Is.EqualTo("MalformedPolicy"));
    }

    [Test]
    public async Task PutPublicAccessBlock_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var request = new PutPublicAccessBlockRequest
        {
            BucketName = "test-bucket",
            PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
            {
                BlockPublicAcls = true,
                IgnorePublicAcls = true,
                BlockPublicPolicy = true,
                RestrictPublicBuckets = true
            }
        };

        // Act
        var response = await _client.PutPublicAccessBlockAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetPublicAccessBlock_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
        {
            BucketName = "test-bucket",
            PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
            {
                BlockPublicAcls = true,
                IgnorePublicAcls = false,
                BlockPublicPolicy = true,
                RestrictPublicBuckets = false
            }
        });

        // Act
        var response = await _client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.PublicAccessBlockConfiguration, Is.Not.Null);
        Assert.That(response.PublicAccessBlockConfiguration.BlockPublicAcls, Is.True);
        Assert.That(response.PublicAccessBlockConfiguration.IgnorePublicAcls, Is.False);
        Assert.That(response.PublicAccessBlockConfiguration.BlockPublicPolicy, Is.True);
        Assert.That(response.PublicAccessBlockConfiguration.RestrictPublicBuckets, Is.False);
    }

    [Test]
    public void GetPublicAccessBlock_NoConfigSet_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest
            {
                BucketName = "test-bucket"
            }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchPublicAccessBlockConfiguration"));
    }

    [Test]
    public async Task DeletePublicAccessBlock_RemovesConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
        {
            BucketName = "test-bucket",
            PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
            {
                BlockPublicAcls = true
            }
        });

        // Act
        var response = await _client.DeletePublicAccessBlockAsync(new DeletePublicAccessBlockRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify configuration is deleted
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest
            {
                BucketName = "test-bucket"
            }));
        Assert.That(ex!.ErrorCode, Is.EqualTo("NoSuchPublicAccessBlockConfiguration"));
    }

    #endregion

    #region Bucket Configuration Tests

    [Test]
    public async Task PutBucketEncryption_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var request = new PutBucketEncryptionRequest
        {
            BucketName = "test-bucket",
            ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
            {
                ServerSideEncryptionRules = new List<ServerSideEncryptionRule>
                {
                    new ServerSideEncryptionRule
                    {
                        ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
                        {
                            ServerSideEncryptionAlgorithm = ServerSideEncryptionMethod.AES256
                        },
                        BucketKeyEnabled = true
                    }
                }
            }
        };

        // Act
        var response = await _client.PutBucketEncryptionAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetBucketEncryption_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketEncryptionAsync(new PutBucketEncryptionRequest
        {
            BucketName = "test-bucket",
            ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
            {
                ServerSideEncryptionRules = new List<ServerSideEncryptionRule>
                {
                    new ServerSideEncryptionRule
                    {
                        ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
                        {
                            ServerSideEncryptionAlgorithm = ServerSideEncryptionMethod.AES256
                        }
                    }
                }
            }
        });

        // Act
        var response = await _client.GetBucketEncryptionAsync(new GetBucketEncryptionRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ServerSideEncryptionConfiguration, Is.Not.Null);
    }

    [Test]
    public async Task DeleteBucketEncryption_RemovesConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketEncryptionAsync(new PutBucketEncryptionRequest
        {
            BucketName = "test-bucket",
            ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
            {
                ServerSideEncryptionRules = new List<ServerSideEncryptionRule>
                {
                    new ServerSideEncryptionRule
                    {
                        ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
                        {
                            ServerSideEncryptionAlgorithm = ServerSideEncryptionMethod.AES256
                        }
                    }
                }
            }
        });

        // Act
        var response = await _client.DeleteBucketEncryptionAsync(new DeleteBucketEncryptionRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PutLifecycleConfiguration_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var configuration = new LifecycleConfiguration
        {
            Rules = new List<LifecycleRule>
            {
                new LifecycleRule
                {
                    Id = "expire-old-objects",
                    Status = LifecycleRuleStatus.Enabled,
                    Prefix = "logs/",
                    Expiration = new LifecycleRuleExpiration { Days = 30 }
                }
            }
        };

        // Act
        var response = await _client.PutLifecycleConfigurationAsync("test-bucket", configuration);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetLifecycleConfiguration_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutLifecycleConfigurationAsync("test-bucket", new LifecycleConfiguration
        {
            Rules = new List<LifecycleRule>
            {
                new LifecycleRule
                {
                    Id = "test-rule",
                    Status = LifecycleRuleStatus.Enabled,
                    Prefix = "",
                    Expiration = new LifecycleRuleExpiration { Days = 7 }
                }
            }
        });

        // Act
        var response = await _client.GetLifecycleConfigurationAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Configuration, Is.Not.Null);
        Assert.That(response.Configuration.Rules.Count, Is.EqualTo(1));
        Assert.That(response.Configuration.Rules[0].Id, Is.EqualTo("test-rule"));
    }

    [Test]
    public async Task PutCORSConfiguration_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var configuration = new CORSConfiguration
        {
            Rules = new List<CORSRule>
            {
                new CORSRule
                {
                    Id = "allow-all",
                    AllowedOrigins = new List<string> { "*" },
                    AllowedMethods = new List<string> { "GET", "PUT" },
                    AllowedHeaders = new List<string> { "*" },
                    MaxAgeSeconds = 3600
                }
            }
        };

        // Act
        var response = await _client.PutCORSConfigurationAsync("test-bucket", configuration);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetCORSConfiguration_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutCORSConfigurationAsync("test-bucket", new CORSConfiguration
        {
            Rules = new List<CORSRule>
            {
                new CORSRule
                {
                    Id = "test-cors",
                    AllowedOrigins = new List<string> { "https://example.com" },
                    AllowedMethods = new List<string> { "GET" }
                }
            }
        });

        // Act
        var response = await _client.GetCORSConfigurationAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Configuration, Is.Not.Null);
        Assert.That(response.Configuration.Rules.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task PutBucketWebsite_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        var configuration = new WebsiteConfiguration
        {
            IndexDocumentSuffix = "index.html",
            ErrorDocument = "error.html"
        };

        // Act
        var response = await _client.PutBucketWebsiteAsync("test-bucket", configuration);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetBucketWebsite_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketWebsiteAsync("test-bucket", new WebsiteConfiguration
        {
            IndexDocumentSuffix = "index.html"
        });

        // Act
        var response = await _client.GetBucketWebsiteAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.WebsiteConfiguration, Is.Not.Null);
        Assert.That(response.WebsiteConfiguration.IndexDocumentSuffix, Is.EqualTo("index.html"));
    }

    [Test]
    public async Task PutBucketLogging_SetsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketAsync("log-bucket");

        var request = new PutBucketLoggingRequest
        {
            BucketName = "test-bucket",
            LoggingConfig = new S3BucketLoggingConfig
            {
                TargetBucketName = "log-bucket",
                TargetPrefix = "logs/"
            }
        };

        // Act
        var response = await _client.PutBucketLoggingAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetBucketLogging_ReturnsConfiguration_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketAsync("log-bucket");
        await _client.PutBucketLoggingAsync(new PutBucketLoggingRequest
        {
            BucketName = "test-bucket",
            LoggingConfig = new S3BucketLoggingConfig
            {
                TargetBucketName = "log-bucket",
                TargetPrefix = "prefix/"
            }
        });

        // Act
        var response = await _client.GetBucketLoggingAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.BucketLoggingConfig, Is.Not.Null);
        Assert.That(response.BucketLoggingConfig.TargetBucketName, Is.EqualTo("log-bucket"));
    }

    [Test]
    public async Task GetBucketNotification_ReturnsEmptyConfiguration()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.GetBucketNotificationAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.TopicConfigurations, Is.Empty);
        Assert.That(response.QueueConfigurations, Is.Empty);
        Assert.That(response.LambdaFunctionConfigurations, Is.Empty);
    }

    #endregion

    #region Object Tagging and Protection Tests

    [Test]
    public async Task PutObjectTagging_SetsTagsSuccessfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });

        // Act
        var response = await _client.PutObjectTaggingAsync(new PutObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Tagging = new Tagging
            {
                TagSet = new List<Tag>
                {
                    new Tag { Key = "Environment", Value = "Production" },
                    new Tag { Key = "Project", Value = "Test" }
                }
            }
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetObjectTagging_ReturnsTags()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });
        await _client.PutObjectTaggingAsync(new PutObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Tagging = new Tagging
            {
                TagSet = new List<Tag>
                {
                    new Tag { Key = "Team", Value = "DevOps" }
                }
            }
        });

        // Act
        var response = await _client.GetObjectTaggingAsync(new GetObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Tagging.Count, Is.EqualTo(1));
        Assert.That(response.Tagging[0].Key, Is.EqualTo("Team"));
        Assert.That(response.Tagging[0].Value, Is.EqualTo("DevOps"));
    }

    [Test]
    public async Task DeleteObjectTagging_RemovesTags()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });
        await _client.PutObjectTaggingAsync(new PutObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Tagging = new Tagging
            {
                TagSet = new List<Tag> { new Tag { Key = "Test", Value = "Value" } }
            }
        });

        // Act
        var response = await _client.DeleteObjectTaggingAsync(new DeleteObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify tags are deleted
        var tagsResponse = await _client.GetObjectTaggingAsync(new GetObjectTaggingRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });
        Assert.That(tagsResponse.Tagging, Is.Empty);
    }

    [Test]
    public async Task PutObjectLegalHold_SetsLegalHold()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });

        // Act
        var response = await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetObjectLegalHold_ReturnsLegalHold()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });
        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            LegalHold = new ObjectLockLegalHold { Status = ObjectLockLegalHoldStatus.On }
        });

        // Act
        var response = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.LegalHold.Status.Value, Is.EqualTo("ON"));
    }

    [Test]
    public async Task PutObjectLockConfiguration_SetsConfiguration()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = "test-bucket",
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled,
                Rule = new ObjectLockRule
                {
                    DefaultRetention = new DefaultRetention
                    {
                        Mode = ObjectLockRetentionMode.Governance,
                        Days = 30
                    }
                }
            }
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetObjectLockConfiguration_ReturnsConfiguration()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = "test-bucket",
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled,
                Rule = new ObjectLockRule
                {
                    DefaultRetention = new DefaultRetention
                    {
                        Mode = ObjectLockRetentionMode.Governance,
                        Days = 7
                    }
                }
            }
        });

        // Act
        var response = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ObjectLockConfiguration, Is.Not.Null);
        Assert.That(response.ObjectLockConfiguration.Rule.DefaultRetention.Days, Is.EqualTo(7));
    }

    [Test]
    public async Task PutObjectRetention_SetsRetention()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });

        // Act
        var response = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.Governance,
                RetainUntilDate = DateTime.UtcNow.AddDays(30)
            }
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetObjectRetention_ReturnsRetention()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });
        var retainUntil = DateTime.UtcNow.AddDays(30);
        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.Compliance,
                RetainUntilDate = retainUntil
            }
        });

        // Act
        var response = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Retention, Is.Not.Null);
        Assert.That(response.Retention.Mode.Value, Is.EqualTo("COMPLIANCE"));
    }

    #endregion

    #region Advanced Features Tests

    [Test]
    public async Task PutBucketTagging_SetsTagsSuccessfully()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");

        // Act
        var response = await _client.PutBucketTaggingAsync("test-bucket", new List<Tag>
        {
            new Tag { Key = "Environment", Value = "Production" },
            new Tag { Key = "Team", Value = "Engineering" }
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetBucketTagging_ReturnsTags()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketTaggingAsync("test-bucket", new List<Tag>
        {
            new Tag { Key = "CostCenter", Value = "12345" }
        });

        // Act
        var response = await _client.GetBucketTaggingAsync(new GetBucketTaggingRequest
        {
            BucketName = "test-bucket"
        });

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.TagSet.Count, Is.EqualTo(1));
        Assert.That(response.TagSet[0].Key, Is.EqualTo("CostCenter"));
        Assert.That(response.TagSet[0].Value, Is.EqualTo("12345"));
    }

    [Test]
    public async Task DeleteBucketTagging_RemovesTags()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        await _client.PutBucketTaggingAsync("test-bucket", new List<Tag>
        {
            new Tag { Key = "Test", Value = "Value" }
        });

        // Act
        var response = await _client.DeleteBucketTaggingAsync("test-bucket");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify tags are deleted
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetBucketTaggingAsync(new GetBucketTaggingRequest
            {
                BucketName = "test-bucket"
            }));
        Assert.That(ex!.ErrorCode, Is.EqualTo("NoSuchTagSet"));
    }

    [Test]
    public async Task RestoreObject_SucceedsForExistingObject()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            InputStream = stream
        });

        // Act
        var response = await _client.RestoreObjectAsync("test-bucket", "test-key");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void RestoreObject_ThrowsForNonExistentObject()
    {
        // Arrange
        _client.PutBucketAsync("test-bucket").Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.RestoreObjectAsync("test-bucket", "non-existent-key"));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchKey"));
    }

    #endregion
}
