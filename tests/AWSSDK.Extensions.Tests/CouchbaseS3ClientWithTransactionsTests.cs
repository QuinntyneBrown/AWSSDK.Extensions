using Amazon.S3.Model;
using AWSSDK.Extensions;
using System.Net;
using System.Text;

namespace AWSSDK.Extensions.Tests;

[TestFixture]
public class CouchbaseS3ClientWithTransactionsTests
{
    private string _testDbPath = null!;
    private CouchbaseS3ClientWithTransactions _client = null!;

    [SetUp]
    public void Setup()
    {
        // Create a unique database path for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_tx_db_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDbPath);
        _client = new CouchbaseS3ClientWithTransactions(Path.Combine(_testDbPath, "test.cblite2"));
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
    public async Task PutBucket_CreatesBucket_WithTransaction()
    {
        // Arrange
        var request = new PutBucketRequest { BucketName = "tx-bucket" };

        // Act
        var response = await _client.PutBucketAsync(request);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        
        // Verify bucket exists
        var buckets = await _client.ListBucketsAsync();
        Assert.That(buckets.Buckets, Has.Count.EqualTo(1));
        Assert.That(buckets.Buckets[0].BucketName, Is.EqualTo("tx-bucket"));
    }

    [Test]
    public void PutBucket_DuplicateBucket_ThrowsInTransaction()
    {
        // Arrange
        var request = new PutBucketRequest { BucketName = "duplicate-tx-bucket" };
        _client.PutBucketAsync(request).Wait();

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutBucketAsync(request));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task DeleteBucket_RemovesBucket_WithTransaction()
    {
        // Arrange
        await _client.PutBucketAsync(new PutBucketRequest { BucketName = "tx-delete-bucket" });

        // Act
        var response = await _client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = "tx-delete-bucket" });

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        
        var buckets = await _client.ListBucketsAsync();
        Assert.That(buckets.Buckets, Is.Empty);
    }

    #endregion

    #region Transactional Object Tests

    [Test]
    public async Task PutObjectsTransactionalAsync_StoresMultipleObjects_Atomically()
    {
        // Arrange
        await _client.PutBucketAsync("tx-bucket");
        
        var requests = new List<PutObjectRequest>
        {
            new PutObjectRequest
            {
                BucketName = "tx-bucket",
                Key = "key1",
                ContentBody = "content1"
            },
            new PutObjectRequest
            {
                BucketName = "tx-bucket",
                Key = "key2",
                ContentBody = "content2"
            },
            new PutObjectRequest
            {
                BucketName = "tx-bucket",
                Key = "key3",
                ContentBody = "content3"
            }
        };

        // Act
        var responses = await _client.PutObjectsTransactionalAsync(requests);

        // Assert
        Assert.That(responses, Has.Count.EqualTo(3));
        Assert.That(responses.All(r => r.HttpStatusCode == HttpStatusCode.OK), Is.True);
        
        // Verify all objects exist
        var listResponse = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = "tx-bucket" });
        Assert.That(listResponse.S3Objects, Has.Count.EqualTo(3));
    }

    [Test]
    public void PutObjectsTransactionalAsync_BucketDoesNotExist_ThrowsException()
    {
        // Arrange
        var requests = new List<PutObjectRequest>
        {
            new PutObjectRequest
            {
                BucketName = "non-existent-bucket",
                Key = "key1",
                ContentBody = "content1"
            }
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.PutObjectsTransactionalAsync(requests));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task PutObject_SingleObject_UsesTransaction()
    {
        // Arrange
        await _client.PutBucketAsync("tx-bucket");
        var request = new PutObjectRequest
        {
            BucketName = "tx-bucket",
            Key = "single-key",
            ContentBody = "single content"
        };

        // Act
        var response = await _client.PutObjectAsync(request);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null);
        
        // Verify object exists
        var getResponse = await _client.GetObjectAsync("tx-bucket", "single-key");
        Assert.That(getResponse, Is.Not.Null);
    }

    [Test]
    public async Task DeleteObjectsAsync_RemovesMultipleObjects_Atomically()
    {
        // Arrange
        await _client.PutBucketAsync("tx-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "tx-bucket",
            Key = "delete-key1",
            ContentBody = "content1"
        });
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "tx-bucket",
            Key = "delete-key2",
            ContentBody = "content2"
        });

        var deleteRequest = new DeleteObjectsRequest { BucketName = "tx-bucket" };
        deleteRequest.AddKey("delete-key1");
        deleteRequest.AddKey("delete-key2");

        // Act
        var response = await _client.DeleteObjectsAsync(deleteRequest);

        // Assert
        Assert.That(response.DeletedObjects, Has.Count.EqualTo(2));
        Assert.That(response.DeleteErrors, Is.Empty);
        
        // Verify objects are deleted
        var listResponse = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = "tx-bucket" });
        Assert.That(listResponse.S3Objects, Is.Empty);
    }

    [Test]
    public async Task CopyObjectAsync_CopiesObject_Successfully()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        
        var content = "Original content";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = content
        });

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = "source-bucket",
            SourceKey = "source-key",
            DestinationBucket = "dest-bucket",
            DestinationKey = "dest-key"
        };

        // Act
        var response = await _client.CopyObjectAsync(copyRequest);

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.ETag, Is.Not.Null);
        
        // Verify copied object
        var getResponse = await _client.GetObjectAsync("dest-bucket", "dest-key");
        using var reader = new StreamReader(getResponse.ResponseStream);
        var copiedContent = await reader.ReadToEndAsync();
        Assert.That(copiedContent, Is.EqualTo(content));
    }

    [Test]
    public void CopyObjectAsync_SourceDoesNotExist_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("source-bucket").Wait();
        _client.PutBucketAsync("dest-bucket").Wait();

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = "source-bucket",
            SourceKey = "non-existent-key",
            DestinationBucket = "dest-bucket",
            DestinationKey = "dest-key"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(copyRequest));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchKey"));
    }

    [Test]
    public void CopyObjectAsync_DestinationBucketDoesNotExist_ThrowsException()
    {
        // Arrange
        _client.PutBucketAsync("source-bucket").Wait();
        _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "content"
        }).Wait();

        var copyRequest = new CopyObjectRequest
        {
            SourceBucket = "source-bucket",
            SourceKey = "source-key",
            DestinationBucket = "non-existent-bucket",
            DestinationKey = "dest-key"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.CopyObjectAsync(copyRequest));
        
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(ex.ErrorCode, Is.EqualTo("NoSuchBucket"));
    }

    [Test]
    public async Task CopyObject_WithSimpleOverload_CopiesSuccessfully()
    {
        // Arrange
        await _client.PutBucketAsync("source-bucket");
        await _client.PutBucketAsync("dest-bucket");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "source-bucket",
            Key = "source-key",
            ContentBody = "test content"
        });

        // Act
        var response = await _client.CopyObjectAsync(
            "source-bucket", "source-key", "dest-bucket", "dest-key");

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        
        // Verify copy
        var getResponse = await _client.GetObjectAsync("dest-bucket", "dest-key");
        Assert.That(getResponse, Is.Not.Null);
    }

    [Test]
    public async Task DeleteObject_SingleObject_WorksCorrectly()
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
        
        // Verify deletion
        Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _client.GetObjectAsync("test-bucket", "test-key"));
    }

    [Test]
    public async Task GetObject_RetrievesStoredObject()
    {
        // Arrange
        await _client.PutBucketAsync("test-bucket");
        var content = "Transactional content";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key",
            ContentBody = content
        });

        // Act
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test-key"
        });

        // Assert
        Assert.That(response.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var reader = new StreamReader(response.ResponseStream);
        var retrievedContent = await reader.ReadToEndAsync();
        Assert.That(retrievedContent, Is.EqualTo(content));
    }

    [Test]
    public async Task ListObjectsV2_ReturnsStoredObjects()
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

    #endregion
}
