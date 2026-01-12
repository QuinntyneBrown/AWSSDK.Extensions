using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for GetBucketVersioning operations.
/// Tests verify the behavior of retrieving bucket versioning configuration.
/// </summary>
public class GetBucketVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public GetBucketVersioningAcceptanceTests()
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

    // Acceptance Criteria 1.1 - Scenario: Get versioning status for a bucket that has never had versioning set
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" that has never had versioning configured
    // When I call GetBucketVersioningAsync with bucket name "my-bucket"
    // Then the response should have HTTP status code 200
    // And the VersioningConfig.Status should be null or empty
    // And the MFADelete should be null or not present
    [Fact]
    public async Task GetBucketVersioningAsync_BucketNeverHadVersioning_ReturnsNullOrEmptyStatus()
    {
        // Arrange
        var bucketName = "my-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);
        Assert.True(string.IsNullOrEmpty(response.VersioningConfig?.Status?.Value));
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning status for a versioning-enabled bucket
    // Given I have valid AWS credentials
    // And I own a bucket "versioned-bucket" with versioning enabled
    // When I call GetBucketVersioningAsync with bucket name "versioned-bucket"
    // Then the response should have HTTP status code 200
    // And the VersioningConfig.Status should be "Enabled"
    [Fact]
    public async Task GetBucketVersioningAsync_VersioningEnabled_ReturnsEnabledStatus()
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

    // Acceptance Criteria 1.1 - Scenario: Get versioning status for a versioning-suspended bucket
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // When I call GetBucketVersioningAsync with bucket name "suspended-bucket"
    // Then the response should have HTTP status code 200
    // And the VersioningConfig.Status should be "Suspended"
    [Fact]
    public async Task GetBucketVersioningAsync_VersioningSuspended_ReturnsSuspendedStatus()
    {
        // Arrange
        var bucketName = "suspended-bucket";
        await _client.PutBucketAsync(bucketName);
        // First enable versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });
        // Then suspend it
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

    // Acceptance Criteria 1.1 - Scenario: Get versioning status for a bucket with MFA Delete enabled
    // Given I have valid AWS credentials
    // And I own a bucket "mfa-bucket" with MFA Delete enabled
    // When I call GetBucketVersioningAsync with bucket name "mfa-bucket"
    // Then the response should have HTTP status code 200
    // And the VersioningConfig.Status should be "Enabled"
    // And the MFADelete should be "Enabled"
    [Fact]
    public async Task GetBucketVersioningAsync_MfaDeleteEnabled_ReturnsMfaDeleteEnabled()
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

    // Acceptance Criteria 1.1 - Scenario: Fail to get versioning status for non-existent bucket
    // Given I have valid AWS credentials
    // And no bucket named "non-existent-bucket" exists
    // When I call GetBucketVersioningAsync with bucket name "non-existent-bucket"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchBucket"
    // And the HTTP status code should be 404
    [Fact]
    public async Task GetBucketVersioningAsync_NonExistentBucket_ThrowsNoSuchBucketException()
    {
        // Arrange
        var bucketName = "non-existent-bucket";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
            async () => await _client.GetBucketVersioningAsync(bucketName));

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    // Acceptance Criteria 1.1 - Scenario: Get versioning status with ExpectedBucketOwner validation - success
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" with account ID "123456789012"
    // When I call GetBucketVersioningAsync with bucket name "my-bucket" and ExpectedBucketOwner "123456789012"
    // Then the response should have HTTP status code 200
    [Fact]
    public async Task GetBucketVersioningAsync_WithMatchingExpectedBucketOwner_Succeeds()
    {
        // Arrange
        var bucketName = "owner-validated-bucket";
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

    // Acceptance Criteria 1.1 - Scenario: Get versioning status with ExpectedBucketOwner validation - failure
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" with account ID "123456789012"
    // When I call GetBucketVersioningAsync with bucket name "my-bucket" and ExpectedBucketOwner "999999999999"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "AccessDenied"
    // And the HTTP status code should be 403
    [Fact]
    public async Task GetBucketVersioningAsync_WithMismatchedExpectedBucketOwner_ThrowsAccessDeniedException()
    {
        // Arrange
        var bucketName = "owner-mismatch-bucket";
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
}
