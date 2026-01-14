# CouchbaseS3Client User Guide

## Overview

The `CouchbaseS3Client` is a local, embedded implementation of the Amazon S3 interface that uses Couchbase Lite for metadata and blob storage. This allows you to develop and test S3-based applications locally without requiring AWS credentials or internet connectivity.

## Features

- **S3-Compatible API**: Implements the `IAmazonS3` interface from AWS SDK
- **Local Storage**: All data stored locally using Couchbase Lite
- **Versioning Support**: Full support for S3 bucket versioning
- **Multipart Upload**: Support for large file uploads
- **Metadata Management**: Store and retrieve object metadata, tags, ACLs
- **Encryption**: Support for bucket encryption configuration
- **No AWS Required**: Works completely offline

## Installation

Add the AWSSDK.Extensions package to your project:

```bash
dotnet add package AWSSDK.Extensions
```

Required dependencies:
- `AWSSDK.S3`
- `Couchbase.Lite`

## Basic Usage

### 1. Initialize the Client

The `CouchbaseS3Client` requires a database path for storing data:

```csharp
using AWSSDK.Extensions;

// Specify the full path including the database name
var databasePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "my-app",
    "s3-storage"
);

// Ensure parent directory exists
Directory.CreateDirectory(Path.GetDirectoryName(databasePath));

// Create the client
var s3Client = new CouchbaseS3Client(databasePath);
```

### 2. Create a Bucket

```csharp
using Amazon.S3.Model;

// Simple bucket creation
await s3Client.PutBucketAsync("my-bucket");

// Or with a request object
var putBucketRequest = new PutBucketRequest
{
    BucketName = "my-bucket"
};
await s3Client.PutBucketAsync(putBucketRequest);
```

### 3. Upload an Object

```csharp
var putRequest = new PutObjectRequest
{
    BucketName = "my-bucket",
    Key = "documents/file.pdf",
    InputStream = fileStream,
    ContentType = "application/pdf"
};

var response = await s3Client.PutObjectAsync(putRequest);
Console.WriteLine($"ETag: {response.ETag}");
Console.WriteLine($"Version ID: {response.VersionId}");
```

### 4. Download an Object

```csharp
// Get the latest version
var getRequest = new GetObjectRequest
{
    BucketName = "my-bucket",
    Key = "documents/file.pdf"
};

using var response = await s3Client.GetObjectAsync(getRequest);
using var fileStream = File.Create("downloaded-file.pdf");
await response.ResponseStream.CopyToAsync(fileStream);

Console.WriteLine($"Content Type: {response.Headers.ContentType}");
Console.WriteLine($"Size: {response.ContentLength} bytes");
```

### 5. List Objects

```csharp
var listRequest = new ListObjectsV2Request
{
    BucketName = "my-bucket",
    Prefix = "documents/",
    MaxKeys = 100
};

var listResponse = await s3Client.ListObjectsV2Async(listRequest);
foreach (var obj in listResponse.S3Objects)
{
    Console.WriteLine($"{obj.Key} - {obj.Size} bytes - {obj.LastModified}");
}
```

### 6. Delete an Object

```csharp
await s3Client.DeleteObjectAsync("my-bucket", "documents/file.pdf");
```

## Using in a .NET API

### Application Startup Configuration

Here's how to integrate CouchbaseS3Client into a .NET web API:

#### Program.cs

```csharp
using AWSSDK.Extensions;
using Amazon.S3;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CouchbaseS3Client
var databaseDirectory = builder.Configuration.GetValue<string>("Storage:DatabasePath")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "my-app-storage"
    );

// Ensure directory exists
Directory.CreateDirectory(databaseDirectory);

// Database path includes the database name
var databasePath = Path.Combine(databaseDirectory, "s3data");

// Register as singleton
builder.Services.AddSingleton<IAmazonS3>(sp =>
    new CouchbaseS3Client(databasePath));

var app = builder.Build();

// Seed buckets in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

    // Ensure required buckets exist
    await EnsureBucketExistsAsync(s3Client, "documents");
    await EnsureBucketExistsAsync(s3Client, "images");
    await EnsureBucketExistsAsync(s3Client, "backups");

    // Enable versioning on specific buckets
    await EnableVersioningAsync(s3Client, "documents");
}

app.Run();

async Task EnsureBucketExistsAsync(IAmazonS3 s3Client, string bucketName)
{
    try
    {
        // Check if bucket exists
        await s3Client.HeadBucketAsync(bucketName);
        Console.WriteLine($"Bucket '{bucketName}' already exists.");
    }
    catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        // Bucket doesn't exist, create it
        await s3Client.PutBucketAsync(bucketName);
        Console.WriteLine($"Created bucket '{bucketName}'.");
    }
}

async Task EnableVersioningAsync(IAmazonS3 s3Client, string bucketName)
{
    var versioningRequest = new Amazon.S3.Model.PutBucketVersioningRequest
    {
        BucketName = bucketName,
        VersioningConfig = new Amazon.S3.Model.S3BucketVersioningConfig
        {
            Status = Amazon.S3.VersionStatus.Enabled
        }
    };

    await s3Client.PutBucketVersioningAsync(versioningRequest);
    Console.WriteLine($"Enabled versioning on bucket '{bucketName}'.");
}
```

#### appsettings.json

```json
{
  "Storage": {
    "DatabasePath": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

#### appsettings.Development.json

```json
{
  "Storage": {
    "DatabasePath": "C:\\Dev\\AppData\\s3-storage"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

### Service Layer Pattern

Create a dedicated storage service that encapsulates S3 operations:

#### IStorageService.cs

```csharp
public interface IStorageService
{
    Task<string> SaveFileAsync(
        string bucketName,
        string key,
        Stream stream,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    Task<Stream> GetFileAsync(
        string bucketName,
        string key,
        string? versionId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteFileAsync(
        string bucketName,
        string key,
        string? versionId = null,
        CancellationToken cancellationToken = default);

    Task EnsureBucketExistsAsync(
        string bucketName,
        CancellationToken cancellationToken = default);
}
```

#### StorageService.cs

```csharp
using Amazon.S3;
using Amazon.S3.Model;

public class StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;

    public StorageService(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<string> SaveFileAsync(
        string bucketName,
        string key,
        Stream stream,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType ?? "application/octet-stream"
        };

        var response = await _s3Client.PutObjectAsync(request, cancellationToken);
        return response.VersionId;
    }

    public async Task<Stream> GetFileAsync(
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

        return memoryStream;
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

    public async Task EnsureBucketExistsAsync(
        string bucketName,
        CancellationToken cancellationToken = default)
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
}
```

### API Endpoint Example

```csharp
// In your endpoint mappings
app.MapPost("/api/files/upload", async (
    IFormFile file,
    [FromQuery] string? key,
    IStorageService storageService) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file uploaded");

    var fileKey = key ?? $"uploads/{Guid.NewGuid()}/{file.FileName}";

    using var stream = file.OpenReadStream();
    var versionId = await storageService.SaveFileAsync(
        "documents",
        fileKey,
        stream,
        file.ContentType
    );

    return Results.Ok(new { Key = fileKey, VersionId = versionId });
});

app.MapGet("/api/files/{*key}", async (
    string key,
    [FromQuery] string? versionId,
    IStorageService storageService) =>
{
    try
    {
        var stream = await storageService.GetFileAsync("documents", key, versionId);
        return Results.File(stream, "application/octet-stream", key.Split('/').Last());
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/files/{*key}", async (
    string key,
    [FromQuery] string? versionId,
    IStorageService storageService) =>
{
    var deleted = await storageService.DeleteFileAsync("documents", key, versionId);
    return deleted ? Results.NoContent() : Results.NotFound();
});
```

## Bucket Seeding Strategies

### Strategy 1: Startup Seeding (Recommended for Development)

Automatically create buckets when the application starts in development mode:

```csharp
if (app.Environment.IsDevelopment())
{
    var s3Client = app.Services.GetRequiredService<IAmazonS3>();

    var requiredBuckets = new[] { "documents", "images", "backups" };

    foreach (var bucket in requiredBuckets)
    {
        await EnsureBucketExistsAsync(s3Client, bucket);
    }
}
```

### Strategy 2: Database Initializer Service

Create a dedicated initializer service:

```csharp
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public class S3DatabaseInitializer : IDatabaseInitializer
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3DatabaseInitializer> _logger;

    public S3DatabaseInitializer(IAmazonS3 s3Client, ILogger<S3DatabaseInitializer> logger)
    {
        _s3Client = s3Client;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var buckets = new Dictionary<string, bool>
        {
            ["documents"] = true,  // Enable versioning
            ["images"] = false,
            ["temp"] = false
        };

        foreach (var (bucketName, enableVersioning) in buckets)
        {
            try
            {
                await _s3Client.HeadBucketAsync(bucketName, cancellationToken);
                _logger.LogInformation("Bucket {BucketName} exists", bucketName);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await _s3Client.PutBucketAsync(bucketName, cancellationToken);
                _logger.LogInformation("Created bucket {BucketName}", bucketName);

                if (enableVersioning)
                {
                    await EnableVersioningAsync(bucketName, cancellationToken);
                    _logger.LogInformation("Enabled versioning on {BucketName}", bucketName);
                }
            }
        }
    }

    private async Task EnableVersioningAsync(string bucketName, CancellationToken cancellationToken)
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
}

// Register in Program.cs
builder.Services.AddSingleton<IDatabaseInitializer, S3DatabaseInitializer>();

// Initialize after app is built
var initializer = app.Services.GetRequiredService<IDatabaseInitializer>();
await initializer.InitializeAsync();
```

### Strategy 3: Health Check with Auto-Initialization

Ensure buckets exist during health checks:

```csharp
public class S3BucketHealthCheck : IHealthCheck
{
    private readonly IAmazonS3 _s3Client;
    private readonly string[] _requiredBuckets;

    public S3BucketHealthCheck(IAmazonS3 s3Client, string[] requiredBuckets)
    {
        _s3Client = s3Client;
        _requiredBuckets = requiredBuckets;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var bucket in _requiredBuckets)
            {
                try
                {
                    await _s3Client.HeadBucketAsync(bucket, cancellationToken);
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Auto-create missing bucket
                    await _s3Client.PutBucketAsync(bucket, cancellationToken);
                }
            }

            return HealthCheckResult.Healthy("All required S3 buckets are available");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("S3 bucket check failed", ex);
        }
    }
}

// Register in Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<S3BucketHealthCheck>("s3_buckets");
```

## Versioning Support

### Enable Versioning on a Bucket

```csharp
var versioningRequest = new PutBucketVersioningRequest
{
    BucketName = "my-bucket",
    VersioningConfig = new S3BucketVersioningConfig
    {
        Status = VersionStatus.Enabled
    }
};

await s3Client.PutBucketVersioningAsync(versioningRequest);
```

### Retrieve Specific Version

```csharp
var response = await s3Client.GetObjectAsync("my-bucket", "file.txt", "version-id-123");
```

### List All Versions of an Object

```csharp
var versionsRequest = new ListVersionsRequest
{
    BucketName = "my-bucket",
    Prefix = "file.txt"
};

var versionsResponse = await s3Client.ListVersionsAsync(versionsRequest);

foreach (var version in versionsResponse.Versions)
{
    Console.WriteLine($"Version {version.VersionId} - {version.LastModified} - Latest: {version.IsLatest}");
}
```

### Delete Specific Version

```csharp
await s3Client.DeleteObjectAsync("my-bucket", "file.txt", "version-id-123");
```

## Advanced Features

### Multipart Upload for Large Files

```csharp
var initRequest = new InitiateMultipartUploadRequest
{
    BucketName = "my-bucket",
    Key = "large-file.zip",
    ContentType = "application/zip"
};

var initResponse = await s3Client.InitiateMultipartUploadAsync(initRequest);
var uploadId = initResponse.UploadId;

try
{
    var partETags = new List<PartETag>();
    const int partSize = 5 * 1024 * 1024; // 5 MB parts

    for (int i = 0; i < totalParts; i++)
    {
        var uploadRequest = new UploadPartRequest
        {
            BucketName = "my-bucket",
            Key = "large-file.zip",
            UploadId = uploadId,
            PartNumber = i + 1,
            InputStream = partStream,
            PartSize = partSize
        };

        var uploadResponse = await s3Client.UploadPartAsync(uploadRequest);
        partETags.Add(new PartETag(i + 1, uploadResponse.ETag));
    }

    var completeRequest = new CompleteMultipartUploadRequest
    {
        BucketName = "my-bucket",
        Key = "large-file.zip",
        UploadId = uploadId,
        PartETags = partETags
    };

    await s3Client.CompleteMultipartUploadAsync(completeRequest);
}
catch
{
    await s3Client.AbortMultipartUploadAsync("my-bucket", "large-file.zip", uploadId);
    throw;
}
```

### Object Metadata and Tags

```csharp
// Set metadata during upload
var putRequest = new PutObjectRequest
{
    BucketName = "my-bucket",
    Key = "file.pdf",
    InputStream = stream,
    Metadata =
    {
        ["x-amz-meta-author"] = "John Doe",
        ["x-amz-meta-department"] = "Engineering"
    }
};

await s3Client.PutObjectAsync(putRequest);

// Retrieve metadata
var metadataResponse = await s3Client.GetObjectMetadataAsync("my-bucket", "file.pdf");
var author = metadataResponse.Metadata["x-amz-meta-author"];

// Add tags
var taggingRequest = new PutObjectTaggingRequest
{
    BucketName = "my-bucket",
    Key = "file.pdf",
    Tagging = new Tagging
    {
        TagSet = new List<Tag>
        {
            new Tag { Key = "Project", Value = "Phoenix" },
            new Tag { Key = "Classification", Value = "Confidential" }
        }
    }
};

await s3Client.PutObjectTaggingAsync(taggingRequest);
```

### Bucket Encryption

```csharp
var encryptionRequest = new PutBucketEncryptionRequest
{
    BucketName = "my-bucket",
    ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
    {
        ServerSideEncryptionRules = new List<ServerSideEncryptionRule>
        {
            new ServerSideEncryptionRule
            {
                ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
                {
                    ServerSideEncryptionAlgorithm = ServerSideEncryptionMethod.AES256
                }
            }
        }
    }
};

await s3Client.PutBucketEncryptionAsync(encryptionRequest);
```

## Best Practices

### 1. Use Dependency Injection

Always register `CouchbaseS3Client` as a singleton service:

```csharp
builder.Services.AddSingleton<IAmazonS3>(sp =>
    new CouchbaseS3Client(databasePath));
```

### 2. Dispose Properly

The client implements `IDisposable`. Ensure proper cleanup:

```csharp
// When registered as singleton, disposal is handled by DI container
// For manual instances:
using var s3Client = new CouchbaseS3Client(databasePath);
```

### 3. Handle Exceptions

Common exceptions to handle:

```csharp
try
{
    await s3Client.GetObjectAsync("my-bucket", "file.txt");
}
catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    // Handle missing object
}
catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
{
    // Handle missing bucket
}
```

### 4. Use Keys with Structure

Organize objects using path-like key patterns:

```csharp
// Good
"users/123/profile.jpg"
"documents/2024/invoices/INV-001.pdf"
"temp/uploads/session-abc/file.txt"

// Avoid flat structure
"file1.txt"
"file2.txt"
```

### 5. Configure Database Location

Use configuration for database paths:

```csharp
// appsettings.json
{
  "Storage": {
    "DatabasePath": "/var/app/storage"
  }
}

// Program.cs
var dbPath = builder.Configuration.GetValue<string>("Storage:DatabasePath")
    ?? Path.Combine(AppContext.BaseDirectory, "storage");
```

### 6. Monitor Database Size

The Couchbase Lite database will grow with usage. Implement cleanup strategies:

```csharp
// Delete old versions
var versionsResponse = await s3Client.ListVersionsAsync(new ListVersionsRequest
{
    BucketName = "my-bucket"
});

var oldVersions = versionsResponse.Versions
    .Where(v => !v.IsLatest && v.LastModified < DateTime.UtcNow.AddDays(-30));

foreach (var version in oldVersions)
{
    await s3Client.DeleteObjectAsync("my-bucket", version.Key, version.VersionId);
}
```

## Testing

### Unit Testing

```csharp
public class FileServiceTests : IDisposable
{
    private readonly CouchbaseS3Client _s3Client;
    private readonly string _testDbPath;

    public FileServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test-db-{Guid.NewGuid()}");
        _s3Client = new CouchbaseS3Client(_testDbPath);
    }

    [Fact]
    public async Task SaveFile_StoresFileSuccessfully()
    {
        // Arrange
        await _s3Client.PutBucketAsync("test-bucket");
        var content = "Test content"u8.ToArray();
        var stream = new MemoryStream(content);

        // Act
        var response = await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "test-bucket",
            Key = "test.txt",
            InputStream = stream
        });

        // Assert
        Assert.NotNull(response.ETag);
    }

    public void Dispose()
    {
        _s3Client?.Dispose();

        // Clean up test database
        if (Directory.Exists(Path.GetDirectoryName(_testDbPath)))
        {
            Directory.Delete(Path.GetDirectoryName(_testDbPath), true);
        }
    }
}
```

### Integration Testing

```csharp
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadFile_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("test"u8.ToArray());
        content.Add(fileContent, "file", "test.txt");

        // Act
        var response = await client.PostAsync("/api/files/upload", content);

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

## Troubleshooting

### Issue: Database Locked

**Problem**: Multiple instances trying to access the same database.

**Solution**: Ensure only one instance of `CouchbaseS3Client` accesses a database path at a time. Use singleton registration in DI.

### Issue: Storage Location Not Found

**Problem**: Directory doesn't exist for database path.

**Solution**: Create parent directory before initializing client:

```csharp
var dbPath = Path.Combine(appData, "storage");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
```

### Issue: Performance Degradation

**Problem**: Slow queries as data grows.

**Solution**: The client automatically creates indexes. Ensure you're using appropriate query patterns:

```csharp
// Good - uses prefix index
var request = new ListObjectsV2Request
{
    BucketName = "my-bucket",
    Prefix = "documents/"
};

// Less efficient - retrieves everything then filters
var allObjects = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
{
    BucketName = "my-bucket"
});
var filtered = allObjects.S3Objects.Where(o => o.Key.StartsWith("documents/"));
```

## Migration from AWS S3

To migrate from AWS S3 to `CouchbaseS3Client` (e.g., for local development):

```csharp
public static class S3Configuration
{
    public static void AddS3Client(this IServiceCollection services, IConfiguration config)
    {
        var useLocal = config.GetValue<bool>("Storage:UseLocal");

        if (useLocal)
        {
            // Use CouchbaseS3Client for local development
            var dbPath = config.GetValue<string>("Storage:DatabasePath")
                ?? Path.Combine(AppContext.BaseDirectory, "s3-storage");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

            services.AddSingleton<IAmazonS3>(sp => new CouchbaseS3Client(dbPath));
        }
        else
        {
            // Use AWS S3 for production
            services.AddAWSService<IAmazonS3>();
        }
    }
}

// In Program.cs
builder.Services.AddS3Client(builder.Configuration);
```

## Performance Considerations

1. **Batch Operations**: Use `DeleteObjectsAsync` for multiple deletions
2. **Streaming**: Use streaming for large files to avoid memory issues
3. **Indexing**: The client creates indexes automatically for efficient queries
4. **Cleanup**: Regularly clean up old versions if versioning is enabled

## Limitations

- **No Server-Side Filtering**: Some advanced S3 features like SELECT queries are not supported
- **No Cross-Region Replication**: All data is local
- **No Bucket Policies**: ACL support is limited compared to AWS S3
- **No Glacier/Storage Classes**: All objects stored with same storage mechanism

## Additional Resources

- AWS S3 SDK Documentation: https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/s3.html
- Couchbase Lite Documentation: https://docs.couchbase.com/couchbase-lite/current/
- Example Implementation: [playground/Enterprise/src/FileStorage](../../playground/Enterprise/src/FileStorage)

## Support

For issues or questions:
- Check the [GitHub repository](https://github.com/anthropics/AWSSDK.Extensions)
- Review example implementations in the playground folder
- Consult AWS S3 SDK documentation for API reference
