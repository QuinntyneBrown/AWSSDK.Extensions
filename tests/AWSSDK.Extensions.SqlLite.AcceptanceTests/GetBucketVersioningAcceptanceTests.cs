using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetBucketVersioning operations using SqlLiteS3Client.
/// Tests verify the behavior of retrieving bucket versioning configuration.
/// </summary>
public class GetBucketVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public GetBucketVersioningAcceptanceTests()
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

    // Acceptance Criteria 1.1 - Scenario: Get versioning status from never-configured bucket
    [Fact]
    public async Task GetBucketVersioningAsync_NeverConfigured_ReturnsNullStatus()
    {
        // Arrange
        var bucketName = "new-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Null(response.VersioningConfig.Status);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning status from enabled bucket
    [Fact]
    public async Task GetBucketVersioningAsync_Enabled_ReturnsEnabledStatus()
    {
        // Arrange
        var bucketName = "versioned-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(VersionStatus.Enabled, response.VersioningConfig.Status);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning status from suspended bucket
    [Fact]
    public async Task GetBucketVersioningAsync_Suspended_ReturnsSuspendedStatus()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable then suspend
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

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(VersionStatus.Suspended, response.VersioningConfig.Status);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning status from non-existent bucket
    [Fact]
    public async Task GetBucketVersioningAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync("non-existent-bucket"));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning with ExpectedBucketOwner - success
    [Fact]
    public async Task GetBucketVersioningAsync_WithMatchingExpectedBucketOwner_Succeeds()
    {
        // Arrange
        var bucketName = "owner-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new GetBucketVersioningRequest
        {
            BucketName = bucketName,
            ExpectedBucketOwner = "123456789012"
        };

        // Act
        var response = await _client.GetBucketVersioningAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning with ExpectedBucketOwner - failure
    [Fact]
    public async Task GetBucketVersioningAsync_WithMismatchedExpectedBucketOwner_ThrowsAccessDeniedException()
    {
        // Arrange
        var bucketName = "owner-bucket";
        await _client.PutBucketAsync(bucketName);

        var request = new GetBucketVersioningRequest
        {
            BucketName = bucketName,
            ExpectedBucketOwner = "999999999999"
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync(request));

        Assert.Equal("AccessDenied", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning returns MFA Delete status
    [Fact]
    public async Task GetBucketVersioningAsync_WithMfaDeleteEnabled_ReturnsMfaDeleteStatus()
    {
        // Arrange
        var bucketName = "mfa-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = true
            }
        });

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.Equal(VersionStatus.Enabled, response.VersioningConfig.Status);
        Assert.True(response.VersioningConfig.EnableMfaDelete);
    }
}
