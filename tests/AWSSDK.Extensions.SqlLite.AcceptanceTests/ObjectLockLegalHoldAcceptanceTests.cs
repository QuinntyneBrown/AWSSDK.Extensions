using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Legal Hold operations using SqlLiteS3Client.
/// Tests verify legal hold behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 6.
/// </summary>
public class ObjectLockLegalHoldAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ObjectLockLegalHoldAcceptanceTests()
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

    #region 6.1 PutObjectLegalHoldAsync

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task PutObjectLegalHoldAsync_EnableLegalHold_SetsStatusOn()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            ContentBody = "evidence content"
        });

        // Act
        var response = await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify legal hold is enabled
        var getResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt"
        });

        Assert.Equal("ON", getResponse.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task PutObjectLegalHoldAsync_DisableLegalHold_SetsStatusOff()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            ContentBody = "evidence content"
        });

        // Enable legal hold first
        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Act - Disable legal hold
        var response = await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.Off
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify legal hold is disabled
        var getResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt"
        });

        Assert.Equal("OFF", getResponse.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task PutObjectLegalHold_WithRetention_BothCanBeSet()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Set retention
        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.Governance,
                RetainUntilDate = DateTime.UtcNow.AddDays(30)
            }
        });

        // Act - Set legal hold
        var response = await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Assert - Both retention and legal hold are set
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var retentionResponse = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.NotNull(retentionResponse.Retention);

        var legalHoldResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.Equal("ON", legalHoldResponse.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task PutObjectLegalHoldAsync_NonExistentObject_ThrowsException()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
            {
                BucketName = bucketName,
                Key = "non-existent.txt",
                LegalHold = new ObjectLockLegalHold
                {
                    Status = ObjectLockLegalHoldStatus.On
                }
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    #endregion

    #region 6.2 GetObjectLegalHoldAsync

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task GetObjectLegalHoldAsync_HoldEnabled_ReturnsOn()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            ContentBody = "evidence content"
        });

        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt",
            LegalHold = new ObjectLockLegalHold
            {
                Status = ObjectLockLegalHoldStatus.On
            }
        });

        // Act
        var response = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "evidence.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("ON", response.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task GetObjectLegalHoldAsync_HoldNotEnabled_ReturnsOff()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act
        var response = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("OFF", response.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task GetObjectLegalHoldAsync_NonExistentObject_ThrowsException()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
            {
                BucketName = bucketName,
                Key = "non-existent.txt"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task PutObjectLegalHoldAsync_ToggleMultipleTimes_WorksCorrectly()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Initial state should be OFF
        var initialResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.Equal("OFF", initialResponse.LegalHold?.Status?.Value);

        // Toggle ON
        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            LegalHold = new ObjectLockLegalHold { Status = ObjectLockLegalHoldStatus.On }
        });

        var onResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.Equal("ON", onResponse.LegalHold?.Status?.Value);

        // Toggle OFF
        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            LegalHold = new ObjectLockLegalHold { Status = ObjectLockLegalHoldStatus.Off }
        });

        var offResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.Equal("OFF", offResponse.LegalHold?.Status?.Value);

        // Toggle ON again
        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            LegalHold = new ObjectLockLegalHold { Status = ObjectLockLegalHoldStatus.On }
        });

        var finalResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });
        Assert.Equal("ON", finalResponse.LegalHold?.Status?.Value);
    }

    [Fact(Skip = "SqlLite implementation pending")]
    public async Task GetObjectLegalHoldAsync_AfterObjectRead_StatusPersists()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        await _client.PutObjectLegalHoldAsync(new PutObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            LegalHold = new ObjectLockLegalHold { Status = ObjectLockLegalHoldStatus.On }
        });

        // Read the object
        var getObjectResponse = await _client.GetObjectAsync(bucketName, "file.txt");
        using var reader = new StreamReader(getObjectResponse.ResponseStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("content", content);

        // Act - Check legal hold status
        var legalHoldResponse = await _client.GetObjectLegalHoldAsync(new GetObjectLegalHoldRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        // Assert
        Assert.Equal("ON", legalHoldResponse.LegalHold?.Status?.Value);
    }

    #endregion
}
