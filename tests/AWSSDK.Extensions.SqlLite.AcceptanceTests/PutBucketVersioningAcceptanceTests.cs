using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for PutBucketVersioning operations using SqlLiteS3Client.
/// Tests verify the behavior of setting bucket versioning configuration.
/// </summary>
public class PutBucketVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public PutBucketVersioningAcceptanceTests()
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

    // Acceptance Criteria 2.1 - Scenario: Enable versioning on a bucket for the first time
    [Fact]
    public async Task PutBucketVersioningAsync_EnableVersioningFirstTime_Succeeds()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        };

        // Act
        var response = await _client.PutBucketVersioningAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Enabled, getResponse.VersioningConfig.Status);
    }

    // Acceptance Criteria 2.1 - Scenario: Enable versioning on a previously versioning-suspended bucket
    [Fact]
    public async Task PutBucketVersioningAsync_EnableVersioningOnSuspendedBucket_Succeeds()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // First enable then suspend
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Act - Re-enable versioning
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Enabled, getResponse.VersioningConfig.Status);
    }

    // Acceptance Criteria 2.1 - Scenario: Suspend versioning on a versioning-enabled bucket
    [Fact]
    public async Task PutBucketVersioningAsync_SuspendVersioning_PreservesExistingVersions()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Create an object to get a version
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "test-object",
            ContentBody = "version 1"
        });

        // Act - Suspend versioning
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getVersioningResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Suspended, getVersioningResponse.VersioningConfig.Status);

        // Verify object still accessible (versions preserved)
        var getObjectResponse = await _client.GetObjectAsync(bucketName, "test-object");
        Assert.Equal(HttpStatusCode.OK, getObjectResponse.HttpStatusCode);
    }

    // Acceptance Criteria 2.1 - Scenario: Attempt to disable versioning completely (not possible)
    [Fact]
    public async Task PutBucketVersioningAsync_CannotCompletelyDisableVersioning_OnlyCanSuspend()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act - Suspend versioning (the only way to "disable")
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Suspended }
        });

        // Assert - Bucket should be in Suspended state, not Off/Disabled
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Suspended, getResponse.VersioningConfig.Status);
    }

    // Acceptance Criteria 2.1 - Scenario: Fail to set versioning on non-existent bucket
    [Fact]
    public async Task PutBucketVersioningAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Arrange
        var request = new PutBucketVersioningRequest
        {
            BucketName = "non-existent-bucket",
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.PutBucketVersioningAsync(request));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 2.1 - Scenario: Enable versioning with ExpectedBucketOwner validation - success
    [Fact]
    public async Task PutBucketVersioningAsync_WithMatchingExpectedBucketOwner_Succeeds()
    {
        // Arrange
        var bucketName = "owner-validated-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled },
            ExpectedBucketOwner = "123456789012"
        };

        // Act
        var response = await _client.PutBucketVersioningAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
    }

    // Acceptance Criteria 2.1 - Scenario: Enable versioning with ExpectedBucketOwner validation - failure
    [Fact]
    public async Task PutBucketVersioningAsync_WithMismatchedExpectedBucketOwner_ThrowsAccessDeniedException()
    {
        // Arrange
        var bucketName = "owner-mismatch-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled },
            ExpectedBucketOwner = "999999999999"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.PutBucketVersioningAsync(request));

        Assert.Equal("AccessDenied", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    // Acceptance Criteria 2.1 - Scenario: Versioning propagation after first enable
    [Fact]
    public async Task PutBucketVersioningAsync_EnableVersioning_VersioningActiveForSubsequentOperations()
    {
        // Arrange
        var bucketName = "versioning-propagation-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act - Enable versioning
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        // Create an object and verify it gets a version ID
        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "test-object",
            ContentBody = "test content"
        });

        // In a versioning-enabled bucket, PutObject should return a version ID
        Assert.NotNull(putResponse.VersionId);
        Assert.NotEqual("null", putResponse.VersionId);
    }
}
