using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

namespace AWSSDK.Extensions.SqlLite.AcceptanceTests;

/// <summary>
/// Acceptance tests for MFA Delete configuration and behavior using SqlLiteS3Client.
/// Tests verify MFA Delete functionality for versioned buckets.
/// </summary>
public class MfaDeleteAcceptanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqlLiteS3Client _client;

    public MfaDeleteAcceptanceTests()
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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
        Assert.Equal("true", deleteResponse.DeleteMarker);
    }

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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

    [Fact(Skip = "SqlLite implementation pending")]
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
