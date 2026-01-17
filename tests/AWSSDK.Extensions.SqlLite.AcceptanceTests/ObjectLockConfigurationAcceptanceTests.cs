using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for Object Lock Configuration operations using SqlLiteS3Client.
/// Tests verify bucket object lock configuration per IAmazonS3-Transaction-Management-AcceptanceCriteria.md Section 7.
/// </summary>
public class ObjectLockConfigurationAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public ObjectLockConfigurationAcceptanceTests()
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

    #region 7.1 PutObjectLockConfigurationAsync

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

        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Equal("GOVERNANCE", getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(30, getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Days);
    }

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

        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Equal("COMPLIANCE", getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Mode?.Value);
        Assert.Equal(7, getResponse.ObjectLockConfiguration?.Rule?.DefaultRetention?.Years);
    }

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

        var getResponse = await _client.GetObjectLockConfigurationAsync(new GetObjectLockConfigurationRequest
        {
            BucketName = bucketName
        });

        Assert.Equal("Enabled", getResponse.ObjectLockConfiguration?.ObjectLockEnabled?.Value);
        Assert.Null(getResponse.ObjectLockConfiguration?.Rule);
    }

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
