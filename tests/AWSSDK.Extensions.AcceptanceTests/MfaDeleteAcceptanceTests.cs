using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.AcceptanceTests;

/// <summary>
/// Acceptance tests for MFA Delete configuration and behavior.
/// Tests verify MFA Delete functionality for versioned buckets.
/// </summary>
public class MfaDeleteAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly CouchbaseS3Client _client;

    public MfaDeleteAcceptanceTests()
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

    // Acceptance Criteria 10.1 - Scenario: Enable MFA Delete on a bucket
    // Given I have valid AWS credentials
    // And I am the bucket owner
    // And I have a valid MFA device configured
    // And I own a versioning-enabled bucket "mfa-bucket"
    // When I call PutBucketVersioningAsync with Status "Enabled" and MfaDelete "Enabled"
    // Then the response should have HTTP status code 200
    // And subsequent GetBucketVersioning should show MFADelete "Enabled"
    [Fact]
    public async Task PutBucketVersioningAsync_EnableMfaDelete_Succeeds()
    {
        // Arrange
        var bucketName = "mfa-bucket";
        await _client.PutBucketAsync(bucketName);

        // Act
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = true
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Enabled, getResponse.VersioningConfig.Status);
        Assert.True(getResponse.VersioningConfig.EnableMfaDelete);
    }

    // Acceptance Criteria 10.1 - Scenario: Simple delete (creates delete marker) does not require MFA
    // Given I have valid AWS credentials
    // And I own a bucket "mfa-bucket" with MFA Delete enabled
    // And object "file.txt" exists
    // When I call DeleteObjectAsync without VersionId and without MFA header
    // Then the response should have HTTP status code 204
    // And a delete marker should be created
    [Fact]
    public async Task DeleteObjectAsync_WithMfaDeleteEnabled_SimpleDeleteSucceeds()
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

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act - Simple delete (no version ID) should work without MFA
        var deleteResponse = await _client.DeleteObjectAsync(bucketName, "file.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);
        Assert.True(deleteResponse.DeleteMarker);
    }

    // Acceptance Criteria 10.1 - Scenario: Verify MFA Delete status can be retrieved
    // When MFA Delete is enabled, GetBucketVersioning should report it
    [Fact]
    public async Task GetBucketVersioningAsync_WithMfaDeleteEnabled_ReportsMfaDeleteStatus()
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
        Assert.Equal(VersionStatus.Enabled, response.VersioningConfig.Status);
        Assert.True(response.VersioningConfig.EnableMfaDelete);
    }

    // Acceptance Criteria 10.1 - Scenario: Verify MFA Delete can be disabled
    [Fact]
    public async Task PutBucketVersioningAsync_DisableMfaDelete_Succeeds()
    {
        // Arrange
        var bucketName = "mfa-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable MFA Delete first
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = true
            }
        });

        // Act - Disable MFA Delete
        var response = await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = false
            }
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.HttpStatusCode);

        var getResponse = await _client.GetBucketVersioningAsync(bucketName);
        Assert.Equal(VersionStatus.Enabled, getResponse.VersioningConfig.Status);
        Assert.False(getResponse.VersioningConfig.EnableMfaDelete);
    }

    // Additional test: MFA Delete configuration is independent of versioning status changes
    [Fact]
    public async Task MfaDelete_IndependentOfVersioningStatus()
    {
        // Arrange
        var bucketName = "mfa-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning with MFA Delete
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = true
            }
        });

        // Suspend versioning but keep MFA Delete enabled
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Suspended,
                EnableMfaDelete = true
            }
        });

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(VersionStatus.Suspended, response.VersioningConfig.Status);
        Assert.True(response.VersioningConfig.EnableMfaDelete);
    }

    // Additional test: MFA Delete setting persists after versioning re-enable
    [Fact]
    public async Task MfaDelete_PersistsAfterVersioningReEnable()
    {
        // Arrange
        var bucketName = "mfa-bucket";
        await _client.PutBucketAsync(bucketName);

        // Enable versioning with MFA Delete
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = true
            }
        });

        // Suspend versioning
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Suspended,
                EnableMfaDelete = true
            }
        });

        // Re-enable versioning
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
        Assert.Equal(VersionStatus.Enabled, response.VersioningConfig.Status);
        Assert.True(response.VersioningConfig.EnableMfaDelete);
    }

    // Additional test: Bucket without MFA Delete allows version deletion
    [Fact]
    public async Task DeleteObjectAsync_WithoutMfaDelete_VersionDeletionSucceeds()
    {
        // Arrange
        var bucketName = "no-mfa-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled,
                EnableMfaDelete = false
            }
        });

        var putResponse = await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            ContentBody = "content"
        });

        // Act - Delete specific version (should work without MFA when MFA Delete is not enabled)
        var deleteResponse = await _client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = "file.txt",
            VersionId = putResponse.VersionId
        });

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.HttpStatusCode);

        // Verify version was deleted
        var listResponse = await _client.ListVersionsAsync(bucketName);
        Assert.DoesNotContain(listResponse.Versions, v => v.VersionId == putResponse.VersionId);
    }

    // Additional test: Default MFA Delete is disabled
    [Fact]
    public async Task GetBucketVersioningAsync_DefaultMfaDelete_IsDisabled()
    {
        // Arrange
        var bucketName = "default-bucket";
        await _client.PutBucketAsync(bucketName);
        await _client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
        });

        // Act
        var response = await _client.GetBucketVersioningAsync(bucketName);

        // Assert
        Assert.Equal(VersionStatus.Enabled, response.VersioningConfig.Status);
        Assert.False(response.VersioningConfig.EnableMfaDelete);
    }
}
