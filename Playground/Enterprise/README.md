# Enterprise File Storage API

A file storage API backed by AWSSDK.Extensions CouchbaseS3Client for local S3-compatible storage.

## Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022, VS Code, or JetBrains Rider

## Project Structure

```
Enterprise/
├── Enterprise.sln
├── README.md
└── src/
    ├── FileStorage/                    # Web API project
    │   ├── Endpoints/
    │   │   └── FileStorageEndpoints.cs # API endpoint definitions
    │   ├── Models/
    │   │   └── StoredFile.cs           # Data models
    │   ├── Services/
    │   │   ├── IStorageService.cs      # Storage service interface
    │   │   └── StorageService.cs       # CouchbaseS3Client wrapper
    │   ├── Program.cs                  # Application entry point
    │   └── appsettings.json            # Configuration
    └── FileStorage.Tests/              # Integration tests
        ├── FileStorageApiTests.cs      # API endpoint tests
        └── FileStorageWebApplicationFactory.cs
```

## Setup

### 1. Build the Solution

```bash
cd Playground/Enterprise
dotnet restore
dotnet build
```

### 2. Run the API

```bash
cd src/FileStorage
dotnet run
```

The API will start on `http://localhost:5000` (or another available port).

### 3. Access Swagger UI

Open your browser to `http://localhost:5000` to access the Swagger UI for interactive API testing.

## API Endpoints

### Bucket Operations

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/files/buckets/{bucketName}` | Create a new bucket |
| POST | `/api/files/buckets/{bucketName}/versioning` | Enable versioning on a bucket |

### File Operations

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/files/{bucketName}/{*key}` | Upload a file |
| GET | `/api/files/{bucketName}/{*key}` | Download a file |
| GET | `/api/files/{bucketName}` | List files in a bucket |
| DELETE | `/api/files/{bucketName}/{*key}` | Delete a file |
| GET | `/api/files/{bucketName}/{key}/versions` | List file versions |

### Query Parameters

- `prefix` - Filter files by prefix (for listing)
- `versionId` - Get or delete a specific version

## Usage Examples

### Create a Bucket

```bash
curl -X POST http://localhost:5000/api/files/buckets/my-bucket
```

### Enable Versioning

```bash
curl -X POST http://localhost:5000/api/files/buckets/my-bucket/versioning
```

### Upload a File

```bash
curl -X POST http://localhost:5000/api/files/my-bucket/documents/report.pdf \
  -H "Content-Type: application/pdf" \
  --data-binary @report.pdf
```

### Upload Text Content

```bash
curl -X POST http://localhost:5000/api/files/my-bucket/hello.txt \
  -H "Content-Type: text/plain" \
  -d "Hello, World!"
```

### Download a File

```bash
curl http://localhost:5000/api/files/my-bucket/hello.txt
```

### Download a Specific Version

```bash
curl "http://localhost:5000/api/files/my-bucket/hello.txt?versionId=abc123"
```

### List Files

```bash
curl http://localhost:5000/api/files/my-bucket
```

### List Files with Prefix

```bash
curl "http://localhost:5000/api/files/my-bucket?prefix=documents/"
```

### Delete a File

```bash
curl -X DELETE http://localhost:5000/api/files/my-bucket/hello.txt
```

### Delete a Specific Version

```bash
curl -X DELETE "http://localhost:5000/api/files/my-bucket/hello.txt?versionId=abc123"
```

### List File Versions

```bash
curl http://localhost:5000/api/files/my-bucket/hello.txt/versions
```

## IStorageService Interface

The `IStorageService` interface provides the following methods:

```csharp
public interface IStorageService : IDisposable
{
    Task<StoredFile> SaveFileAsync(string bucketName, string key, Stream stream,
        string? contentType = null, CancellationToken cancellationToken = default);

    Task<StoredFileWithContent> GetFileAsync(string bucketName, string key,
        string? versionId = null, CancellationToken cancellationToken = default);

    Task<IEnumerable<StoredFile>> GetFilesAsync(string bucketName,
        string? prefix = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteFileAsync(string bucketName, string key,
        string? versionId = null, CancellationToken cancellationToken = default);

    Task EnsureBucketExistsAsync(string bucketName,
        CancellationToken cancellationToken = default);

    Task EnableVersioningAsync(string bucketName,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<FileVersion>> GetFileVersionsAsync(string bucketName, string key,
        CancellationToken cancellationToken = default);
}
```

## Storage Configuration

Configure the database path in `appsettings.json`:

```json
{
  "Storage": {
    "DatabasePath": "./data/enterprise-storage"
  }
}
```

If not specified, a temporary directory will be used.

## Running Tests

```bash
cd src/FileStorage.Tests
dotnet test
```

Or from the solution root:

```bash
dotnet test Enterprise.sln
```

### Test Coverage

The integration tests cover:

- **Bucket Operations**: Creating buckets, enabling versioning
- **File Upload**: Single files, nested paths, binary content, various content types
- **File Download**: Basic download, version-specific downloads, non-existent files
- **File Listing**: All files, prefix filtering, empty buckets
- **File Deletion**: Basic delete, versioned delete markers, non-existent files
- **Version Management**: Multiple versions, latest version tracking
- **Edge Cases**: Empty files, long filenames, special characters, overwrites

## Architecture

The API uses a layered architecture:

1. **Endpoints** - Minimal API endpoints handle HTTP requests
2. **IStorageService** - Interface for storage operations
3. **StorageService** - Wraps `CouchbaseS3Client` from AWSSDK.Extensions
4. **CouchbaseS3Client** - Implements `IAmazonS3` using Couchbase Lite as backend

This design allows:
- Easy testing with mock implementations
- Swappable storage backends
- Clean separation of concerns

## Default Bucket

A default bucket named `default` is automatically created on startup for convenience.

## Dependencies

- **AWSSDK.Extensions** - Provides `CouchbaseS3Client` for local S3-compatible storage
- **Microsoft.AspNetCore.OpenApi** - Swagger/OpenAPI support
- **NUnit** - Test framework for integration tests
- **Microsoft.AspNetCore.Mvc.Testing** - WebApplicationFactory for API testing
