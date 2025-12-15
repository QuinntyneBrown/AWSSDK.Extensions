# AWSSDK.Extensions

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-blue)
![Build Status](https://github.com/QuinntyneBrown/AWSSDK.Extensions/workflows/PR%20Tests/badge.svg)

A powerful extension library for AWS SDK that provides local Couchbase Lite implementations of Amazon S3 interfaces. This library enables offline-first development and testing scenarios without requiring AWS infrastructure.

## Features

- **Local S3 Implementation**: Full implementation of core AWS S3 operations using Couchbase Lite as the storage backend
- **Transactional Support**: Enhanced version with atomic operations for multiple object updates
- **Offline-First**: Perfect for development, testing, and scenarios requiring offline capabilities
- **Compatible Interface**: Implements `IAmazonS3` interface for seamless integration with existing AWS SDK code

## Installation

```bash
dotnet add package AWSSDK.Extensions
```

## Components

### CouchbaseS3Client

A Couchbase Lite-based implementation of the Amazon S3 interface that supports:

- **Bucket Operations**: Create, delete, and list buckets
- **Object Operations**: Put, get, delete, and list objects
- **Metadata Support**: Store and retrieve custom metadata with objects
- **Prefix-based Listing**: Efficient object listing with prefix filters
- **ETag Generation**: MD5-based ETag calculation for object integrity

### CouchbaseS3ClientWithTransactions

An enhanced version that adds transactional support:

- **Atomic Multi-Object Operations**: Put or delete multiple objects atomically
- **Copy Operations**: Atomic object copying between buckets
- **Rollback on Failure**: Automatic rollback on transaction failures
- **All CouchbaseS3Client Features**: Includes all base functionality

## Usage

### Basic Usage

```csharp
using AWSSDK.Extensions;
using Amazon.S3.Model;

// Initialize the client with a local database path
var client = new CouchbaseS3Client("/path/to/database.cblite2");

// Create a bucket
await client.PutBucketAsync("my-bucket");

// Put an object
var putRequest = new PutObjectRequest
{
    BucketName = "my-bucket",
    Key = "my-file.txt",
    ContentBody = "Hello, World!"
};
await client.PutObjectAsync(putRequest);

// Get an object
var getResponse = await client.GetObjectAsync("my-bucket", "my-file.txt");
using var reader = new StreamReader(getResponse.ResponseStream);
var content = await reader.ReadToEndAsync();

// List objects
var listResponse = await client.ListObjectsV2Async(new ListObjectsV2Request
{
    BucketName = "my-bucket"
});

// Clean up
client.Dispose();
```

### Transactional Operations

```csharp
using AWSSDK.Extensions;
using Amazon.S3.Model;

var client = new CouchbaseS3ClientWithTransactions("/path/to/database.cblite2");

await client.PutBucketAsync("my-bucket");

// Put multiple objects atomically
var requests = new List<PutObjectRequest>
{
    new() { BucketName = "my-bucket", Key = "file1.txt", ContentBody = "Content 1" },
    new() { BucketName = "my-bucket", Key = "file2.txt", ContentBody = "Content 2" },
    new() { BucketName = "my-bucket", Key = "file3.txt", ContentBody = "Content 3" }
};

// All objects are created atomically - if any fails, none are created
var responses = await client.PutObjectsTransactionalAsync(requests);

// Copy objects atomically
await client.CopyObjectAsync(
    sourceBucket: "my-bucket",
    sourceKey: "file1.txt",
    destinationBucket: "my-bucket",
    destinationKey: "file1-copy.txt"
);

client.Dispose();
```

### Using with Metadata

```csharp
var putRequest = new PutObjectRequest
{
    BucketName = "my-bucket",
    Key = "document.pdf",
    InputStream = fileStream,
    ContentType = "application/pdf"
};

// Add custom metadata
putRequest.Metadata.Add("author", "John Doe");
putRequest.Metadata.Add("document-type", "invoice");

await client.PutObjectAsync(putRequest);

// Retrieve with metadata
var getResponse = await client.GetObjectAsync("my-bucket", "document.pdf");
var author = getResponse.Metadata["author"]; // "John Doe"
```

## Supported Operations

### Bucket Operations
- ✅ `PutBucketAsync` - Create buckets
- ✅ `DeleteBucketAsync` - Delete empty buckets
- ✅ `ListBucketsAsync` - List all buckets

### Object Operations
- ✅ `PutObjectAsync` - Upload objects
- ✅ `GetObjectAsync` - Download objects
- ✅ `DeleteObjectAsync` - Delete single object
- ✅ `DeleteObjectsAsync` - Delete multiple objects
- ✅ `ListObjectsV2Async` - List objects with prefix support
- ✅ `CopyObjectAsync` - Copy objects (Transactions only)

### Metadata
- ✅ Custom metadata storage and retrieval
- ✅ Content-Type support
- ✅ ETag generation

## Development

### Prerequisites

- .NET 9.0 SDK or later
- Couchbase Lite dependencies (automatically managed via NuGet)

### Building

```bash
dotnet build
```

### Running Tests

The project includes comprehensive unit tests using NUnit:

```bash
dotnet test
```

Tests cover:
- Bucket operations (create, delete, list)
- Object operations (put, get, delete)
- Metadata handling
- Error conditions
- Transactional operations
- Edge cases

### Project Structure

```
AWSSDK.Extensions/
├── src/
│   └── AWSSDK.Extensions/
│       ├── CouchbaseS3Implementation.cs        # Base S3 implementation
│       └── CouchbaseS3ClientWithTransactions.cs # Enhanced with transactions
├── tests/
│   └── AWSSDK.Extensions.Tests/
│       ├── CouchbaseS3ClientTests.cs
│       └── CouchbaseS3ClientWithTransactionsTests.cs
└── .github/
    └── workflows/
        └── pr-tests.yml                         # CI/CD pipeline
```

## CI/CD

The project uses GitHub Actions for continuous integration. The workflow:

1. Runs on every pull request and push to main/master branches
2. Restores dependencies
3. Builds the solution in Release configuration
4. Executes all unit tests
5. Reports test results

## Contributing

Contributions are welcome! Please ensure:

1. All tests pass before submitting a PR
2. Add tests for new functionality
3. Follow the existing code style
4. Update documentation as needed

## Use Cases

- **Local Development**: Develop and test S3-dependent code without AWS credentials
- **Integration Testing**: Test S3 interactions in CI/CD pipelines without AWS costs
- **Offline Applications**: Build applications that work offline with S3-like storage
- **Prototyping**: Quickly prototype S3-based solutions locally
- **Education**: Learn AWS S3 concepts without AWS account setup

## Limitations

This is a local implementation for development and testing purposes. Some AWS S3 features are not implemented:

- Versioning
- Access Control Lists (ACLs)
- Bucket policies
- Lifecycle rules
- Multipart uploads
- Pre-signed URLs
- Server-side encryption
- Cross-region replication

For production AWS S3 usage, use the official AWS SDK for .NET.

## License

This project is licensed under the MIT License.

## Acknowledgments

- Built on top of [Couchbase Lite for .NET](https://www.couchbase.com/products/lite)
- Implements interfaces from [AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/)

## Support

For issues, questions, or contributions, please visit the [GitHub repository](https://github.com/QuinntyneBrown/AWSSDK.Extensions).