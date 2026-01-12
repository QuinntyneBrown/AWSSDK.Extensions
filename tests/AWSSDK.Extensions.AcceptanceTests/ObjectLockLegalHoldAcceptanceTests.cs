using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Legal Hold operations.
/// Tests verify legal hold behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 6.
/// </summary>
public class ObjectLockLegalHoldAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ObjectLockLegalHoldAcceptanceTests()
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

    #region 6.1 PutObjectLegalHoldAsync

    // Acceptance Criteria 6.1 - Scenario: Enable legal hold on an object
    // Given I have valid AWS credentials
    // And I have s3:PutObjectLegalHold permission
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // And object "evidence.txt" exists
    // When I call PutObjectLegalHoldAsync with Status "ON"
    // Then the response should have HTTP status code 200
    // And the object should have legal hold enabled
    // And the object cannot be deleted until legal hold is removed
    [Fact]
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

    // Acceptance Criteria 6.1 - Scenario: Remove legal hold from an object
    // Given I have valid AWS credentials
    // And I have s3:PutObjectLegalHold permission
    // And object "evidence.txt" has legal hold enabled
    // When I call PutObjectLegalHoldAsync with Status "OFF"
    // Then the response should have HTTP status code 200
    // And the object legal hold should be removed
    // And the object can be deleted (if no retention period)
    [Fact]
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

    // Acceptance Criteria 6.1 - Scenario: Legal hold with retention period - both must be cleared
    // Given I have valid AWS credentials
    // And object "file.txt" has both legal hold and Governance retention until "2026-01-01"
    // When retention period expires on "2026-01-01"
    // Then the object still cannot be deleted because legal hold is active
    // When I remove the legal hold
    // Then the object can be deleted
    [Fact]
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
                Mode = ObjectLockRetentionMode.GOVERNANCE,
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

    // Additional test: Put legal hold on non-existent object throws exception
    [Fact]
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

    // Acceptance Criteria 6.2 - Scenario: Get legal hold status - hold is enabled
    // Given I have valid AWS credentials
    // And I have s3:GetObjectLegalHold permission
    // And object "evidence.txt" has legal hold enabled
    // When I call GetObjectLegalHoldAsync for "evidence.txt"
    // Then the response should have HTTP status code 200
    // And the response Status should be "ON"
    [Fact]
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

    // Acceptance Criteria 6.2 - Scenario: Get legal hold status - hold is not enabled
    // Given I have valid AWS credentials
    // And object "file.txt" does not have legal hold
    // When I call GetObjectLegalHoldAsync for "file.txt"
    // Then the response should have HTTP status code 200
    // And the response Status should be "OFF"
    [Fact]
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

    // Additional test: Get legal hold for non-existent object throws exception
    [Fact]
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

    // Additional test: Toggle legal hold multiple times
    [Fact]
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

    // Additional test: Legal hold status persists after object content read
    [Fact]
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
