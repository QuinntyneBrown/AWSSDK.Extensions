using Amazon.S3;
using Amazon.S3.Model;
using AWSSDK.Extensions;
using System.Text;

namespace FileCrudWithVersioning;

class Program
{
    private const string BucketName = "demo-versioned-bucket";
    private static CouchbaseS3Client _s3Client = null!;

    static async Task Main(string[] args)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "FileCrudWithVersioningDemo", "s3demo");

        // Ensure clean start - with retry for locked files
        var dbDir = Path.GetDirectoryName(databasePath)!;
        if (Directory.Exists(dbDir))
        {
            try
            {
                Directory.Delete(dbDir, true);
            }
            catch (UnauthorizedAccessException)
            {
                // Files may be locked - wait and retry
                Console.WriteLine("Waiting for locked files to be released...");
                await Task.Delay(1000);
                try
                {
                    Directory.Delete(dbDir, true);
                }
                catch
                {
                    // Use a new unique directory if still locked
                    databasePath = Path.Combine(Path.GetTempPath(), "FileCrudWithVersioningDemo", $"s3demo_{DateTime.Now.Ticks}");
                    dbDir = Path.GetDirectoryName(databasePath)!;
                }
            }
        }
        Directory.CreateDirectory(dbDir);

        Console.WriteLine("=== File CRUD with Versioning Demo ===\n");
        Console.WriteLine($"Database path: {databasePath}\n");

        using (_s3Client = new CouchbaseS3Client(databasePath))
        {
            await DemoBucketOperations();
            await DemoVersioningSetup();
            await DemoFileOperationsWithVersioning();
            await DemoPresignedUrls();
            await DemoVersionRetrieval();
            await DemoDeleteWithVersioning();
            await DemoListVersions();
            await DemoCleanup();
        }

        Console.WriteLine("\n=== Demo Complete ===");
    }

    static async Task DemoBucketOperations()
    {
        PrintSection("1. Bucket Operations");

        // Create bucket
        Console.WriteLine($"Creating bucket: {BucketName}");
        var putBucketResponse = await _s3Client.PutBucketAsync(BucketName);
        Console.WriteLine($"  Result: {putBucketResponse.HttpStatusCode}");

        // List buckets
        Console.WriteLine("\nListing all buckets:");
        var listBucketsResponse = await _s3Client.ListBucketsAsync();
        foreach (var bucket in listBucketsResponse.Buckets)
        {
            Console.WriteLine($"  - {bucket.BucketName} (Created: {bucket.CreationDate})");
        }

        // Head bucket
        Console.WriteLine($"\nChecking bucket exists (HEAD):");
        var headBucketResponse = await _s3Client.HeadBucketAsync(BucketName);
        Console.WriteLine($"  Bucket '{BucketName}' exists: {headBucketResponse.HttpStatusCode == System.Net.HttpStatusCode.OK}");
    }

    static async Task DemoVersioningSetup()
    {
        PrintSection("2. Versioning Setup");

        // Check initial versioning status
        Console.WriteLine("Checking initial versioning status:");
        var getVersioningResponse = await _s3Client.GetBucketVersioningAsync(new GetBucketVersioningRequest
        {
            BucketName = BucketName
        });
        Console.WriteLine($"  Status: {getVersioningResponse.VersioningConfig.Status ?? "Off"}");

        // Enable versioning
        Console.WriteLine("\nEnabling versioning on bucket:");
        var putVersioningResponse = await _s3Client.PutBucketVersioningAsync(new PutBucketVersioningRequest
        {
            BucketName = BucketName,
            VersioningConfig = new S3BucketVersioningConfig
            {
                Status = VersionStatus.Enabled
            }
        });
        Console.WriteLine($"  Result: {putVersioningResponse.HttpStatusCode}");

        // Verify versioning is enabled
        getVersioningResponse = await _s3Client.GetBucketVersioningAsync(new GetBucketVersioningRequest
        {
            BucketName = BucketName
        });
        Console.WriteLine($"  New Status: {getVersioningResponse.VersioningConfig.Status}");
    }

    static async Task DemoFileOperationsWithVersioning()
    {
        PrintSection("3. File Operations with Versioning");

        var key = "documents/readme.txt";

        // Version 1
        Console.WriteLine($"Uploading first version of '{key}':");
        var content1 = "This is version 1 of the readme file.";
        var putResponse1 = await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentBody = content1,
            ContentType = "text/plain",
            Metadata = { ["author"] = "Alice", ["revision"] = "1" }
        });
        Console.WriteLine($"  Version ID: {putResponse1.VersionId}");
        Console.WriteLine($"  ETag: {putResponse1.ETag}");
        var version1Id = putResponse1.VersionId;

        // Version 2
        Console.WriteLine($"\nUploading second version of '{key}':");
        var content2 = "This is version 2 of the readme file.\nAdded more content here.";
        var putResponse2 = await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentBody = content2,
            ContentType = "text/plain",
            Metadata = { ["author"] = "Bob", ["revision"] = "2" }
        });
        Console.WriteLine($"  Version ID: {putResponse2.VersionId}");
        Console.WriteLine($"  ETag: {putResponse2.ETag}");
        var version2Id = putResponse2.VersionId;

        // Version 3
        Console.WriteLine($"\nUploading third version of '{key}':");
        var content3 = "This is version 3 of the readme file.\nMore content added.\nFinal revision.";
        var putResponse3 = await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentBody = content3,
            ContentType = "text/plain",
            Metadata = { ["author"] = "Charlie", ["revision"] = "3" }
        });
        Console.WriteLine($"  Version ID: {putResponse3.VersionId}");
        Console.WriteLine($"  ETag: {putResponse3.ETag}");

        // Upload another file
        Console.WriteLine("\nUploading another file 'documents/notes.txt':");
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = "documents/notes.txt",
            ContentBody = "Some notes here.",
            ContentType = "text/plain"
        });
        Console.WriteLine("  Uploaded successfully");

        // Upload a binary file
        Console.WriteLine("\nUploading binary file 'images/logo.png':");
        var binaryContent = Encoding.UTF8.GetBytes("PNG-MOCK-BINARY-DATA-HERE");
        using var binaryStream = new MemoryStream(binaryContent);
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = "images/logo.png",
            InputStream = binaryStream,
            ContentType = "image/png"
        });
        Console.WriteLine("  Uploaded successfully");
    }

    static async Task DemoPresignedUrls()
    {
        PrintSection("4. Pre-Signed URL Generation");

        var key = "documents/readme.txt";

        // GET presigned URL
        Console.WriteLine($"Generating GET presigned URL for '{key}':");
        var getUrl = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        });
        Console.WriteLine($"  URL: {getUrl}");

        // Validate the URL
        var isValid = _s3Client.ValidatePreSignedURL(getUrl);
        Console.WriteLine($"  Valid: {isValid}");

        // PUT presigned URL
        Console.WriteLine($"\nGenerating PUT presigned URL for 'uploads/new-file.txt':");
        var putUrl = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key = "uploads/new-file.txt",
            Expires = DateTime.UtcNow.AddMinutes(30),
            Verb = HttpVerb.PUT
        });
        Console.WriteLine($"  URL: {putUrl}");

        // DELETE presigned URL
        Console.WriteLine($"\nGenerating DELETE presigned URL for '{key}':");
        var deleteUrl = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Verb = HttpVerb.DELETE
        });
        Console.WriteLine($"  URL: {deleteUrl}");

        // Presigned URL with version ID
        Console.WriteLine($"\nGenerating presigned URL with specific version:");
        var listVersionsResponse = await _s3Client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = BucketName,
            Prefix = key
        });

        if (listVersionsResponse.Versions.Count > 1)
        {
            var oldVersion = listVersionsResponse.Versions[1]; // Get an older version
            var versionedUrl = await _s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = key,
                Expires = DateTime.UtcNow.AddHours(1),
                Verb = HttpVerb.GET,
                VersionId = oldVersion.VersionId
            });
            Console.WriteLine($"  Version: {oldVersion.VersionId}");
            Console.WriteLine($"  URL: {versionedUrl}");
        }
    }

    static async Task DemoVersionRetrieval()
    {
        PrintSection("5. Retrieving Specific Versions");

        var key = "documents/readme.txt";

        // List all versions
        var listVersionsResponse = await _s3Client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = BucketName,
            Prefix = key
        });

        Console.WriteLine($"All versions of '{key}':");
        foreach (var version in listVersionsResponse.Versions)
        {
            Console.WriteLine($"  - Version: {version.VersionId}, Size: {version.Size} bytes, " +
                              $"Modified: {version.LastModified}, IsLatest: {version.IsLatest}");
        }

        // Get current version (latest)
        Console.WriteLine($"\nRetrieving current (latest) version:");
        var getCurrentResponse = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = BucketName,
            Key = key
        });
        using (var reader = new StreamReader(getCurrentResponse.ResponseStream))
        {
            var content = await reader.ReadToEndAsync();
            Console.WriteLine($"  Version ID: {getCurrentResponse.VersionId}");
            Console.WriteLine($"  Content: {content.Substring(0, Math.Min(50, content.Length))}...");
        }

        // Get specific older version
        if (listVersionsResponse.Versions.Count >= 2)
        {
            var olderVersion = listVersionsResponse.Versions.First(v => !v.IsLatest);
            Console.WriteLine($"\nRetrieving older version ({olderVersion.VersionId}):");
            var getOlderResponse = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = BucketName,
                Key = key,
                VersionId = olderVersion.VersionId
            });
            using (var reader = new StreamReader(getOlderResponse.ResponseStream))
            {
                var content = await reader.ReadToEndAsync();
                Console.WriteLine($"  Content: {content.Substring(0, Math.Min(50, content.Length))}...");
            }
        }

        // Get metadata of specific version
        Console.WriteLine($"\nRetrieving metadata:");
        var metadataResponse = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = BucketName,
            Key = key
        });
        Console.WriteLine($"  Content-Type: {metadataResponse.Headers.ContentType}");
        Console.WriteLine($"  Content-Length: {metadataResponse.Headers.ContentLength}");
        Console.WriteLine($"  ETag: {metadataResponse.ETag}");
        Console.WriteLine($"  Last-Modified: {metadataResponse.LastModified}");
        Console.WriteLine($"  Version ID: {metadataResponse.VersionId}");
        foreach (var metaKey in metadataResponse.Metadata.Keys)
        {
            Console.WriteLine($"  Metadata[{metaKey}]: {metadataResponse.Metadata[metaKey]}");
        }
    }

    static async Task DemoDeleteWithVersioning()
    {
        PrintSection("6. Delete Operations with Versioning");

        var key = "documents/readme.txt";

        // Delete without version ID creates a delete marker
        Console.WriteLine($"Deleting '{key}' without version ID (creates delete marker):");
        var deleteResponse = await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = BucketName,
            Key = key
        });
        Console.WriteLine($"  Delete Marker: {deleteResponse.DeleteMarker}");
        Console.WriteLine($"  Version ID (of delete marker): {deleteResponse.VersionId}");

        // Try to get the object (should fail or return delete marker)
        Console.WriteLine($"\nTrying to GET deleted object:");
        try
        {
            await _s3Client.GetObjectAsync(BucketName, key);
            Console.WriteLine("  Object still accessible");
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            Console.WriteLine($"  Result: Object not found (delete marker in place)");
        }

        // List versions to see delete marker
        Console.WriteLine($"\nListing versions after delete:");
        var versionsResponse = await _s3Client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = BucketName,
            Prefix = key
        });
        var deleteMarkersInVersions = versionsResponse.Versions.Where(v => v.IsDeleteMarker).ToList();
        var objectVersions = versionsResponse.Versions.Where(v => !v.IsDeleteMarker).ToList();
        Console.WriteLine($"  Object versions: {objectVersions.Count}");
        Console.WriteLine($"  Delete markers: {deleteMarkersInVersions.Count}");
        foreach (var dm in deleteMarkersInVersions)
        {
            Console.WriteLine($"    - Delete Marker Version: {dm.VersionId}, IsLatest: {dm.IsLatest}");
        }

        // Restore by deleting the delete marker
        if (deleteMarkersInVersions.Any(dm => dm.IsLatest))
        {
            var deleteMarker = deleteMarkersInVersions.First(dm => dm.IsLatest);
            Console.WriteLine($"\nRestoring object by deleting the delete marker:");
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = key,
                VersionId = deleteMarker.VersionId
            });

            // Verify restoration
            var restored = await _s3Client.GetObjectAsync(BucketName, key);
            using var reader = new StreamReader(restored.ResponseStream);
            var content = await reader.ReadToEndAsync();
            Console.WriteLine($"  Object restored! Current content: {content.Substring(0, Math.Min(40, content.Length))}...");
        }
    }

    static async Task DemoListVersions()
    {
        PrintSection("7. List Operations with Versions");

        // List all objects
        Console.WriteLine("Listing all objects (current versions):");
        var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = BucketName
        });
        foreach (var obj in listResponse.S3Objects)
        {
            Console.WriteLine($"  - {obj.Key} ({obj.Size} bytes)");
        }

        // List with prefix
        Console.WriteLine("\nListing objects with prefix 'documents/':");
        var prefixResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = "documents/"
        });
        foreach (var obj in prefixResponse.S3Objects)
        {
            Console.WriteLine($"  - {obj.Key}");
        }

        // List all versions
        Console.WriteLine("\nListing ALL versions in bucket:");
        var allVersionsResponse = await _s3Client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = BucketName
        });
        var allDeleteMarkers = allVersionsResponse.Versions.Where(v => v.IsDeleteMarker).ToList();
        var allObjectVersions = allVersionsResponse.Versions.Where(v => !v.IsDeleteMarker).ToList();
        Console.WriteLine($"  Total object versions: {allObjectVersions.Count}");
        Console.WriteLine($"  Total delete markers: {allDeleteMarkers.Count}");

        var grouped = allObjectVersions.GroupBy(v => v.Key);
        foreach (var group in grouped)
        {
            Console.WriteLine($"\n  Key: {group.Key}");
            foreach (var version in group)
            {
                var latestMarker = version.IsLatest ? " [LATEST]" : "";
                Console.WriteLine($"    - {version.VersionId} ({version.Size} bytes){latestMarker}");
            }
        }
    }

    static async Task DemoCleanup()
    {
        PrintSection("8. Cleanup");

        // Delete all versions and delete markers
        Console.WriteLine("Deleting all objects and versions:");
        var allVersions = await _s3Client.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = BucketName
        });

        foreach (var version in allVersions.Versions)
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = version.Key,
                VersionId = version.VersionId
            });
            var typeLabel = version.IsDeleteMarker ? "delete marker" : "version";
            Console.WriteLine($"  Deleted {typeLabel}: {version.Key} (version: {version.VersionId})");
        }

        // Delete bucket
        Console.WriteLine($"\nDeleting bucket '{BucketName}':");
        var deleteResponse = await _s3Client.DeleteBucketAsync(BucketName);
        Console.WriteLine($"  Result: {deleteResponse.HttpStatusCode}");

        // Verify bucket is gone
        Console.WriteLine("\nFinal bucket list:");
        var finalBuckets = await _s3Client.ListBucketsAsync();
        if (finalBuckets.Buckets.Count == 0)
        {
            Console.WriteLine("  (no buckets)");
        }
        else
        {
            foreach (var bucket in finalBuckets.Buckets)
            {
                Console.WriteLine($"  - {bucket.BucketName}");
            }
        }
    }

    static void PrintSection(string title)
    {
        Console.WriteLine($"\n{'=',-50}");
        Console.WriteLine($" {title}");
        Console.WriteLine($"{'=',-50}\n");
    }
}
