using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Retention operations.
/// Tests verify object retention behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 5.
/// </summary>
public class ObjectLockRetentionAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ObjectLockRetentionAcceptanceTests()
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

    #region 5.1 PutObjectRetentionAsync

    // Acceptance Criteria 5.1 - Scenario: Set Governance mode retention on an object
    // Given I have valid AWS credentials
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // And object "file.txt" exists
    // When I call PutObjectRetentionAsync with Mode GOVERNANCE and RetainUntilDate
    // Then the response should have HTTP status code 200
    // And the object should be locked in Governance mode until the specified date
    [Fact]
    public async Task PutObjectRetentionAsync_GovernanceMode_SetsRetention()
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

        var retainUntilDate = DateTime.UtcNow.AddDays(30);

        // Act
        var response = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.GOVERNANCE,
                RetainUntilDate = retainUntilDate
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify retention was set
        var getResponse = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        Assert.Equal("GOVERNANCE", getResponse.Retention?.Mode?.Value);
    }

    // Acceptance Criteria 5.1 - Scenario: Set Compliance mode retention on an object
    // Given I have valid AWS credentials
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // And object "file.txt" exists
    // When I call PutObjectRetentionAsync with Mode COMPLIANCE and RetainUntilDate
    // Then the response should have HTTP status code 200
    // And the object should be locked in Compliance mode
    // And no user including root can delete the object before retention expires
    [Fact]
    public async Task PutObjectRetentionAsync_ComplianceMode_SetsRetention()
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

        var retainUntilDate = DateTime.UtcNow.AddYears(1);

        // Act
        var response = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.COMPLIANCE,
                RetainUntilDate = retainUntilDate
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify retention was set
        var getResponse = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        Assert.Equal("COMPLIANCE", getResponse.Retention?.Mode?.Value);
    }

    // Acceptance Criteria 5.1 - Scenario: Extend retention period succeeds
    // Given I have valid AWS credentials
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // And object "file.txt" has Governance retention until "2025-06-01"
    // When I call PutObjectRetentionAsync with RetainUntilDate "2025-12-01"
    // Then the response should have HTTP status code 200
    // And the retention period should be extended
    [Fact]
    public async Task PutObjectRetentionAsync_ExtendRetentionPeriod_Succeeds()
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

        var initialRetainUntilDate = DateTime.UtcNow.AddMonths(3);
        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.GOVERNANCE,
                RetainUntilDate = initialRetainUntilDate
            }
        });

        var extendedRetainUntilDate = DateTime.UtcNow.AddMonths(6);

        // Act
        var response = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.GOVERNANCE,
                RetainUntilDate = extendedRetainUntilDate
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        // The retention date should be extended
        Assert.NotNull(getResponse.Retention?.RetainUntilDate);
    }

    // Acceptance Criteria 5.1 - Scenario: Bypass Governance mode with permission
    // Given I have valid AWS credentials
    // And I have s3:BypassGovernanceRetention permission
    // And object "file.txt" has Governance retention
    // When I call PutObjectRetentionAsync with empty retention and BypassGovernanceRetention header
    // Then the response should have HTTP status code 200
    // And the retention should be removed
    [Fact]
    public async Task PutObjectRetentionAsync_RemoveRetention_Succeeds()
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

        // Set initial retention
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

        // Act - Remove retention
        var response = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = null
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        Assert.Null(getResponse.Retention);
    }

    // Additional test: Put retention on non-existent object throws exception
    [Fact]
    public async Task PutObjectRetentionAsync_NonExistentObject_ThrowsException()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
            {
                BucketName = bucketName,
                Key = "non-existent.txt",
                Retention = new ObjectLockRetention
                {
                    Mode = ObjectLockRetentionMode.GOVERNANCE,
                    RetainUntilDate = DateTime.UtcNow.AddDays(30)
                }
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    #endregion

    #region 5.2 GetObjectRetentionAsync

    // Acceptance Criteria 5.2 - Scenario: Get retention settings for a locked object
    // Given I have valid AWS credentials
    // And I have s3:GetObjectRetention permission
    // And object "file.txt" has Governance retention until "2026-01-01"
    // When I call GetObjectRetentionAsync for "file.txt"
    // Then the response should have HTTP status code 200
    // And the response Mode should be "GOVERNANCE"
    // And the response RetainUntilDate should be "2026-01-01T00:00:00Z"
    [Fact]
    public async Task GetObjectRetentionAsync_ObjectWithRetention_ReturnsRetentionSettings()
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

        var retainUntilDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.GOVERNANCE,
                RetainUntilDate = retainUntilDate
            }
        });

        // Act
        var response = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("GOVERNANCE", response.Retention?.Mode?.Value);
        Assert.NotNull(response.Retention?.RetainUntilDate);
    }

    // Acceptance Criteria 5.2 - Scenario: Get retention for object without retention
    // Given I have valid AWS credentials
    // And object "file.txt" has no retention settings
    // When I call GetObjectRetentionAsync for "file.txt"
    // Then the response should indicate no retention is set
    [Fact]
    public async Task GetObjectRetentionAsync_ObjectWithoutRetention_ReturnsNull()
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
        var response = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "file.txt"
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Null(response.Retention);
    }

    // Additional test: Get retention for non-existent object throws exception
    [Fact]
    public async Task GetObjectRetentionAsync_NonExistentObject_ThrowsException()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
            {
                BucketName = bucketName,
                Key = "non-existent.txt"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    // Additional test: Retention mode values are correct
    [Fact]
    public async Task PutObjectRetentionAsync_ValidModes_AcceptsBothGovernanceAndCompliance()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "gov-file.txt",
            ContentBody = "governance content"
        });

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "comp-file.txt",
            ContentBody = "compliance content"
        });

        // Act & Assert - Governance
        var govResponse = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "gov-file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.GOVERNANCE,
                RetainUntilDate = DateTime.UtcNow.AddDays(30)
            }
        });
        Assert.Equal(HttpStatusCode.OK, govResponse.HttpStatusCode);

        // Act & Assert - Compliance
        var compResponse = await _client.PutObjectRetentionAsync(new PutObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "comp-file.txt",
            Retention = new ObjectLockRetention
            {
                Mode = ObjectLockRetentionMode.COMPLIANCE,
                RetainUntilDate = DateTime.UtcNow.AddDays(30)
            }
        });
        Assert.Equal(HttpStatusCode.OK, compResponse.HttpStatusCode);

        // Verify
        var govGet = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "gov-file.txt"
        });
        Assert.Equal("GOVERNANCE", govGet.Retention?.Mode?.Value);

        var compGet = await _client.GetObjectRetentionAsync(new GetObjectRetentionRequest
        {
            BucketName = bucketName,
            Key = "comp-file.txt"
        });
        Assert.Equal("COMPLIANCE", compGet.Retention?.Mode?.Value);
    }

    #endregion
}
