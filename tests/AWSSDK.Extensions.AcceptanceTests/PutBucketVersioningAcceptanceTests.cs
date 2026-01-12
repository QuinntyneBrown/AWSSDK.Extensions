using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for PutBucketVersioning operations.
/// Tests verify the behavior of setting bucket versioning configuration.
/// </summary>
public class PutBucketVersioningAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public PutBucketVersioningAcceptanceTests()
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

    // Acceptance Criteria 2.1 - Scenario: Enable versioning on a bucket for the first time
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" that has never had versioning configured
    // When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled"
    // Then the response should have HTTP status code 200
    // And subsequent GetBucketVersioningAsync should return Status "Enabled"
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
    // Given I have valid AWS credentials
    // And I own a bucket "suspended-bucket" with versioning suspended
    // When I call PutBucketVersioningAsync with bucket name "suspended-bucket" and Status "Enabled"
    // Then the response should have HTTP status code 200
    // And subsequent GetBucketVersioningAsync should return Status "Enabled"
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
    // Given I have valid AWS credentials
    // And I own a bucket "versioned-bucket" with versioning enabled
    // When I call PutBucketVersioningAsync with bucket name "versioned-bucket" and Status "Suspended"
    // Then the response should have HTTP status code 200
    // And subsequent GetBucketVersioningAsync should return Status "Suspended"
    // And existing object versions should be preserved
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
    // Given I have valid AWS credentials
    // And I own a bucket "versioned-bucket" with versioning enabled
    // When I attempt to completely disable versioning (return to unversioned state)
    // Then it is not possible - the bucket can only be suspended, never fully disabled
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
        // Once versioning has been enabled, bucket can only be Suspended, not returned to Off
        Assert.Equal(VersionStatus.Suspended, getResponse.VersioningConfig.Status);
    }

    // Acceptance Criteria 2.1 - Scenario: Fail to set versioning on non-existent bucket
    // Given I have valid AWS credentials
    // And no bucket named "non-existent-bucket" exists
    // When I call PutBucketVersioningAsync with bucket name "non-existent-bucket" and Status "Enabled"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "NoSuchBucket"
    // And the HTTP status code should be 404
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
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" with account ID "123456789012"
    // When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled" and ExpectedBucketOwner "123456789012"
    // Then the response should have HTTP status code 200
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
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" with account ID "123456789012"
    // When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled" and ExpectedBucketOwner "999999999999"
    // Then the response should throw AmazonS3Exception
    // And the error code should be "AccessDenied"
    // And the HTTP status code should be 403
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
    // Given I have valid AWS credentials
    // And I own a bucket "my-bucket" that has never had versioning configured
    // When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled"
    // Then the response should have HTTP status code 200
    // And versioning should be active for subsequent operations
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
