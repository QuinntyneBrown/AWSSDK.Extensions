using Amazon.S3;
using Amazon.S3.Model;
using AWSSDK.Extensions;
using FileStorage.Models;

namespace FileStorage.Services;

/// <summary>
/// Implementation of IStorageService using CouchbaseS3Client as the backend.
/// </summary>
public class StorageService : IStorageService
{
    private readonly CouchbaseS3Client _s3Client;
    private bool _disposed;

    public StorageService(string databasePath)
    {
        _s3Client = new CouchbaseS3Client(databasePath);
    }

    public StorageService(CouchbaseS3Client s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<StoredFile> SaveFileAsync(
        string bucketName,
        string key,
        Stream stream,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = memoryStream,
            ContentType = contentType ?? "application/octet-stream"
        };

        var response = await _s3Client.PutObjectAsync(request, cancellationToken);

        return new StoredFile
        {
            BucketName = bucketName,
            Key = key,
            VersionId = response.VersionId,
            ETag = response.ETag,
            Size = memoryStream.Length,
            ContentType = contentType ?? "application/octet-stream",
            LastModified = DateTime.UtcNow
        };
    }

    public async Task<StoredFileWithContent> GetFileAsync(
        string bucketName,
        string key,
        string? versionId = null,
        CancellationToken cancellationToken = default)
    {
        GetObjectResponse response;

        if (!string.IsNullOrEmpty(versionId))
        {
            response = await _s3Client.GetObjectAsync(bucketName, key, versionId, cancellationToken);
        }
        else
        {
            response = await _s3Client.GetObjectAsync(bucketName, key, cancellationToken);
        }

        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return new StoredFileWithContent
        {
            BucketName = bucketName,
            Key = key,
            VersionId = response.VersionId,
            ETag = response.ETag,
            Size = response.ContentLength,
            ContentType = response.Headers.ContentType,
            LastModified = response.LastModified,
            Content = memoryStream
        };
    }

    public async Task<IEnumerable<StoredFile>> GetFilesAsync(
        string bucketName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix
        };

        var response = await _s3Client.ListObjectsV2Async(request, cancellationToken);

        return response.S3Objects.Select(obj => new StoredFile
        {
            BucketName = bucketName,
            Key = obj.Key,
            ETag = obj.ETag,
            Size = obj.Size,
            LastModified = obj.LastModified
        }).ToList();
    }

    public async Task<bool> DeleteFileAsync(
        string bucketName,
        string key,
        string? versionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(versionId))
            {
                await _s3Client.DeleteObjectAsync(bucketName, key, versionId, cancellationToken);
            }
            else
            {
                await _s3Client.DeleteObjectAsync(bucketName, key, cancellationToken);
            }
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3Client.HeadBucketAsync(bucketName, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _s3Client.PutBucketAsync(bucketName, cancellationToken);
        }
    }

    public async Task EnableVersioningAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new PutBucketVersioningRequest
        {
            BucketName = bucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled
            }
        };

        await _s3Client.PutBucketVersioningAsync(request, cancellationToken);
    }

    public async Task<IEnumerable<FileVersion>> GetFileVersionsAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest
        {
            BucketName = bucketName,
            Prefix = key
        };

        var response = await _s3Client.ListVersionsAsync(request, cancellationToken);

        var versions = new List<FileVersion>();

        foreach (var version in response.Versions.Where(v => v.Key == key))
        {
            versions.Add(new FileVersion
            {
                VersionId = version.VersionId,
                Key = version.Key,
                IsLatest = version.IsLatest,
                LastModified = version.LastModified,
                Size = version.Size,
                IsDeleteMarker = false
            });
        }

        foreach (var marker in response.DeleteMarkers.Where(m => m.Key == key))
        {
            versions.Add(new FileVersion
            {
                VersionId = marker.VersionId,
                Key = marker.Key,
                IsLatest = marker.IsLatest,
                LastModified = marker.LastModified,
                Size = 0,
                IsDeleteMarker = true
            });
        }

        return versions.OrderByDescending(v => v.LastModified).ToList();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _s3Client.Dispose();
            _disposed = true;
        }
    }
}
