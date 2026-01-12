using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Configuration operations.
/// Tests verify bucket object lock configuration per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 7.
/// </summary>
public class ObjectLockConfigurationAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public ObjectLockConfigurationAcceptanceTests()
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

    #region 7.1 PutObjectLockConfigurationAsync

    // Acceptance Criteria 7.1 - Scenario: Set default retention configuration on bucket
    // Given I have valid AWS credentials
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // When I call PutObjectLockConfigurationAsync with:
    //   | Property             | Value      |
    //   | DefaultRetention.Mode | GOVERNANCE |
    //   | DefaultRetention.Days | 30         |
    // Then the response should have HTTP status code 200
    // And new objects should automatically have 30-day Governance retention
    [Fact]
    public async Task PutObjectLockConfigurationAsync_DefaultRetentionDays_SetsConfiguration()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
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
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify configuration was set
        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Equal("GOVERNANCE", getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(30, getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Days);
    }

    // Acceptance Criteria 7.1 - Scenario: Set default retention in years
    // Given I have valid AWS credentials
    // And I own a bucket "lock-bucket" with Object Lock enabled
    // When I call PutObjectLockConfigurationAsync with:
    //   | Property              | Value      |
    //   | DefaultRetention.Mode | COMPLIANCE |
    //   | DefaultRetention.Years | 7          |
    // Then the response should have HTTP status code 200
    // And new objects should have 7-year Compliance retention
    [Fact]
    public async Task PutObjectLockConfigurationAsync_DefaultRetentionYears_SetsConfiguration()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled,
                Rule = new ObjectLockRule
                {
                    DefaultRetention = new DefaultRetention
                    {
                        Mode = ObjectLockRetentionMode.Compliance,
                        Years = 7
                    }
                }
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify configuration was set
        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Equal("COMPLIANCE", getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(7, getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Years);
    }

    // Acceptance Criteria 7.1 - Scenario: Remove default retention configuration
    // Given I have valid AWS credentials
    // And bucket "lock-bucket" has default retention configured
    // When I call PutObjectLockConfigurationAsync with empty retention
    // Then the response should have HTTP status code 200
    // And new objects will not have automatic retention
    [Fact]
    public async Task PutObjectLockConfigurationAsync_RemoveDefaultRetention_Succeeds()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Set initial configuration
        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
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

        // Act - Remove default retention by setting config without rule
        var response = await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled,
                Rule = null
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Verify configuration was updated
        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Null(getResponse.ObjectLockConfiguration?.Rule);
    }

    // Additional test: Put object lock configuration on non-existent bucket throws exception
    [Fact]
    public async Task PutObjectLockConfigurationAsync_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
            {
                BucketName = "non-existent-bucket",
                ObjectLockConfiguration = new ObjectLockConfiguration
                {
                    ObjectLockEnabled = ObjectLockEnabled.Enabled
                }
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    #endregion

    #region 7.2 GetObjectLockConfigurationAsync

    // Acceptance Criteria 7.2 - Scenario: Get Object Lock configuration with defaults
    // Given I have valid AWS credentials
    // And I have s3:GetBucketObjectLockConfiguration permission
    // And bucket "lock-bucket" has default Governance retention of 30 days
    // When I call GetObjectLockConfigurationAsync for "lock-bucket"
    // Then the response should have HTTP status code 200
    // And the response ObjectLockEnabled should be "Enabled"
    // And the response DefaultRetention.Mode should be "GOVERNANCE"
    // And the response DefaultRetention.Days should be 30
    [Fact]
    public async Task GetObjectLockConfigurationAsync_WithDefaults_ReturnsConfiguration()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
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

        // Act
        var response = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("Enabled", response.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Equal("GOVERNANCE", response.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(30, response.ObjectLockConfiguration?.Rule?.DefaultRetention?.Days);
    }

    // Acceptance Criteria 7.2 - Scenario: Get Object Lock configuration without defaults
    // Given I have valid AWS credentials
    // And bucket "lock-bucket" has Object Lock enabled but no default retention
    // When I call GetObjectLockConfigurationAsync for "lock-bucket"
    // Then the response should have HTTP status code 200
    // And the response ObjectLockEnabled should be "Enabled"
    // And the response DefaultRetention should be null or not present
    [Fact]
    public async Task GetObjectLockConfigurationAsync_WithoutDefaults_ReturnsEnabledOnlyConfiguration()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled
            }
        });

        // Act
        var response = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal("Enabled", response.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Null(response.ObjectLockConfiguration?.Rule);
    }

    // Acceptance Criteria 7.2 - Scenario: Fail to get config for non-Object-Lock bucket
    // Given I have valid AWS credentials
    // And bucket "regular-bucket" does not have Object Lock enabled
    // When I call GetObjectLockConfigurationAsync for "regular-bucket"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "ObjectLockConfigurationNotFoundError"
    [Fact]
    public async Task GetObjectLockConfigurationAsync_NoBucketLockConfig_ThrowsException()
    {
        // Arrange
        var bucketName = "regular-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
            {
                BucketName = bucketName
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("ObjectLockConfigurationNotFoundError", exception.ErrorCode);
    }

    // Additional test: Get object lock configuration on non-existent bucket throws exception
    [Fact]
    public async Task GetObjectLockConfigurationAsync_NonExistentBucket_ThrowsException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(async () =>
            await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
            {
                BucketName = "non-existent-bucket"
            }));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    // Additional test: Update object lock configuration
    [Fact]
    public async Task PutObjectLockConfigurationAsync_UpdateConfiguration_Succeeds()
    {
        // Arrange
        var bucketName = "lock-bucket";
        await _client.PutBucketAsync(bucketName);

        // Set initial configuration
        await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
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

        // Act - Update to Compliance mode with years
        var response = await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
        {
            BucketName = bucketName,
            ObjectLockConfiguration = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled,
                Rule = new ObjectLockRule
                {
                    DefaultRetention = new DefaultRetention
                    {
                        Mode = ObjectLockRetentionMode.Compliance,
                        Years = 5
                    }
                }
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("COMPLIANCE", getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(5, getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Years);
    }

    #endregion
}
