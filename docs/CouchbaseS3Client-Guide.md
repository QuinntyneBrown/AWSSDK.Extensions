# CouchbaseS3Client Quick Reference

## Overview
`CouchbaseS3Client` is a local, embedded S3-compatible storage implementation using Couchbase Lite. It implements the `IAmazonS3` interface, enabling S3 API operations with local file storage—ideal for development, testing, offline scenarios, and embedded applications.

## Key Features
- **S3 API Compatible**: Implements AWS S3 interface
- **Local Storage**: Uses Couchbase Lite for metadata and blob storage
- **Versioning Support**: Full S3 versioning (Enabled/Suspended/None)
- **No Cloud Dependency**: Completely offline-capable
- **Automatic Indexing**: Optimized queries for buckets, objects, and prefix searches

## Constructor
```csharp
public CouchbaseS3Client(string databasePath)
```
- **databasePath**: Path to Couchbase Lite database file (e.g., `"C:\\data\\s3storage.cblite2"`)
- Automatically creates indexes on initialization

## Core Operations

### Buckets
- `PutBucketAsync(bucketName)` - Create bucket
- `DeleteBucketAsync(bucketName)` - Delete empty bucket
- `ListBucketsAsync()` - List all buckets
- `HeadBucketAsync(bucketName)` - Check bucket existence
- `PutBucketVersioningAsync(request)` - Enable/suspend versioning
- `GetBucketVersioningAsync(bucketName)` - Get versioning status

### Objects
- `PutObjectAsync(request)` - Upload object (supports versioning)
- `GetObjectAsync(bucketName, key)` - Download object
- `GetObjectAsync(request)` - Get specific version via `VersionId`
- `DeleteObjectAsync(bucketName, key)` - Delete object (creates delete marker if versioned)
- `DeleteObjectAsync(request)` - Permanently delete version via `VersionId`
- `CopyObjectAsync(request)` - Copy object (supports cross-bucket, versioning)
- `ListObjectsV2Async(request)` - List objects with pagination
- `ListVersionsAsync(request)` - List all versions of objects
- `GetObjectMetadataAsync(request)` - Get metadata only
- `DeleteObjectsAsync(request)` - Batch delete with partial failure handling

### Conditionals
- `IfMatch` / `IfNoneMatch` (ETag-based)
- `IfModifiedSince` / `IfUnmodifiedSince` (timestamp-based)
- `IfVersionIdMatch` (version-specific operations)

## Basic Usage

### Initialize Client
```csharp
var client = new CouchbaseS3Client("C:\\data\\myapp.cblite2");
```

### Create Bucket and Upload
```csharp
await client.PutBucketAsync("my-bucket");
var putRequest = new PutObjectRequest
{
    BucketName = "my-bucket",
    Key = "files/document.txt",
    ContentType = "text/plain",
    InputStream = File.OpenRead("local-file.txt")
};
await client.PutObjectAsync(putRequest);
```

### Download Object
```csharp
var getRequest = new GetObjectRequest
{
    BucketName = "my-bucket",
    Key = "files/document.txt"
};
var response = await client.GetObjectAsync(getRequest);
using var fileStream = File.Create("downloaded.txt");
response.ResponseStream.CopyTo(fileStream);
```

### Enable Versioning
```csharp
var versioningRequest = new PutBucketVersioningRequest
{
    BucketName = "my-bucket",
    VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
};
await client.PutBucketVersioningAsync(versioningRequest);
```

## Pre-Populating Buckets on API Startup

### Startup Configuration (ASP.NET Core)
```csharp
// Program.cs or Startup.cs
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Register as singleton for lifecycle management
        builder.Services.AddSingleton<IAmazonS3>(sp =>
        {
            var dbPath = Path.Combine(
                builder.Configuration["StoragePath"] ?? "C:\\data",
                "s3storage.cblite2"
            );
            return new CouchbaseS3Client(dbPath);
        });
        
        var app = builder.Build();
        
        // Pre-populate buckets
        await InitializeS3StorageAsync(app.Services);
        
        app.MapControllers();
        app.Run();
    }
    
    private static async Task InitializeS3StorageAsync(IServiceProvider services)
    {
        var s3Client = services.GetRequiredService<IAmazonS3>();
        
        // Define buckets to create
        var buckets = new[] { "uploads", "documents", "images", "archives" };
        
        // Create buckets if they don't exist
        foreach (var bucketName in buckets)
        {
            try
            {
                await s3Client.HeadBucketAsync(bucketName);
                Console.WriteLine($"Bucket '{bucketName}' already exists");
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                await s3Client.PutBucketAsync(bucketName);
                Console.WriteLine($"Created bucket '{bucketName}'");
                
                // Optionally enable versioning
                if (bucketName == "documents")
                {
                    await s3Client.PutBucketVersioningAsync(new PutBucketVersioningRequest
                    {
                        BucketName = bucketName,
                        VersioningConfig = new S3BucketVersioningConfig 
                        { 
                            Status = VersionStatus.Enabled 
                        }
                    });
                    Console.WriteLine($"Enabled versioning on '{bucketName}'");
                }
            }
        }
        
        // Optionally seed initial files
        await SeedInitialFilesAsync(s3Client);
    }
    
    private static async Task SeedInitialFilesAsync(IAmazonS3 s3Client)
    {
        var seedFiles = new[]
        {
            new { Bucket = "documents", Key = "templates/default.docx", Path = "seeds/default.docx" },
            new { Bucket = "images", Key = "placeholder.png", Path = "seeds/placeholder.png" }
        };
        
        foreach (var seed in seedFiles)
        {
            if (!File.Exists(seed.Path)) continue;
            
            try
            {
                await s3Client.GetObjectMetadataAsync(seed.Bucket, seed.Key);
                // File exists, skip
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NotFound")
            {
                await s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = seed.Bucket,
                    Key = seed.Key,
                    FilePath = seed.Path
                });
                Console.WriteLine($"Seeded file '{seed.Key}' in bucket '{seed.Bucket}'");
            }
        }
    }
}
```

### Alternative: Hosted Service Pattern
```csharp
public class S3InitializationService : IHostedService
{
    private readonly IAmazonS3 _s3Client;
    
    public S3InitializationService(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bucketsToCreate = new[] { "uploads", "temp", "archive" };
        
        foreach (var bucket in bucketsToCreate)
        {
            try
            {
                await _s3Client.PutBucketAsync(bucket, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists")
            {
                // Ignore if already exists
            }
        }
    }
    
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Register in Program.cs
builder.Services.AddHostedService<S3InitializationService>();
```

## Configuration Best Practices

### appsettings.json
```json
{
  "S3Storage": {
    "DatabasePath": "C:\\data\\s3storage.cblite2",
    "PrePopulateBuckets": ["uploads", "documents", "images"],
    "VersionedBuckets": ["documents"],
    "SeedDataPath": "seeds"
  }
}
```

### Dependency Injection Pattern
```csharp
// Controller usage
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    
    public FilesController(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = "uploads",
            Key = $"{DateTime.UtcNow:yyyy/MM/dd}/{file.FileName}",
            InputStream = stream,
            ContentType = file.ContentType
        });
        return Ok();
    }
}
```

## Versioning Behavior

### Unversioned Bucket
- New `PutObject` overwrites existing object
- `DeleteObject` permanently removes object
- No `VersionId` in responses

### Versioned Bucket (Enabled)
- Each `PutObject` creates new version with unique `VersionId`
- `DeleteObject` creates delete marker (object appears deleted)
- Delete version permanently with `DeleteObjectAsync(new DeleteObjectRequest { VersionId = "..." })`
- `ListVersionsAsync` shows all versions

### Versioned Bucket (Suspended)
- New objects get `VersionId = "null"`
- Previous versions remain accessible
- New overwrites replace current "null" version

## Internal Structure

### Document Types
- **Bucket**: `bucket::{bucketName}` - stores bucket metadata, versioning config
- **Object**: `object::{bucketName}::{key}` - stores current/latest object
- **Version**: `version::{bucketName}::{key}::{versionId}` - stores archived versions

### Indexes
- `idx_bucket`: bucketName, type
- `idx_objects`: bucketName, key, type
- `idx_prefix`: bucketName, prefix, type (for efficient prefix queries)

## Error Handling
All operations throw `AmazonS3Exception` with standard S3 error codes:
- `NoSuchBucket` - Bucket doesn't exist
- `NoSuchKey` - Object doesn't exist
- `BucketAlreadyExists` - Bucket creation conflict
- `BucketNotEmpty` - Cannot delete non-empty bucket
- `PreconditionFailed` - Conditional request failed
- `NoSuchVersion` - Requested version doesn't exist

## Disposal
```csharp
// Implements IDisposable
using var client = new CouchbaseS3Client("data.cblite2");
// ... use client
// Automatically disposed, database closed
```

## Limitations
- No AWS-specific features (IAM, encryption at rest via KMS, cross-region)
- No multipart uploads (throws `NotImplementedException`)
- Pre-signed URLs are placeholders
- Object Lock features throw `NotImplementedException`
- Local storage only (no replication to AWS S3)

## Use Cases
✅ Development/testing environments  
✅ Offline-first applications  
✅ Desktop/mobile apps needing S3-like storage  
✅ CI/CD pipelines (no cloud dependencies)  
✅ Embedded systems with limited connectivity  
✅ Local file management with S3 API compatibility
