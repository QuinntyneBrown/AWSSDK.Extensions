using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Retention operations using SqlLiteS3Client.
/// Tests verify object retention behavior per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 5.
/// </summary>
public class ObjectLockRetentionAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ObjectLockRetentionAcceptanceTests()
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

    #region 5.1 PutObjectRetentionAsync

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Governance,
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

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Compliance,
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

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Governance,
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
                Mode = ObjectLockRetentionMode.Governance,
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

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Governance,
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

    [Fact(Skip = "SqlLite implementation pending")]
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
                    Mode = ObjectLockRetentionMode.Governance,
                    RetainUntilDate = DateTime.UtcNow.AddDays(30)
                }
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchKey", exception.ErrorCode);
    }

    #endregion

    #region 5.2 GetObjectRetentionAsync

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Governance,
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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
                Mode = ObjectLockRetentionMode.Governance,
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
                Mode = ObjectLockRetentionMode.Compliance,
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
