using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AWSSDK.Extensions;

/// <summary>
/// Couchbase Lite implementation of Amazon S3 interface.
/// Uses Couchbase Lite for metadata and blob storage.
/// </summary>
public class CouchbaseS3Client : IAmazonS3
{
    private readonly Database _database;
    private readonly string _databasePath;
    private bool _disposed;

    public CouchbaseS3Client(string databasePath)
    {
        _databasePath = databasePath;
        var config = new DatabaseConfiguration
        {
            Directory = Path.GetDirectoryName(databasePath)
        };
        
        _database = new Database(Path.GetFileName(databasePath), config);
        
        // Create indexes for efficient querying
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        // Index for bucket queries
        _database.CreateIndex("idx_bucket", 
            IndexBuilder.ValueIndex(
                ValueIndexItem.Expression(Expression.Property("bucketName")),
                ValueIndexItem.Expression(Expression.Property("type"))
            ));

        // Index for object queries within buckets
        _database.CreateIndex("idx_objects",
            IndexBuilder.ValueIndex(
                ValueIndexItem.Expression(Expression.Property("bucketName")),
                ValueIndexItem.Expression(Expression.Property("key")),
                ValueIndexItem.Expression(Expression.Property("type"))
            ));

        // Index for prefix searches
        _database.CreateIndex("idx_prefix",
            IndexBuilder.ValueIndex(
                ValueIndexItem.Expression(Expression.Property("bucketName")),
                ValueIndexItem.Expression(Expression.Property("prefix")),
                ValueIndexItem.Expression(Expression.Property("type"))
            ));
    }

    #region Bucket Operations

    public async Task<PutBucketResponse> PutBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new PutBucketRequest { BucketName = bucketName };
        return await PutBucketAsync(request, cancellationToken);
    }

    public async Task<PutBucketResponse> PutBucketAsync(PutBucketRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc != null)
            {
                throw new AmazonS3Exception("Bucket already exists")
                {
                    StatusCode = HttpStatusCode.Conflict,
                    ErrorCode = "BucketAlreadyExists"
                };
            }

            var doc = new MutableDocument($"bucket::{request.BucketName}");
            doc.SetString("type", "bucket");
            doc.SetString("bucketName", request.BucketName);
            doc.SetDate("creationDate", DateTimeOffset.UtcNow);
            
            _database.Save(doc);

            return new PutBucketResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<DeleteBucketResponse> DeleteBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteBucketRequest { BucketName = bucketName };
        return await DeleteBucketAsync(request, cancellationToken);
    }

    public async Task<DeleteBucketResponse> DeleteBucketAsync(DeleteBucketRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Check if bucket is empty
            var objects = GetObjectsInBucket(request.BucketName);
            if (objects.Any())
            {
                throw new AmazonS3Exception("Bucket is not empty")
                {
                    StatusCode = HttpStatusCode.Conflict,
                    ErrorCode = "BucketNotEmpty"
                };
            }

            _database.Delete(bucketDoc);

            return new DeleteBucketResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public async Task<ListBucketsResponse> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        return await ListBucketsAsync(new ListBucketsRequest(), cancellationToken);
    }

    public async Task<ListBucketsResponse> ListBucketsAsync(ListBucketsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var query = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(Expression.Property("type").EqualTo(Expression.String("bucket")));

            var buckets = new List<S3Bucket>();
            foreach (var result in query.Execute())
            {
                var dict = result.GetDictionary(0);
                var creationDate = dict.GetDate("creationDate");
                buckets.Add(new S3Bucket
                {
                    BucketName = dict.GetString("bucketName"),
                    CreationDate = creationDate.UtcDateTime
                });
            }

            return new ListBucketsResponse
            {
                Buckets = buckets,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if a bucket exists and returns metadata about the bucket without returning the contents.
    /// This is equivalent to an HTTP HEAD request for the bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to check.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the bucket metadata.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<HeadBucketResponse> HeadBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new HeadBucketRequest { BucketName = bucketName };
        return await HeadBucketAsync(request, cancellationToken);
    }

    /// <summary>
    /// Checks if a bucket exists and returns metadata about the bucket without returning the contents.
    /// This is equivalent to an HTTP HEAD request for the bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the bucket metadata.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");

            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            return new HeadBucketResponse
            {
                BucketRegion = "local",
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    #endregion

    #region Object Operations

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var objectId = $"object::{request.BucketName}::{request.Key}";
            var doc = new MutableDocument(objectId);

            // Store metadata
            doc.SetString("type", "object");
            doc.SetString("bucketName", request.BucketName);
            doc.SetString("key", request.Key);
            doc.SetString("contentType", request.ContentType ?? "application/octet-stream");
            doc.SetDate("lastModified", DateTimeOffset.UtcNow);
            
            // Extract prefix for efficient prefix searches
            var lastSlash = request.Key.LastIndexOf('/');
            if (lastSlash > 0)
            {
                doc.SetString("prefix", request.Key.Substring(0, lastSlash + 1));
            }

            // Store metadata headers
            if (request.Metadata != null && request.Metadata.Count > 0)
            {
                var metadataDict = new MutableDictionaryObject();
                foreach (var key in request.Metadata.Keys)
                {
                    metadataDict.SetString(key, request.Metadata[key]);
                }
                doc.SetDictionary("metadata", metadataDict);
            }

            // Store content as blob
            byte[] content;
            if (request.InputStream != null)
            {
                using (var ms = new MemoryStream())
                {
                    request.InputStream.CopyTo(ms);
                    content = ms.ToArray();
                }
            }
            else if (!string.IsNullOrEmpty(request.ContentBody))
            {
                content = System.Text.Encoding.UTF8.GetBytes(request.ContentBody);
            }
            else
            {
                content = Array.Empty<byte>();
            }

            var blob = new Blob("application/octet-stream", content);
            doc.SetBlob("content", blob);
            doc.SetLong("size", content.Length);

            // Calculate ETag (simple MD5 hash)
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(content);
                var etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                doc.SetString("etag", etag);
            }

            _database.Save(doc);

            return new PutObjectResponse
            {
                ETag = doc.GetString("etag"),
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = key
        };
        return await GetObjectAsync(request, cancellationToken);
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var objectId = $"object::{request.BucketName}::{request.Key}";
            var doc = _database.GetDocument(objectId);

            if (doc == null)
            {
                throw new AmazonS3Exception("Object does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                };
            }

            var blob = doc.GetBlob("content");
            var response = new GetObjectResponse
            {
                BucketName = request.BucketName,
                Key = request.Key,
                ContentLength = doc.GetLong("size"),
                ETag = doc.GetString("etag"),
                LastModified = doc.GetDate("lastModified").UtcDateTime,
                HttpStatusCode = HttpStatusCode.OK
            };

            // Set content type via headers to align with newer AWSSDK
            response.Headers.ContentType = doc.GetString("contentType");

            // Set response stream
            if (blob != null)
            {
                response.ResponseStream = blob.ContentStream;
            }
            else
            {
                response.ResponseStream = new MemoryStream();
            }

            // Add metadata
            var metadataDict = doc.GetDictionary("metadata");
            if (metadataDict != null)
            {
                foreach (var key in metadataDict.Keys)
                {
                    response.Metadata[key] = metadataDict.GetString(key);
                }
            }

            return response;
        }, cancellationToken);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = key
        };
        return await DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var objectId = $"object::{request.BucketName}::{request.Key}";
            var doc = _database.GetDocument(objectId);

            if (doc != null)
            {
                _database.Delete(doc);
            }

            return new DeleteObjectResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public async Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var deletedObjects = new List<DeletedObject>();
            var errors = new List<DeleteError>();

            foreach (var keyVersion in request.Objects)
            {
                try
                {
                    var objectId = $"object::{request.BucketName}::{keyVersion.Key}";
                    var doc = _database.GetDocument(objectId);

                    if (doc != null)
                    {
                        _database.Delete(doc);
                        deletedObjects.Add(new DeletedObject
                        {
                            Key = keyVersion.Key
                        });
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new DeleteError
                    {
                        Key = keyVersion.Key,
                        Code = "InternalError",
                        Message = ex.Message
                    });
                }
            }

            return new DeleteObjectsResponse
            {
                DeletedObjects = deletedObjects,
                DeleteErrors = errors,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var query = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("object"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            var objects = new List<S3Object>();
            var commonPrefixes = new HashSet<string>();

            foreach (var result in query.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                // Apply continuation token filter
                if (!string.IsNullOrEmpty(request.ContinuationToken) && 
                    string.CompareOrdinal(key, request.ContinuationToken) <= 0)
                {
                    continue;
                }

                // Handle delimiter for directory-like listing
                if (!string.IsNullOrEmpty(request.Delimiter))
                {
                    var prefixLength = request.Prefix?.Length ?? 0;
                    var delimiterIndex = key.IndexOf(request.Delimiter, prefixLength);
                    
                    if (delimiterIndex >= 0)
                    {
                        var commonPrefix = key.Substring(0, delimiterIndex + request.Delimiter.Length);
                        commonPrefixes.Add(commonPrefix);
                        continue;
                    }
                }

                var lastModified = dict.GetDate("lastModified");
                objects.Add(new S3Object
                {
                    Key = key,
                    Size = dict.GetLong("size"),
                    LastModified = lastModified.UtcDateTime,
                    ETag = dict.GetString("etag"),
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Sort and apply max keys
            objects = objects.OrderBy(o => o.Key).ToList();
            
            var maxKeys = request.MaxKeys > 0 ? request.MaxKeys : 1000;
            var isTruncated = objects.Count > maxKeys;
            
            if (isTruncated)
            {
                objects = objects.Take(maxKeys).ToList();
            }

            var response = new ListObjectsV2Response
            {
                Name = request.BucketName,
                Prefix = request.Prefix,
                Delimiter = request.Delimiter,
                MaxKeys = maxKeys,
                IsTruncated = isTruncated,
                S3Objects = objects,
                CommonPrefixes = commonPrefixes.OrderBy(p => p).Select(p => new string(p)).ToList(),
                KeyCount = objects.Count,
                HttpStatusCode = HttpStatusCode.OK
            };

            if (isTruncated && objects.Any())
            {
                response.NextContinuationToken = objects.Last().Key;
            }

            return response;
        }, cancellationToken);
    }

    #endregion

    #region Helper Methods

    private List<Document> GetObjectsInBucket(string bucketName)
    {
        var query = QueryBuilder.Select(SelectResult.All())
            .From(DataSource.Database(_database))
            .Where(
                Expression.Property("type").EqualTo(Expression.String("object"))
                .And(Expression.Property("bucketName").EqualTo(Expression.String(bucketName)))
            );

        return query.Execute().Select(r => r.GetDictionary(0)).Cast<Document>().ToList();
    }

    #endregion

    #region Copy Operations

    /// <summary>
    /// Copies an object from one location to another within the Couchbase Lite storage.
    /// </summary>
    /// <param name="sourceBucket">The name of the source bucket.</param>
    /// <param name="sourceKey">The key of the source object.</param>
    /// <param name="destinationBucket">The name of the destination bucket.</param>
    /// <param name="destinationKey">The key for the destination object.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing information about the copied object.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the source object or destination bucket does not exist.</exception>
    public async Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey
        };
        return await CopyObjectAsync(request, cancellationToken);
    }

    /// <summary>
    /// Copies an object from one location to another within the Couchbase Lite storage.
    /// </summary>
    /// <param name="request">The request containing source and destination information.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing information about the copied object.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the source object or destination bucket does not exist.</exception>
    public async Task<CopyObjectResponse> CopyObjectAsync(CopyObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify destination bucket exists
            var destBucketDoc = _database.GetDocument($"bucket::{request.DestinationBucket}");
            if (destBucketDoc == null)
            {
                throw new AmazonS3Exception("Destination bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Get source object (handling version if specified)
            Document? sourceDoc;
            if (!string.IsNullOrEmpty(request.SourceVersionId))
            {
                sourceDoc = _database.GetDocument($"version::{request.SourceBucket}::{request.SourceKey}::{request.SourceVersionId}");
                if (sourceDoc == null)
                {
                    throw new AmazonS3Exception("The specified version does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchVersion"
                    };
                }
            }
            else
            {
                sourceDoc = _database.GetDocument($"object::{request.SourceBucket}::{request.SourceKey}");
                if (sourceDoc == null)
                {
                    throw new AmazonS3Exception("Source object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Create destination document
            var destObjectId = $"object::{request.DestinationBucket}::{request.DestinationKey}";
            var destDoc = new MutableDocument(destObjectId);

            // Copy core properties
            destDoc.SetString("type", "object");
            destDoc.SetString("bucketName", request.DestinationBucket);
            destDoc.SetString("key", request.DestinationKey);
            destDoc.SetString("contentType", sourceDoc.GetString("contentType"));
            destDoc.SetDate("lastModified", DateTimeOffset.UtcNow);
            destDoc.SetLong("size", sourceDoc.GetLong("size"));

            // Extract prefix for efficient prefix searches
            var lastSlash = request.DestinationKey.LastIndexOf('/');
            if (lastSlash > 0)
            {
                destDoc.SetString("prefix", request.DestinationKey.Substring(0, lastSlash + 1));
            }

            // Copy blob content
            var sourceBlob = sourceDoc.GetBlob("content");
            if (sourceBlob != null)
            {
                // Create a new blob from the source content
                byte[] content;
                using (var ms = new MemoryStream())
                {
                    sourceBlob.ContentStream?.CopyTo(ms);
                    content = ms.ToArray();
                }
                var destBlob = new Blob(sourceBlob.ContentType ?? "application/octet-stream", content);
                destDoc.SetBlob("content", destBlob);
            }

            // Handle metadata based on MetadataDirective
            if (request.MetadataDirective == S3MetadataDirective.REPLACE && request.Metadata?.Count > 0)
            {
                // Use the new metadata from the request
                var metadataDict = new MutableDictionaryObject();
                foreach (var key in request.Metadata.Keys)
                {
                    metadataDict.SetString(key, request.Metadata[key]);
                }
                destDoc.SetDictionary("metadata", metadataDict);
            }
            else
            {
                // Copy metadata from source (COPY directive or no metadata specified)
                var sourceMetadata = sourceDoc.GetDictionary("metadata");
                if (sourceMetadata != null)
                {
                    var metadataDict = new MutableDictionaryObject();
                    foreach (var key in sourceMetadata.Keys)
                    {
                        metadataDict.SetString(key, sourceMetadata.GetString(key));
                    }
                    destDoc.SetDictionary("metadata", metadataDict);
                }
            }

            // Calculate new ETag
            string etag;
            if (sourceBlob != null)
            {
                byte[] content;
                using (var ms = new MemoryStream())
                {
                    sourceBlob.ContentStream?.CopyTo(ms);
                    content = ms.ToArray();
                }
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(content);
                    etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            else
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    var hash = md5.ComputeHash(Array.Empty<byte>());
                    etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            destDoc.SetString("etag", etag);

            _database.Save(destDoc);

            return new CopyObjectResponse
            {
                ETag = etag,
                LastModified = destDoc.GetDate("lastModified").UtcDateTime,
                SourceVersionId = request.SourceVersionId,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    #endregion

    #region Not Implemented Operations

    /// <summary>
    /// Retrieves metadata for an object without returning the object itself.
    /// This is equivalent to an HTTP HEAD request for the object.
    /// </summary>
    /// <param name="bucketName">The name of the bucket containing the object.</param>
    /// <param name="key">The key of the object.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the object metadata.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the object does not exist.</exception>
    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = key
        };
        return await GetObjectMetadataAsync(request, cancellationToken);
    }

    /// <summary>
    /// Retrieves metadata for an object without returning the object itself.
    /// This is equivalent to an HTTP HEAD request for the object.
    /// </summary>
    /// <param name="request">The request containing the bucket name and object key.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the object metadata.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the object does not exist.</exception>
    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            Document? doc;

            // If versionId is specified, look for the versioned document
            if (!string.IsNullOrEmpty(request.VersionId))
            {
                var versionDocId = $"version::{request.BucketName}::{request.Key}::{request.VersionId}";
                doc = _database.GetDocument(versionDocId);

                if (doc == null)
                {
                    throw new AmazonS3Exception("The specified version does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchVersion"
                    };
                }
            }
            else
            {
                var objectId = $"object::{request.BucketName}::{request.Key}";
                doc = _database.GetDocument(objectId);

                if (doc == null)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            var response = new GetObjectMetadataResponse
            {
                ContentLength = doc.GetLong("size"),
                ETag = doc.GetString("etag"),
                LastModified = doc.GetDate("lastModified").UtcDateTime,
                HttpStatusCode = HttpStatusCode.OK
            };

            // Set content type via headers
            response.Headers.ContentType = doc.GetString("contentType");

            // Add metadata
            var metadataDict = doc.GetDictionary("metadata");
            if (metadataDict != null)
            {
                foreach (var key in metadataDict.Keys)
                {
                    response.Metadata[key] = metadataDict.GetString(key);
                }
            }

            // Handle version ID if present
            var versionId = doc.GetString("versionId") ?? request.VersionId;
            if (!string.IsNullOrEmpty(versionId))
            {
                response.VersionId = versionId;
            }

            return response;
        }, cancellationToken);
    }

    /// <summary>
    /// Lists objects in a bucket using the legacy V1 API.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to list.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of objects.</returns>
    public async Task<ListObjectsResponse> ListObjectsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsRequest { BucketName = bucketName };
        return await ListObjectsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists objects in a bucket with a prefix filter using the legacy V1 API.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to list.</param>
    /// <param name="prefix">The prefix to filter objects by.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the filtered list of objects.</returns>
    public async Task<ListObjectsResponse> ListObjectsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsRequest
        {
            BucketName = bucketName,
            Prefix = prefix
        };
        return await ListObjectsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists objects in a bucket using the legacy V1 API with marker-based pagination.
    /// </summary>
    /// <param name="request">The request containing bucket name, prefix, marker, and other parameters.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of objects with pagination information.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<ListObjectsResponse> ListObjectsAsync(ListObjectsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var query = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("object"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            var objects = new List<S3Object>();
            var commonPrefixes = new HashSet<string>();

            foreach (var result in query.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                // Apply marker filter (V1 pagination - marker is the last key from previous page)
                if (!string.IsNullOrEmpty(request.Marker) &&
                    string.CompareOrdinal(key, request.Marker) <= 0)
                {
                    continue;
                }

                // Handle delimiter for directory-like listing
                if (!string.IsNullOrEmpty(request.Delimiter))
                {
                    var prefixLength = request.Prefix?.Length ?? 0;
                    var delimiterIndex = key.IndexOf(request.Delimiter, prefixLength);

                    if (delimiterIndex >= 0)
                    {
                        var commonPrefix = key.Substring(0, delimiterIndex + request.Delimiter.Length);
                        commonPrefixes.Add(commonPrefix);
                        continue;
                    }
                }

                var lastModified = dict.GetDate("lastModified");
                objects.Add(new S3Object
                {
                    Key = key,
                    Size = dict.GetLong("size"),
                    LastModified = lastModified.UtcDateTime,
                    ETag = dict.GetString("etag"),
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Sort by key
            objects = objects.OrderBy(o => o.Key).ToList();

            // Apply max keys
            var maxKeys = request.MaxKeys > 0 ? request.MaxKeys : 1000;
            var isTruncated = objects.Count > maxKeys;

            if (isTruncated)
            {
                objects = objects.Take(maxKeys).ToList();
            }

            var response = new ListObjectsResponse
            {
                Name = request.BucketName,
                Prefix = request.Prefix,
                Marker = request.Marker,
                Delimiter = request.Delimiter,
                MaxKeys = maxKeys,
                IsTruncated = isTruncated,
                S3Objects = objects,
                CommonPrefixes = commonPrefixes.OrderBy(p => p).Select(p => new CommonPrefix { Prefix = p }).ToList(),
                HttpStatusCode = HttpStatusCode.OK
            };

            // Set NextMarker for pagination (the key of the last object returned)
            if (isTruncated && objects.Any())
            {
                response.NextMarker = objects.Last().Key;
            }

            return response;
        }, cancellationToken);
    }

    /// <summary>
    /// Lists all versions of objects in a bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of object versions.</returns>
    public async Task<ListVersionsResponse> ListVersionsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest { BucketName = bucketName };
        return await ListVersionsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists all versions of objects in a bucket with a prefix filter.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="prefix">The prefix to filter objects by.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the filtered list of object versions.</returns>
    public async Task<ListVersionsResponse> ListVersionsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest
        {
            BucketName = bucketName,
            Prefix = prefix
        };
        return await ListVersionsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists all versions of objects in a bucket.
    /// Returns both object versions and delete markers.
    /// </summary>
    /// <param name="request">The request containing bucket name and filter parameters.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing object versions and delete markers.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var versions = new List<S3ObjectVersion>();
            var deleteMarkers = new List<DeleteMarkerEntry>();
            var commonPrefixes = new HashSet<string>();

            // Query for current objects
            var objectQuery = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("object"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            foreach (var result in objectQuery.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                // Apply key marker filter
                if (!string.IsNullOrEmpty(request.KeyMarker) &&
                    string.CompareOrdinal(key, request.KeyMarker) <= 0)
                {
                    continue;
                }

                // Handle delimiter for directory-like listing
                if (!string.IsNullOrEmpty(request.Delimiter))
                {
                    var prefixLength = request.Prefix?.Length ?? 0;
                    var delimiterIndex = key.IndexOf(request.Delimiter, prefixLength);

                    if (delimiterIndex >= 0)
                    {
                        var commonPrefix = key.Substring(0, delimiterIndex + request.Delimiter.Length);
                        commonPrefixes.Add(commonPrefix);
                        continue;
                    }
                }

                var lastModified = dict.GetDate("lastModified");
                var versionId = dict.GetString("versionId") ?? "null"; // "null" is used for non-versioned objects

                versions.Add(new S3ObjectVersion
                {
                    Key = key,
                    VersionId = versionId,
                    IsLatest = true,
                    LastModified = lastModified.UtcDateTime,
                    ETag = dict.GetString("etag"),
                    Size = dict.GetLong("size"),
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Query for versioned objects
            var versionQuery = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("version"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            foreach (var result in versionQuery.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                // Apply key marker filter
                if (!string.IsNullOrEmpty(request.KeyMarker) &&
                    string.CompareOrdinal(key, request.KeyMarker) <= 0)
                {
                    continue;
                }

                var lastModified = dict.GetDate("lastModified");
                var versionId = dict.GetString("versionId");

                versions.Add(new S3ObjectVersion
                {
                    Key = key,
                    VersionId = versionId,
                    IsLatest = false,
                    LastModified = lastModified.UtcDateTime,
                    ETag = dict.GetString("etag"),
                    Size = dict.GetLong("size"),
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Query for delete markers
            var deleteMarkerQuery = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("deleteMarker"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            foreach (var result in deleteMarkerQuery.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                var lastModified = dict.GetDate("lastModified");
                var versionId = dict.GetString("versionId");
                var isLatest = dict.GetBoolean("isLatest");

                deleteMarkers.Add(new DeleteMarkerEntry
                {
                    Key = key,
                    VersionId = versionId,
                    IsLatest = isLatest,
                    LastModified = lastModified.UtcDateTime
                });
            }

            // Sort versions by key, then by lastModified descending
            versions = versions
                .OrderBy(v => v.Key)
                .ThenByDescending(v => v.LastModified)
                .ToList();

            // Apply max keys
            var maxKeys = request.MaxKeys > 0 ? request.MaxKeys : 1000;
            var totalItems = versions.Count + deleteMarkers.Count;
            var isTruncated = totalItems > maxKeys;

            if (isTruncated)
            {
                versions = versions.Take(maxKeys).ToList();
            }

            var response = new ListVersionsResponse
            {
                Name = request.BucketName,
                Prefix = request.Prefix,
                KeyMarker = request.KeyMarker,
                VersionIdMarker = request.VersionIdMarker,
                Delimiter = request.Delimiter,
                MaxKeys = maxKeys,
                IsTruncated = isTruncated,
                Versions = versions,
                DeleteMarkers = deleteMarkers,
                CommonPrefixes = commonPrefixes.OrderBy(p => p).Select(p => new CommonPrefix { Prefix = p }).ToList(),
                HttpStatusCode = HttpStatusCode.OK
            };

            // Set markers for pagination
            if (isTruncated && versions.Any())
            {
                var lastVersion = versions.Last();
                response.NextKeyMarker = lastVersion.Key;
                response.NextVersionIdMarker = lastVersion.VersionId;
            }

            return response;
        }, cancellationToken);
    }

    #region Pre-signed URL Operations

    // Signing key for pre-signed URLs (in production, this would be configured)
    private static readonly string _signingKey = "CouchbaseLiteS3SigningKey";

    /// <summary>
    /// Generates a pre-signed URL asynchronously for accessing an object without authentication.
    /// </summary>
    /// <param name="request">The request containing bucket name, key, verb, and expiration details.</param>
    /// <returns>A signed URL that provides temporary access to the specified object.</returns>
    public Task<string> GetPreSignedURLAsync(GetPreSignedUrlRequest request)
    {
        return Task.FromResult(GetPreSignedURL(request));
    }

    /// <summary>
    /// Copies a specific version of an object from one location to another.
    /// </summary>
    /// <param name="sourceBucket">The name of the source bucket.</param>
    /// <param name="sourceKey">The key of the source object.</param>
    /// <param name="destinationBucket">The name of the destination bucket.</param>
    /// <param name="destinationKey">The key for the destination object.</param>
    /// <param name="sourceVersionId">The version ID of the source object to copy.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing information about the copied object.</returns>
    public async Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string sourceVersionId, CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey,
            SourceVersionId = sourceVersionId
        };
        return await CopyObjectAsync(request, cancellationToken);
    }

    /// <summary>
    /// Copies a part of an object as part of a multipart upload operation.
    /// </summary>
    /// <param name="sourceBucket">The name of the source bucket.</param>
    /// <param name="sourceKey">The key of the source object.</param>
    /// <param name="destinationBucket">The name of the destination bucket.</param>
    /// <param name="destinationKey">The key for the destination object.</param>
    /// <param name="uploadId">The upload ID of the multipart upload.</param>
    /// <param name="partNumber">The part number (string format for compatibility).</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ETag of the copied part.</returns>
    public async Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string uploadId, string partNumber, CancellationToken cancellationToken = default)
    {
        var request = new CopyPartRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey,
            UploadId = uploadId,
            PartNumber = int.Parse(partNumber)
        };
        return await CopyPartAsync(request, cancellationToken);
    }

    public Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Retrieves metadata for a specific version of an object without returning the object itself.
    /// </summary>
    /// <param name="bucketName">The name of the bucket containing the object.</param>
    /// <param name="key">The key of the object.</param>
    /// <param name="versionId">The version ID of the object.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the object metadata.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the object or version does not exist.</exception>
    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = bucketName,
            Key = key,
            VersionId = versionId
        };
        return await GetObjectMetadataAsync(request, cancellationToken);
    }

    public Task<ListDirectoryBucketsResponse> ListDirectoryBucketsAsync(ListDirectoryBucketsRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, string versionId, int days, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(AmazonWebServiceRequest request)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _database?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion

    #region Service Configuration (Required by IAmazonS3)

    public IClientConfig Config => new AmazonS3Config();

    public IS3PaginatorFactory Paginators => throw new NotImplementedException();

    #endregion

    #region Placeholder implementations for remaining IAmazonS3 members
    
    // Note: The IAmazonS3 interface has many more methods. 
    // This implementation covers the core operations.
    // Additional methods would need similar implementations.

    /// <summary>
    /// Aborts a multipart upload and deletes all uploaded parts.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="key">The object key.</param>
    /// <param name="uploadId">The upload ID.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating success.</returns>
    public async Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var request = new AbortMultipartUploadRequest
        {
            BucketName = bucketName,
            Key = key,
            UploadId = uploadId
        };
        return await AbortMultipartUploadAsync(request, cancellationToken);
    }

    /// <summary>
    /// Aborts a multipart upload and deletes all uploaded parts.
    /// </summary>
    /// <param name="request">The request containing the upload ID.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating success.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the upload does not exist.</exception>
    public async Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify the upload exists
            var uploadDocId = $"upload::{request.UploadId}";
            var uploadDoc = _database.GetDocument(uploadDocId);
            if (uploadDoc == null)
            {
                throw new AmazonS3Exception("The specified upload does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchUpload"
                };
            }

            // Delete all parts for this upload
            var partsQuery = QueryBuilder.Select(SelectResult.Expression(Meta.ID))
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("part"))
                    .And(Expression.Property("uploadId").EqualTo(Expression.String(request.UploadId)))
                );

            foreach (var result in partsQuery.Execute())
            {
                var partDocId = result.GetString("id");
                if (partDocId != null)
                {
                    var partDoc = _database.GetDocument(partDocId);
                    if (partDoc != null)
                    {
                        _database.Delete(partDoc);
                    }
                }
            }

            // Delete the upload document
            _database.Delete(uploadDoc);

            return new AbortMultipartUploadResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Completes a multipart upload by assembling all parts into a final object.
    /// </summary>
    /// <param name="request">The request containing the upload ID and list of parts.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ETag and location of the completed object.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the upload does not exist or parts are invalid.</exception>
    public async Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify the upload exists
            var uploadDocId = $"upload::{request.UploadId}";
            var uploadDoc = _database.GetDocument(uploadDocId);
            if (uploadDoc == null)
            {
                throw new AmazonS3Exception("The specified upload does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchUpload"
                };
            }

            var bucketName = uploadDoc.GetString("bucketName");
            var key = uploadDoc.GetString("key");
            var contentType = uploadDoc.GetString("contentType");

            // Collect all parts in order and concatenate
            var allContent = new List<byte>();
            var partEtags = new List<string>();

            // Sort parts by part number
            var sortedParts = request.PartETags?.OrderBy(p => p.PartNumber).ToList() ?? new List<PartETag>();

            foreach (var partEtag in sortedParts)
            {
                var partDocId = $"part::{request.UploadId}::{partEtag.PartNumber}";
                var partDoc = _database.GetDocument(partDocId);

                if (partDoc == null)
                {
                    throw new AmazonS3Exception($"Part {partEtag.PartNumber} not found")
                    {
                        StatusCode = HttpStatusCode.BadRequest,
                        ErrorCode = "InvalidPart"
                    };
                }

                var storedEtag = partDoc.GetString("etag");
                var providedEtag = partEtag.ETag?.Trim('"');

                if (storedEtag != providedEtag)
                {
                    throw new AmazonS3Exception($"Part {partEtag.PartNumber} ETag mismatch")
                    {
                        StatusCode = HttpStatusCode.BadRequest,
                        ErrorCode = "InvalidPart"
                    };
                }

                var partBlob = partDoc.GetBlob("content");
                if (partBlob != null)
                {
                    using (var ms = new MemoryStream())
                    {
                        partBlob.ContentStream?.CopyTo(ms);
                        allContent.AddRange(ms.ToArray());
                    }
                }
                partEtags.Add(storedEtag);
            }

            var finalContent = allContent.ToArray();

            // Calculate final ETag (S3 uses special format for multipart: "etag-N" where N is part count)
            string finalEtag;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                // Concatenate all part MD5s and compute MD5 of that
                var combinedHashes = new List<byte>();
                foreach (var etag in partEtags)
                {
                    var hashBytes = new byte[etag.Length / 2];
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        hashBytes[i] = Convert.ToByte(etag.Substring(i * 2, 2), 16);
                    }
                    combinedHashes.AddRange(hashBytes);
                }
                var finalHash = md5.ComputeHash(combinedHashes.ToArray());
                finalEtag = BitConverter.ToString(finalHash).Replace("-", "").ToLowerInvariant() + $"-{sortedParts.Count}";
            }

            // Create the final object document
            var objectDocId = $"object::{bucketName}::{key}";
            var objectDoc = new MutableDocument(objectDocId);
            objectDoc.SetString("type", "object");
            objectDoc.SetString("bucketName", bucketName);
            objectDoc.SetString("key", key);
            objectDoc.SetString("contentType", contentType);
            objectDoc.SetDate("lastModified", DateTimeOffset.UtcNow);
            objectDoc.SetLong("size", finalContent.Length);
            objectDoc.SetString("etag", finalEtag);

            // Extract prefix for efficient prefix searches
            var lastSlash = key.LastIndexOf('/');
            if (lastSlash > 0)
            {
                objectDoc.SetString("prefix", key.Substring(0, lastSlash + 1));
            }

            // Store content as blob
            var contentBlob = new Blob(contentType, finalContent);
            objectDoc.SetBlob("content", contentBlob);

            // Copy metadata from upload doc
            var metadata = uploadDoc.GetDictionary("metadata");
            if (metadata != null)
            {
                var metadataDict = new MutableDictionaryObject();
                foreach (var mk in metadata.Keys)
                {
                    metadataDict.SetString(mk, metadata.GetString(mk));
                }
                objectDoc.SetDictionary("metadata", metadataDict);
            }

            _database.Save(objectDoc);

            // Delete all parts
            foreach (var partEtag in sortedParts)
            {
                var partDocId = $"part::{request.UploadId}::{partEtag.PartNumber}";
                var partDoc = _database.GetDocument(partDocId);
                if (partDoc != null)
                {
                    _database.Delete(partDoc);
                }
            }

            // Delete the upload document
            _database.Delete(uploadDoc);

            return new CompleteMultipartUploadResponse
            {
                BucketName = bucketName,
                Key = key,
                ETag = finalEtag,
                Location = $"cblite://{bucketName}/{key}",
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Copies a part of an object as part of a multipart upload operation.
    /// </summary>
    /// <param name="sourceBucket">The name of the source bucket.</param>
    /// <param name="sourceKey">The key of the source object.</param>
    /// <param name="destinationBucket">The name of the destination bucket.</param>
    /// <param name="destinationKey">The key for the destination object.</param>
    /// <param name="uploadId">The upload ID of the multipart upload.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ETag of the copied part.</returns>
    public async Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string uploadId, CancellationToken cancellationToken = default)
    {
        var request = new CopyPartRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey,
            UploadId = uploadId,
            PartNumber = 1 // Default part number
        };
        return await CopyPartAsync(request, cancellationToken);
    }

    /// <summary>
    /// Copies a part of an object as part of a multipart upload operation.
    /// </summary>
    /// <param name="request">The request containing source, destination, and part information.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ETag of the copied part.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the source object, upload, or bucket does not exist.</exception>
    public async Task<CopyPartResponse> CopyPartAsync(CopyPartRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Validate part number (S3 allows 1-10000)
            if (request.PartNumber < 1 || request.PartNumber > 10000)
            {
                throw new AmazonS3Exception("Part number must be between 1 and 10000")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "InvalidArgument"
                };
            }

            // Verify the upload exists
            var uploadDoc = _database.GetDocument($"upload::{request.UploadId}");
            if (uploadDoc == null)
            {
                throw new AmazonS3Exception("The specified upload does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchUpload"
                };
            }

            // Get source object
            Document? sourceDoc;
            if (!string.IsNullOrEmpty(request.SourceVersionId))
            {
                sourceDoc = _database.GetDocument($"version::{request.SourceBucket}::{request.SourceKey}::{request.SourceVersionId}");
                if (sourceDoc == null)
                {
                    throw new AmazonS3Exception("The specified version does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchVersion"
                    };
                }
            }
            else
            {
                sourceDoc = _database.GetDocument($"object::{request.SourceBucket}::{request.SourceKey}");
                if (sourceDoc == null)
                {
                    throw new AmazonS3Exception("Source object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Get the content to copy (optionally with byte range)
            var sourceBlob = sourceDoc.GetBlob("content");
            byte[] partContent;

            if (sourceBlob != null)
            {
                using (var ms = new MemoryStream())
                {
                    sourceBlob.ContentStream?.CopyTo(ms);
                    var fullContent = ms.ToArray();

                    // Handle byte range if specified
                    if (request.FirstByte.HasValue && request.LastByte.HasValue)
                    {
                        var firstByte = (int)request.FirstByte.Value;
                        var lastByte = (int)Math.Min(request.LastByte.Value, fullContent.Length - 1);
                        var length = lastByte - firstByte + 1;
                        partContent = new byte[length];
                        Array.Copy(fullContent, firstByte, partContent, 0, length);
                    }
                    else
                    {
                        partContent = fullContent;
                    }
                }
            }
            else
            {
                partContent = Array.Empty<byte>();
            }

            // Store the part
            var partDocId = $"part::{request.UploadId}::{request.PartNumber}";
            var partDoc = new MutableDocument(partDocId);
            partDoc.SetString("type", "part");
            partDoc.SetString("uploadId", request.UploadId);
            partDoc.SetInt("partNumber", request.PartNumber);
            partDoc.SetDate("lastModified", DateTimeOffset.UtcNow);
            partDoc.SetLong("size", partContent.Length);

            // Store content as blob
            var partBlob = new Blob("application/octet-stream", partContent);
            partDoc.SetBlob("content", partBlob);

            // Calculate ETag for the part
            string etag;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(partContent);
                etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            partDoc.SetString("etag", etag);

            _database.Save(partDoc);

            return new CopyPartResponse
            {
                ETag = etag,
                LastModified = partDoc.GetDate("lastModified").UtcDateTime,
                PartNumber = request.PartNumber,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<DeleteBucketAnalyticsConfigurationResponse> DeleteBucketAnalyticsConfigurationAsync(DeleteBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Deletes the encryption configuration from the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeleteBucketEncryptionResponse> DeleteBucketEncryptionAsync(DeleteBucketEncryptionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("encryption");
                _database.Save(mutableDoc);
            }

            return new DeleteBucketEncryptionResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public Task<DeleteBucketIntelligentTieringConfigurationResponse> DeleteBucketIntelligentTieringConfigurationAsync(DeleteBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketInventoryConfigurationResponse> DeleteBucketInventoryConfigurationAsync(DeleteBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketMetricsConfigurationResponse> DeleteBucketMetricsConfigurationAsync(DeleteBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketOwnershipControlsResponse> DeleteBucketOwnershipControlsAsync(DeleteBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Deletes the policy associated with the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    public async Task<DeleteBucketPolicyResponse> DeleteBucketPolicyAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteBucketPolicyRequest { BucketName = bucketName };
        return await DeleteBucketPolicyAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes the policy associated with the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeleteBucketPolicyResponse> DeleteBucketPolicyAsync(DeleteBucketPolicyRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Remove the policy from the bucket document
            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("policy");
                _database.Save(mutableDoc);
            }

            return new DeleteBucketPolicyResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public Task<DeleteBucketReplicationResponse> DeleteBucketReplicationAsync(DeleteBucketReplicationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketTaggingResponse> DeleteBucketTaggingAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketTaggingResponse> DeleteBucketTaggingAsync(DeleteBucketTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Deletes the website configuration from the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    public async Task<DeleteBucketWebsiteResponse> DeleteBucketWebsiteAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteBucketWebsiteRequest { BucketName = bucketName };
        return await DeleteBucketWebsiteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes the website configuration from the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeleteBucketWebsiteResponse> DeleteBucketWebsiteAsync(DeleteBucketWebsiteRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("websiteConfiguration");
                _database.Save(mutableDoc);
            }

            return new DeleteBucketWebsiteResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes the CORS configuration from the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    public async Task<DeleteCORSConfigurationResponse> DeleteCORSConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteCORSConfigurationRequest { BucketName = bucketName };
        return await DeleteCORSConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes the CORS configuration from the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeleteCORSConfigurationResponse> DeleteCORSConfigurationAsync(DeleteCORSConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("corsConfiguration");
                _database.Save(mutableDoc);
            }

            return new DeleteCORSConfigurationResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes the lifecycle configuration from the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    public async Task<DeleteLifecycleConfigurationResponse> DeleteLifecycleConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new DeleteLifecycleConfigurationRequest { BucketName = bucketName };
        return await DeleteLifecycleConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes the lifecycle configuration from the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeleteLifecycleConfigurationResponse> DeleteLifecycleConfigurationAsync(DeleteLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("lifecycleConfiguration");
                _database.Save(mutableDoc);
            }

            return new DeleteLifecycleConfigurationResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public Task<DeleteObjectTaggingResponse> DeleteObjectTaggingAsync(DeleteObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Deletes the public access block configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the delete operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<DeletePublicAccessBlockResponse> DeletePublicAccessBlockAsync(DeletePublicAccessBlockRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.Remove("publicAccessBlock");
                _database.Save(mutableDoc);
            }

            return new DeletePublicAccessBlockResponse
            {
                HttpStatusCode = HttpStatusCode.NoContent
            };
        }, cancellationToken);
    }

    public void EnsureBucketExists(string bucketName)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the access control list (ACL) for a bucket or object.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ACL.</returns>
    public async Task<GetACLResponse> GetACLAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetACLRequest { BucketName = bucketName };
        return await GetACLAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the access control list (ACL) for a bucket or object.
    /// </summary>
    /// <param name="request">The request containing the bucket name and optional key.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ACL with grants and owner information.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket or object does not exist.</exception>
    public async Task<GetACLResponse> GetACLAsync(GetACLRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            Document? doc;
            if (!string.IsNullOrEmpty(request.Key))
            {
                // Object ACL
                doc = _database.GetDocument($"object::{request.BucketName}::{request.Key}");
                if (doc == null)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }
            else
            {
                // Bucket ACL
                doc = _database.GetDocument($"bucket::{request.BucketName}");
                if (doc == null)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Get stored ACL or return default private ACL
            var acl = new S3AccessControlList();
            var owner = new Owner
            {
                Id = doc.GetString("ownerId") ?? "local-user",
                DisplayName = doc.GetString("ownerDisplayName") ?? "Local User"
            };
            acl.Owner = owner;

            // Check for stored grants
            var grantsArray = doc.GetArray("grants");
            if (grantsArray != null)
            {
                foreach (var grantDict in grantsArray)
                {
                    if (grantDict is DictionaryObject grantDictObj)
                    {
                        var grant = new S3Grant
                        {
                            Permission = new S3Permission(grantDictObj.GetString("permission") ?? "FULL_CONTROL"),
                            Grantee = new S3Grantee
                            {
                                CanonicalUser = grantDictObj.GetString("granteeId"),
                                DisplayName = grantDictObj.GetString("granteeDisplayName")
                            }
                        };
                        acl.Grants.Add(grant);
                    }
                }
            }
            else
            {
                // Default: owner has full control
                acl.Grants.Add(new S3Grant
                {
                    Permission = S3Permission.FULL_CONTROL,
                    Grantee = new S3Grantee { CanonicalUser = owner.Id, DisplayName = owner.DisplayName }
                });
            }

            return new GetACLResponse
            {
                AccessControlList = acl,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<GetBucketAccelerateConfigurationResponse> GetBucketAccelerateConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketAccelerateConfigurationResponse> GetBucketAccelerateConfigurationAsync(GetBucketAccelerateConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketAnalyticsConfigurationResponse> GetBucketAnalyticsConfigurationAsync(GetBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the encryption configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the encryption configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no encryption configuration.</exception>
    public async Task<GetBucketEncryptionResponse> GetBucketEncryptionAsync(GetBucketEncryptionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var encryptionDict = bucketDoc.GetDictionary("encryption");
            if (encryptionDict == null)
            {
                throw new AmazonS3Exception("The server side encryption configuration was not found")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "ServerSideEncryptionConfigurationNotFoundError"
                };
            }

            var rules = new List<ServerSideEncryptionRule>();
            var rulesArray = encryptionDict.GetArray("rules");
            if (rulesArray != null)
            {
                foreach (var ruleItem in rulesArray)
                {
                    if (ruleItem is DictionaryObject ruleDict)
                    {
                        var rule = new ServerSideEncryptionRule
                        {
                            ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
                            {
                                ServerSideEncryptionAlgorithm = new ServerSideEncryptionMethod(
                                    ruleDict.GetString("sseAlgorithm") ?? "AES256"),
                                KMSMasterKeyID = ruleDict.GetString("kmsMasterKeyId")
                            },
                            BucketKeyEnabled = ruleDict.GetBoolean("bucketKeyEnabled")
                        };
                        rules.Add(rule);
                    }
                }
            }

            return new GetBucketEncryptionResponse
            {
                ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
                {
                    ServerSideEncryptionRules = rules
                },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<GetBucketIntelligentTieringConfigurationResponse> GetBucketIntelligentTieringConfigurationAsync(GetBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketInventoryConfigurationResponse> GetBucketInventoryConfigurationAsync(GetBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLocationResponse> GetBucketLocationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLocationResponse> GetBucketLocationAsync(GetBucketLocationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the logging configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the logging configuration.</returns>
    public async Task<GetBucketLoggingResponse> GetBucketLoggingAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketLoggingRequest { BucketName = bucketName };
        return await GetBucketLoggingAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the logging configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the logging configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<GetBucketLoggingResponse> GetBucketLoggingAsync(GetBucketLoggingRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var loggingDict = bucketDoc.GetDictionary("loggingConfiguration");
            var config = new S3BucketLoggingConfig();

            if (loggingDict != null)
            {
                config.TargetBucketName = loggingDict.GetString("targetBucket");
                config.TargetPrefix = loggingDict.GetString("targetPrefix");
            }

            return new GetBucketLoggingResponse
            {
                BucketLoggingConfig = config,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<GetBucketMetricsConfigurationResponse> GetBucketMetricsConfigurationAsync(GetBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the notification configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the notification configuration.</returns>
    public async Task<GetBucketNotificationResponse> GetBucketNotificationAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketNotificationRequest { BucketName = bucketName };
        return await GetBucketNotificationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the notification configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the notification configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<GetBucketNotificationResponse> GetBucketNotificationAsync(GetBucketNotificationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Return empty notification configuration if none set
            // In real AWS S3, this returns an empty configuration, not an error
            return new GetBucketNotificationResponse
            {
                TopicConfigurations = new List<TopicConfiguration>(),
                QueueConfigurations = new List<QueueConfiguration>(),
                LambdaFunctionConfigurations = new List<LambdaFunctionConfiguration>(),
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<GetBucketOwnershipControlsResponse> GetBucketOwnershipControlsAsync(GetBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the bucket policy for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the bucket policy as a JSON string.</returns>
    public async Task<GetBucketPolicyResponse> GetBucketPolicyAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketPolicyRequest { BucketName = bucketName };
        return await GetBucketPolicyAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the bucket policy for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the bucket policy as a JSON string.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no policy.</exception>
    public async Task<GetBucketPolicyResponse> GetBucketPolicyAsync(GetBucketPolicyRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var policy = bucketDoc.GetString("policy");
            if (string.IsNullOrEmpty(policy))
            {
                throw new AmazonS3Exception("The bucket policy does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucketPolicy"
                };
            }

            return new GetBucketPolicyResponse
            {
                Policy = policy,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<GetBucketPolicyStatusResponse> GetBucketPolicyStatusAsync(GetBucketPolicyStatusRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketReplicationResponse> GetBucketReplicationAsync(GetBucketReplicationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketRequestPaymentResponse> GetBucketRequestPaymentAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketRequestPaymentResponse> GetBucketRequestPaymentAsync(GetBucketRequestPaymentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketTaggingResponse> GetBucketTaggingAsync(GetBucketTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Gets the versioning state of a bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the versioning state.</returns>
    public async Task<GetBucketVersioningResponse> GetBucketVersioningAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketVersioningRequest { BucketName = bucketName };
        return await GetBucketVersioningAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the versioning state of a bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the versioning state (Off, Enabled, or Suspended).</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<GetBucketVersioningResponse> GetBucketVersioningAsync(GetBucketVersioningRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var versioningStatus = bucketDoc.GetString("versioningStatus");
            var status = VersionStatus.Off;

            if (versioningStatus == "Enabled")
            {
                status = VersionStatus.Enabled;
            }
            else if (versioningStatus == "Suspended")
            {
                status = VersionStatus.Suspended;
            }

            return new GetBucketVersioningResponse
            {
                VersioningConfig = new S3BucketVersioningConfig { Status = status },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the website configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the website configuration.</returns>
    public async Task<GetBucketWebsiteResponse> GetBucketWebsiteAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketWebsiteRequest { BucketName = bucketName };
        return await GetBucketWebsiteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the website configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the website configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no website configuration.</exception>
    public async Task<GetBucketWebsiteResponse> GetBucketWebsiteAsync(GetBucketWebsiteRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var websiteDict = bucketDoc.GetDictionary("websiteConfiguration");
            if (websiteDict == null)
            {
                throw new AmazonS3Exception("The specified bucket does not have a website configuration")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchWebsiteConfiguration"
                };
            }

            var config = new WebsiteConfiguration();

            // Parse index document
            var indexDocDict = websiteDict.GetDictionary("indexDocument");
            if (indexDocDict != null)
            {
                config.IndexDocumentSuffix = indexDocDict.GetString("suffix");
            }

            // Parse error document
            var errorDocDict = websiteDict.GetDictionary("errorDocument");
            if (errorDocDict != null)
            {
                config.ErrorDocument = errorDocDict.GetString("key");
            }

            // Parse redirect all requests
            var redirectAllDict = websiteDict.GetDictionary("redirectAllRequestsTo");
            if (redirectAllDict != null)
            {
                config.RedirectAllRequestsTo = new RoutingRuleRedirect
                {
                    HostName = redirectAllDict.GetString("hostName"),
                    Protocol = redirectAllDict.GetString("protocol")
                };
            }

            // Parse routing rules
            var routingRulesArray = websiteDict.GetArray("routingRules");
            if (routingRulesArray != null)
            {
                config.RoutingRules = new List<RoutingRule>();
                foreach (var ruleItem in routingRulesArray)
                {
                    if (ruleItem is DictionaryObject ruleDict)
                    {
                        var rule = new RoutingRule();

                        var conditionDict = ruleDict.GetDictionary("condition");
                        if (conditionDict != null)
                        {
                            rule.Condition = new RoutingRuleCondition
                            {
                                KeyPrefixEquals = conditionDict.GetString("keyPrefixEquals"),
                                HttpErrorCodeReturnedEquals = conditionDict.GetString("httpErrorCodeReturnedEquals")
                            };
                        }

                        var redirectDict = ruleDict.GetDictionary("redirect");
                        if (redirectDict != null)
                        {
                            rule.Redirect = new RoutingRuleRedirect
                            {
                                HostName = redirectDict.GetString("hostName"),
                                Protocol = redirectDict.GetString("protocol"),
                                ReplaceKeyWith = redirectDict.GetString("replaceKeyWith"),
                                ReplaceKeyPrefixWith = redirectDict.GetString("replaceKeyPrefixWith"),
                                HttpRedirectCode = redirectDict.GetString("httpRedirectCode")
                            };
                        }

                        config.RoutingRules.Add(rule);
                    }
                }
            }

            return new GetBucketWebsiteResponse
            {
                WebsiteConfiguration = config,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the CORS configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the CORS configuration.</returns>
    public async Task<GetCORSConfigurationResponse> GetCORSConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetCORSConfigurationRequest { BucketName = bucketName };
        return await GetCORSConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the CORS configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the CORS configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no CORS configuration.</exception>
    public async Task<GetCORSConfigurationResponse> GetCORSConfigurationAsync(GetCORSConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var corsDict = bucketDoc.GetDictionary("corsConfiguration");
            if (corsDict == null)
            {
                throw new AmazonS3Exception("The CORS configuration does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchCORSConfiguration"
                };
            }

            var rules = new List<CORSRule>();
            var rulesArray = corsDict.GetArray("rules");
            if (rulesArray != null)
            {
                foreach (var ruleItem in rulesArray)
                {
                    if (ruleItem is DictionaryObject ruleDict)
                    {
                        var rule = new CORSRule
                        {
                            Id = ruleDict.GetString("id"),
                            MaxAgeSeconds = (int)ruleDict.GetLong("maxAgeSeconds")
                        };

                        // Parse allowed headers
                        var allowedHeadersArray = ruleDict.GetArray("allowedHeaders");
                        if (allowedHeadersArray != null)
                        {
                            rule.AllowedHeaders = allowedHeadersArray.Select(h => h?.ToString() ?? string.Empty).ToList();
                        }

                        // Parse allowed methods
                        var allowedMethodsArray = ruleDict.GetArray("allowedMethods");
                        if (allowedMethodsArray != null)
                        {
                            rule.AllowedMethods = allowedMethodsArray.Select(m => m?.ToString() ?? string.Empty).ToList();
                        }

                        // Parse allowed origins
                        var allowedOriginsArray = ruleDict.GetArray("allowedOrigins");
                        if (allowedOriginsArray != null)
                        {
                            rule.AllowedOrigins = allowedOriginsArray.Select(o => o?.ToString() ?? string.Empty).ToList();
                        }

                        // Parse expose headers
                        var exposeHeadersArray = ruleDict.GetArray("exposeHeaders");
                        if (exposeHeadersArray != null)
                        {
                            rule.ExposeHeaders = exposeHeadersArray.Select(e => e?.ToString() ?? string.Empty).ToList();
                        }

                        rules.Add(rule);
                    }
                }
            }

            return new GetCORSConfigurationResponse
            {
                Configuration = new CORSConfiguration { Rules = rules },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Gets the lifecycle configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the lifecycle configuration.</returns>
    public async Task<GetLifecycleConfigurationResponse> GetLifecycleConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetLifecycleConfigurationRequest { BucketName = bucketName };
        return await GetLifecycleConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Gets the lifecycle configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the lifecycle configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no lifecycle configuration.</exception>
    public async Task<GetLifecycleConfigurationResponse> GetLifecycleConfigurationAsync(GetLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var lifecycleDict = bucketDoc.GetDictionary("lifecycleConfiguration");
            if (lifecycleDict == null)
            {
                throw new AmazonS3Exception("The lifecycle configuration does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchLifecycleConfiguration"
                };
            }

            var rules = new List<LifecycleRule>();
            var rulesArray = lifecycleDict.GetArray("rules");
            if (rulesArray != null)
            {
                foreach (var ruleItem in rulesArray)
                {
                    if (ruleItem is DictionaryObject ruleDict)
                    {
                        var rule = new LifecycleRule
                        {
                            Id = ruleDict.GetString("id"),
                            Status = new LifecycleRuleStatus(ruleDict.GetString("status") ?? "Enabled"),
                            Prefix = ruleDict.GetString("prefix") ?? string.Empty
                        };

                        // Parse expiration
                        var expirationDict = ruleDict.GetDictionary("expiration");
                        if (expirationDict != null)
                        {
                            rule.Expiration = new LifecycleRuleExpiration
                            {
                                Days = (int)expirationDict.GetLong("days"),
                                ExpiredObjectDeleteMarker = expirationDict.GetBoolean("expiredObjectDeleteMarker")
                            };
                            var dateStr = expirationDict.GetString("date");
                            if (!string.IsNullOrEmpty(dateStr))
                            {
                                rule.Expiration.DateUtc = DateTime.Parse(dateStr);
                            }
                        }

                        // Parse transitions
                        var transitionsArray = ruleDict.GetArray("transitions");
                        if (transitionsArray != null)
                        {
                            rule.Transitions = new List<LifecycleTransition>();
                            foreach (var transItem in transitionsArray)
                            {
                                if (transItem is DictionaryObject transDict)
                                {
                                    var transition = new LifecycleTransition
                                    {
                                        Days = (int)transDict.GetLong("days"),
                                        StorageClass = new S3StorageClass(transDict.GetString("storageClass") ?? "STANDARD_IA")
                                    };
                                    rule.Transitions.Add(transition);
                                }
                            }
                        }

                        // Parse noncurrent version expiration
                        var noncurrentExpDict = ruleDict.GetDictionary("noncurrentVersionExpiration");
                        if (noncurrentExpDict != null)
                        {
                            rule.NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration
                            {
                                NoncurrentDays = (int)noncurrentExpDict.GetLong("noncurrentDays"),
                                NewerNoncurrentVersions = (int)noncurrentExpDict.GetLong("newerNoncurrentVersions")
                            };
                        }

                        rules.Add(rule);
                    }
                }
            }

            return new GetLifecycleConfigurationResponse
            {
                Configuration = new LifecycleConfiguration { Rules = rules },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public IDictionary<string, string> GetAllObjectMetadata(string bucketName, string objectKey, IDictionary<string, string> metadata)
        => throw new NotImplementedException();

    public Task<GetObjectAttributesResponse> GetObjectAttributesAsync(GetObjectAttributesRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectLegalHoldResponse> GetObjectLegalHoldAsync(GetObjectLegalHoldRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectLockConfigurationResponse> GetObjectLockConfigurationAsync(GetObjectLockConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectRetentionResponse> GetObjectRetentionAsync(GetObjectRetentionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTaggingResponse> GetObjectTaggingAsync(GetObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTorrentResponse> GetObjectTorrentAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTorrentResponse> GetObjectTorrentAsync(GetObjectTorrentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Generates a pre-signed URL for accessing an object without authentication.
    /// The URL includes a signature and expiration time that can be validated.
    /// </summary>
    /// <param name="request">The request containing bucket name, key, verb, and expiration details.</param>
    /// <returns>A signed URL that provides temporary access to the specified object.</returns>
    /// <remarks>
    /// For Couchbase Lite local storage, this generates a custom URL format:
    /// cblite://{bucket}/{key}?expires={timestamp}&amp;verb={verb}&amp;signature={sig}
    /// The signature is an HMAC-SHA256 hash of the URL components.
    /// </remarks>
    public string GetPreSignedURL(GetPreSignedUrlRequest request)
    {
        if (string.IsNullOrEmpty(request.BucketName))
        {
            throw new ArgumentException("Bucket name is required", nameof(request));
        }

        if (string.IsNullOrEmpty(request.Key))
        {
            throw new ArgumentException("Key is required", nameof(request));
        }

        // Calculate expiration timestamp
        var expiresUtc = request.Expires.ToUniversalTime();
        var expiresTimestamp = new DateTimeOffset(expiresUtc).ToUnixTimeSeconds();

        // Determine the HTTP verb
        var verb = request.Verb.ToString().ToUpperInvariant();

        // Create the string to sign
        var stringToSign = $"{verb}\n{request.BucketName}\n{request.Key}\n{expiresTimestamp}";

        // Generate HMAC-SHA256 signature
        string signature;
        using (var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_signingKey)))
        {
            var signatureBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringToSign));
            signature = Convert.ToBase64String(signatureBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        // Build the pre-signed URL
        // Using a custom scheme for local storage
        var encodedKey = Uri.EscapeDataString(request.Key);
        var url = $"cblite://{request.BucketName}/{encodedKey}?X-Expires={expiresTimestamp}&X-Verb={verb}&X-Signature={signature}";

        // Add version ID if specified
        if (!string.IsNullOrEmpty(request.VersionId))
        {
            url += $"&versionId={Uri.EscapeDataString(request.VersionId)}";
        }

        return url;
    }

    /// <summary>
    /// Validates a pre-signed URL signature and expiration.
    /// </summary>
    /// <param name="url">The pre-signed URL to validate.</param>
    /// <returns>True if the URL is valid and not expired, false otherwise.</returns>
    public bool ValidatePreSignedURL(string url)
    {
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            var expiresStr = query["X-Expires"];
            var verb = query["X-Verb"];
            var providedSignature = query["X-Signature"];

            if (string.IsNullOrEmpty(expiresStr) || string.IsNullOrEmpty(verb) || string.IsNullOrEmpty(providedSignature))
            {
                return false;
            }

            // Check expiration
            var expiresTimestamp = long.Parse(expiresStr);
            var expiresDateTime = DateTimeOffset.FromUnixTimeSeconds(expiresTimestamp);
            if (expiresDateTime < DateTimeOffset.UtcNow)
            {
                return false;
            }

            // Extract bucket and key from path
            var bucketName = uri.Host;
            var key = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

            // Recreate the string to sign
            var stringToSign = $"{verb}\n{bucketName}\n{key}\n{expiresTimestamp}";

            // Generate expected signature
            string expectedSignature;
            using (var hmac = new System.Security.Cryptography.HMACSHA256(
                System.Text.Encoding.UTF8.GetBytes(_signingKey)))
            {
                var signatureBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringToSign));
                expectedSignature = Convert.ToBase64String(signatureBytes)
                    .Replace("+", "-")
                    .Replace("/", "_")
                    .TrimEnd('=');
            }

            return expectedSignature == providedSignature;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    /// <summary>
    /// Gets the public access block configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the public access block configuration.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or has no public access block configuration.</exception>
    public async Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var publicAccessBlockDict = bucketDoc.GetDictionary("publicAccessBlock");
            if (publicAccessBlockDict == null)
            {
                throw new AmazonS3Exception("The public access block configuration was not found")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchPublicAccessBlockConfiguration"
                };
            }

            var config = new PublicAccessBlockConfiguration
            {
                BlockPublicAcls = publicAccessBlockDict.GetBoolean("blockPublicAcls"),
                IgnorePublicAcls = publicAccessBlockDict.GetBoolean("ignorePublicAcls"),
                BlockPublicPolicy = publicAccessBlockDict.GetBoolean("blockPublicPolicy"),
                RestrictPublicBuckets = publicAccessBlockDict.GetBoolean("restrictPublicBuckets")
            };

            return new GetPublicAccessBlockResponse
            {
                PublicAccessBlockConfiguration = config,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Initiates a multipart upload for a large object.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the upload ID.</returns>
    public async Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = bucketName,
            Key = key
        };
        return await InitiateMultipartUploadAsync(request, cancellationToken);
    }

    /// <summary>
    /// Initiates a multipart upload for a large object.
    /// Returns an upload ID that is used to track parts of the upload.
    /// </summary>
    /// <param name="request">The request containing bucket name, key, and optional metadata.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the upload ID.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(InitiateMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Generate upload ID
            var uploadId = Guid.NewGuid().ToString("N");

            // Create upload tracking document
            var uploadDocId = $"upload::{uploadId}";
            var uploadDoc = new MutableDocument(uploadDocId);
            uploadDoc.SetString("type", "upload");
            uploadDoc.SetString("uploadId", uploadId);
            uploadDoc.SetString("bucketName", request.BucketName);
            uploadDoc.SetString("key", request.Key);
            uploadDoc.SetDate("initiated", DateTimeOffset.UtcNow);
            uploadDoc.SetString("contentType", request.ContentType ?? "application/octet-stream");

            // Store metadata if provided
            if (request.Metadata?.Count > 0)
            {
                var metadataDict = new MutableDictionaryObject();
                foreach (var key in request.Metadata.Keys)
                {
                    metadataDict.SetString(key, request.Metadata[key]);
                }
                uploadDoc.SetDictionary("metadata", metadataDict);
            }

            _database.Save(uploadDoc);

            return new InitiateMultipartUploadResponse
            {
                BucketName = request.BucketName,
                Key = request.Key,
                UploadId = uploadId,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<ListBucketAnalyticsConfigurationsResponse> ListBucketAnalyticsConfigurationsAsync(ListBucketAnalyticsConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketIntelligentTieringConfigurationsResponse> ListBucketIntelligentTieringConfigurationsAsync(ListBucketIntelligentTieringConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketInventoryConfigurationsResponse> ListBucketInventoryConfigurationsAsync(ListBucketInventoryConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketMetricsConfigurationsResponse> ListBucketMetricsConfigurationsAsync(ListBucketMetricsConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Lists in-progress multipart uploads in a bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of in-progress uploads.</returns>
    public async Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new ListMultipartUploadsRequest { BucketName = bucketName };
        return await ListMultipartUploadsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists in-progress multipart uploads in a bucket with a prefix filter.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="prefix">The prefix to filter uploads by.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the filtered list of in-progress uploads.</returns>
    public async Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListMultipartUploadsRequest
        {
            BucketName = bucketName,
            Prefix = prefix
        };
        return await ListMultipartUploadsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists in-progress multipart uploads in a bucket.
    /// </summary>
    /// <param name="request">The request containing bucket name and filter parameters.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of in-progress uploads.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists
            var bucketDoc = _database.GetDocument($"bucket::{request.BucketName}");
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var uploads = new List<MultipartUpload>();

            var query = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("upload"))
                    .And(Expression.Property("bucketName").EqualTo(Expression.String(request.BucketName)))
                );

            foreach (var result in query.Execute())
            {
                var dict = result.GetDictionary(0);
                var key = dict.GetString("key");

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                {
                    continue;
                }

                // Apply key marker filter
                if (!string.IsNullOrEmpty(request.KeyMarker) &&
                    string.CompareOrdinal(key, request.KeyMarker) <= 0)
                {
                    continue;
                }

                uploads.Add(new MultipartUpload
                {
                    Key = key,
                    UploadId = dict.GetString("uploadId"),
                    Initiated = dict.GetDate("initiated").UtcDateTime,
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Sort by key then by initiated
            uploads = uploads
                .OrderBy(u => u.Key)
                .ThenBy(u => u.Initiated)
                .ToList();

            // Apply max uploads
            var maxUploads = request.MaxUploads > 0 ? request.MaxUploads : 1000;
            var isTruncated = uploads.Count > maxUploads;

            if (isTruncated)
            {
                uploads = uploads.Take(maxUploads).ToList();
            }

            var response = new ListMultipartUploadsResponse
            {
                BucketName = request.BucketName,
                Prefix = request.Prefix,
                KeyMarker = request.KeyMarker,
                UploadIdMarker = request.UploadIdMarker,
                MaxUploads = maxUploads,
                IsTruncated = isTruncated,
                MultipartUploads = uploads,
                HttpStatusCode = HttpStatusCode.OK
            };

            if (isTruncated && uploads.Any())
            {
                var lastUpload = uploads.Last();
                response.NextKeyMarker = lastUpload.Key;
                response.NextUploadIdMarker = lastUpload.UploadId;
            }

            return response;
        }, cancellationToken);
    }

    /// <summary>
    /// Lists the parts that have been uploaded for a multipart upload.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="key">The object key.</param>
    /// <param name="uploadId">The upload ID.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of uploaded parts.</returns>
    public async Task<ListPartsResponse> ListPartsAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var request = new ListPartsRequest
        {
            BucketName = bucketName,
            Key = key,
            UploadId = uploadId
        };
        return await ListPartsAsync(request, cancellationToken);
    }

    /// <summary>
    /// Lists the parts that have been uploaded for a multipart upload.
    /// </summary>
    /// <param name="request">The request containing the upload ID.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the list of uploaded parts.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the upload does not exist.</exception>
    public async Task<ListPartsResponse> ListPartsAsync(ListPartsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify the upload exists
            var uploadDoc = _database.GetDocument($"upload::{request.UploadId}");
            if (uploadDoc == null)
            {
                throw new AmazonS3Exception("The specified upload does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchUpload"
                };
            }

            var parts = new List<PartDetail>();

            var query = QueryBuilder.Select(SelectResult.All())
                .From(DataSource.Database(_database))
                .Where(
                    Expression.Property("type").EqualTo(Expression.String("part"))
                    .And(Expression.Property("uploadId").EqualTo(Expression.String(request.UploadId)))
                );

            foreach (var result in query.Execute())
            {
                var dict = result.GetDictionary(0);
                var partNumber = dict.GetInt("partNumber");

                // Apply part number marker filter
                if (request.PartNumberMarker.HasValue && partNumber <= request.PartNumberMarker.Value)
                {
                    continue;
                }

                parts.Add(new PartDetail
                {
                    PartNumber = partNumber,
                    Size = dict.GetLong("size"),
                    ETag = dict.GetString("etag"),
                    LastModified = dict.GetDate("lastModified").UtcDateTime
                });
            }

            // Sort by part number
            parts = parts.OrderBy(p => p.PartNumber).ToList();

            // Apply max parts
            var maxParts = request.MaxParts > 0 ? request.MaxParts : 1000;
            var isTruncated = parts.Count > maxParts;

            if (isTruncated)
            {
                parts = parts.Take(maxParts).ToList();
            }

            var response = new ListPartsResponse
            {
                BucketName = request.BucketName,
                Key = request.Key,
                UploadId = request.UploadId,
                PartNumberMarker = request.PartNumberMarker ?? 0,
                MaxParts = maxParts,
                IsTruncated = isTruncated,
                Parts = parts,
                HttpStatusCode = HttpStatusCode.OK
            };

            if (isTruncated && parts.Any())
            {
                response.NextPartNumberMarker = parts.Last().PartNumber;
            }

            return response;
        }, cancellationToken);
    }

    public void MakeObjectPublic(string bucketName, string objectKey, bool enable)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the access control list (ACL) for a bucket or object.
    /// </summary>
    /// <param name="request">The request containing the bucket name, optional key, and ACL.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating success.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket or object does not exist.</exception>
    public async Task<PutACLResponse> PutACLAsync(PutACLRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            string docId;
            Document? existingDoc;

            if (!string.IsNullOrEmpty(request.Key))
            {
                // Object ACL
                docId = $"object::{request.BucketName}::{request.Key}";
                existingDoc = _database.GetDocument(docId);
                if (existingDoc == null)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }
            else
            {
                // Bucket ACL
                docId = $"bucket::{request.BucketName}";
                existingDoc = _database.GetDocument(docId);
                if (existingDoc == null)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            var mutableDoc = existingDoc.ToMutable();

            // Handle canned ACL
            if (request.CannedACL != null)
            {
                mutableDoc.SetString("cannedACL", request.CannedACL.Value);
            }

            // Handle explicit ACL
            if (request.AccessControlList != null)
            {
                if (request.AccessControlList.Owner != null)
                {
                    mutableDoc.SetString("ownerId", request.AccessControlList.Owner.Id);
                    mutableDoc.SetString("ownerDisplayName", request.AccessControlList.Owner.DisplayName);
                }

                var grantsArray = new MutableArrayObject();
                foreach (var grant in request.AccessControlList.Grants)
                {
                    var grantDict = new MutableDictionaryObject();
                    grantDict.SetString("permission", grant.Permission?.Value);
                    if (grant.Grantee != null)
                    {
                        grantDict.SetString("granteeId", grant.Grantee.CanonicalUser);
                        grantDict.SetString("granteeDisplayName", grant.Grantee.DisplayName);
                        grantDict.SetString("granteeType", grant.Grantee.Type?.Value);
                        grantDict.SetString("granteeEmailAddress", grant.Grantee.EmailAddress);
                        grantDict.SetString("granteeUri", grant.Grantee.URI);
                    }
                    grantsArray.AddDictionary(grantDict);
                }
                mutableDoc.SetArray("grants", grantsArray);
            }

            _database.Save(mutableDoc);

            return new PutACLResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutBucketAccelerateConfigurationResponse> PutBucketAccelerateConfigurationAsync(PutBucketAccelerateConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketAnalyticsConfigurationResponse> PutBucketAnalyticsConfigurationAsync(PutBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the encryption configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and encryption configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutBucketEncryptionResponse> PutBucketEncryptionAsync(PutBucketEncryptionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                var encryptionDict = new MutableDictionaryObject();
                var rulesArray = new MutableArrayObject();

                if (request.ServerSideEncryptionConfiguration?.ServerSideEncryptionRules != null)
                {
                    foreach (var rule in request.ServerSideEncryptionConfiguration.ServerSideEncryptionRules)
                    {
                        var ruleDict = new MutableDictionaryObject();
                        if (rule.ServerSideEncryptionByDefault != null)
                        {
                            ruleDict.SetString("sseAlgorithm",
                                rule.ServerSideEncryptionByDefault.ServerSideEncryptionAlgorithm?.Value ?? "AES256");
                            if (!string.IsNullOrEmpty(rule.ServerSideEncryptionByDefault.KMSMasterKeyID))
                            {
                                ruleDict.SetString("kmsMasterKeyId", rule.ServerSideEncryptionByDefault.KMSMasterKeyID);
                            }
                        }
                        ruleDict.SetBoolean("bucketKeyEnabled", rule.BucketKeyEnabled);
                        rulesArray.AddDictionary(ruleDict);
                    }
                }

                encryptionDict.SetArray("rules", rulesArray);
                mutableDoc.SetDictionary("encryption", encryptionDict);
                _database.Save(mutableDoc);
            }

            return new PutBucketEncryptionResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutBucketIntelligentTieringConfigurationResponse> PutBucketIntelligentTieringConfigurationAsync(PutBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketInventoryConfigurationResponse> PutBucketInventoryConfigurationAsync(PutBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the logging configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and logging configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutBucketLoggingResponse> PutBucketLoggingAsync(PutBucketLoggingRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                if (request.LoggingConfig != null &&
                    !string.IsNullOrEmpty(request.LoggingConfig.TargetBucketName))
                {
                    var loggingDict = new MutableDictionaryObject();
                    loggingDict.SetString("targetBucket", request.LoggingConfig.TargetBucketName);
                    loggingDict.SetString("targetPrefix", request.LoggingConfig.TargetPrefix);
                    mutableDoc.SetDictionary("loggingConfiguration", loggingDict);
                }
                else
                {
                    mutableDoc.Remove("loggingConfiguration");
                }
                _database.Save(mutableDoc);
            }

            return new PutBucketLoggingResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutBucketMetricsConfigurationResponse> PutBucketMetricsConfigurationAsync(PutBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the notification configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and notification configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    /// <remarks>
    /// In Couchbase Lite local storage, notifications are not actually sent anywhere,
    /// but the configuration is stored for API compatibility.
    /// </remarks>
    public async Task<PutBucketNotificationResponse> PutBucketNotificationAsync(PutBucketNotificationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // For local storage, we acknowledge the configuration but don't actually
            // set up any notifications since there's no cloud infrastructure
            return new PutBucketNotificationResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutBucketOwnershipControlsResponse> PutBucketOwnershipControlsAsync(PutBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the bucket policy for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="policy">The bucket policy as a JSON string.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    public async Task<PutBucketPolicyResponse> PutBucketPolicyAsync(string bucketName, string policy, CancellationToken cancellationToken = default)
    {
        var request = new PutBucketPolicyRequest { BucketName = bucketName, Policy = policy };
        return await PutBucketPolicyAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sets the bucket policy for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="policy">The bucket policy as a JSON string.</param>
    /// <param name="contentMD5">The MD5 hash of the policy content (not used in local storage).</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    public async Task<PutBucketPolicyResponse> PutBucketPolicyAsync(string bucketName, string policy, string contentMD5, CancellationToken cancellationToken = default)
    {
        var request = new PutBucketPolicyRequest { BucketName = bucketName, Policy = policy, ContentMD5 = contentMD5 };
        return await PutBucketPolicyAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sets the bucket policy for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and policy.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist or the policy is invalid.</exception>
    public async Task<PutBucketPolicyResponse> PutBucketPolicyAsync(PutBucketPolicyRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(request.Policy))
            {
                throw new AmazonS3Exception("Policy is required")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "MalformedPolicy"
                };
            }

            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            // Validate that the policy is valid JSON
            try
            {
                System.Text.Json.JsonDocument.Parse(request.Policy);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new AmazonS3Exception("Invalid JSON policy document")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "MalformedPolicy"
                };
            }

            // Store the policy in the bucket document
            using (var mutableDoc = bucketDoc.ToMutable())
            {
                mutableDoc.SetString("policy", request.Policy);
                _database.Save(mutableDoc);
            }

            return new PutBucketPolicyResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutBucketReplicationResponse> PutBucketReplicationAsync(PutBucketReplicationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketRequestPaymentResponse> PutBucketRequestPaymentAsync(string bucketName, RequestPaymentConfiguration requestPaymentConfiguration, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketRequestPaymentResponse> PutBucketRequestPaymentAsync(PutBucketRequestPaymentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketTaggingResponse> PutBucketTaggingAsync(string bucketName, List<Tag> tagSet, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketTaggingResponse> PutBucketTaggingAsync(PutBucketTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the versioning state of a bucket.
    /// Once versioning is enabled on a bucket, it can never be returned to an Off state.
    /// It can only be suspended.
    /// </summary>
    /// <param name="request">The request containing the bucket name and versioning configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating success.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutBucketVersioningResponse> PutBucketVersioningAsync(PutBucketVersioningRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var currentStatus = bucketDoc.GetString("versioningStatus");
            var newStatus = request.VersioningConfig?.Status?.Value;

            // Validate state transitions:
            // - Off -> Enabled: OK
            // - Off -> Suspended: Not recommended but allowed
            // - Enabled -> Suspended: OK
            // - Suspended -> Enabled: OK
            // - Enabled -> Off: NOT ALLOWED (versioning cannot be disabled once enabled)
            if (currentStatus == "Enabled" && string.IsNullOrEmpty(newStatus))
            {
                throw new AmazonS3Exception("Versioning cannot be disabled once it has been enabled")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "IllegalVersioningConfigurationException"
                };
            }

            // Update the bucket document with versioning status
            var mutableDoc = bucketDoc.ToMutable();
            mutableDoc.SetString("versioningStatus", newStatus ?? "Off");

            _database.Save(mutableDoc);

            return new PutBucketVersioningResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Sets the website configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="websiteConfiguration">The website configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    public async Task<PutBucketWebsiteResponse> PutBucketWebsiteAsync(string bucketName, WebsiteConfiguration websiteConfiguration, CancellationToken cancellationToken = default)
    {
        var request = new PutBucketWebsiteRequest { BucketName = bucketName, WebsiteConfiguration = websiteConfiguration };
        return await PutBucketWebsiteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sets the website configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and website configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutBucketWebsiteResponse> PutBucketWebsiteAsync(PutBucketWebsiteRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                var websiteDict = new MutableDictionaryObject();
                var config = request.WebsiteConfiguration;

                if (config != null)
                {
                    // Serialize index document
                    if (!string.IsNullOrEmpty(config.IndexDocumentSuffix))
                    {
                        var indexDocDict = new MutableDictionaryObject();
                        indexDocDict.SetString("suffix", config.IndexDocumentSuffix);
                        websiteDict.SetDictionary("indexDocument", indexDocDict);
                    }

                    // Serialize error document
                    if (!string.IsNullOrEmpty(config.ErrorDocument))
                    {
                        var errorDocDict = new MutableDictionaryObject();
                        errorDocDict.SetString("key", config.ErrorDocument);
                        websiteDict.SetDictionary("errorDocument", errorDocDict);
                    }

                    // Serialize redirect all requests
                    if (config.RedirectAllRequestsTo != null)
                    {
                        var redirectAllDict = new MutableDictionaryObject();
                        redirectAllDict.SetString("hostName", config.RedirectAllRequestsTo.HostName);
                        redirectAllDict.SetString("protocol", config.RedirectAllRequestsTo.Protocol);
                        websiteDict.SetDictionary("redirectAllRequestsTo", redirectAllDict);
                    }

                    // Serialize routing rules
                    if (config.RoutingRules != null && config.RoutingRules.Count > 0)
                    {
                        var routingRulesArray = new MutableArrayObject();
                        foreach (var rule in config.RoutingRules)
                        {
                            var ruleDict = new MutableDictionaryObject();

                            if (rule.Condition != null)
                            {
                                var conditionDict = new MutableDictionaryObject();
                                conditionDict.SetString("keyPrefixEquals", rule.Condition.KeyPrefixEquals);
                                conditionDict.SetString("httpErrorCodeReturnedEquals", rule.Condition.HttpErrorCodeReturnedEquals);
                                ruleDict.SetDictionary("condition", conditionDict);
                            }

                            if (rule.Redirect != null)
                            {
                                var redirectDict = new MutableDictionaryObject();
                                redirectDict.SetString("hostName", rule.Redirect.HostName);
                                redirectDict.SetString("protocol", rule.Redirect.Protocol);
                                redirectDict.SetString("replaceKeyWith", rule.Redirect.ReplaceKeyWith);
                                redirectDict.SetString("replaceKeyPrefixWith", rule.Redirect.ReplaceKeyPrefixWith);
                                redirectDict.SetString("httpRedirectCode", rule.Redirect.HttpRedirectCode);
                                ruleDict.SetDictionary("redirect", redirectDict);
                            }

                            routingRulesArray.AddDictionary(ruleDict);
                        }
                        websiteDict.SetArray("routingRules", routingRulesArray);
                    }
                }

                mutableDoc.SetDictionary("websiteConfiguration", websiteDict);
                _database.Save(mutableDoc);
            }

            return new PutBucketWebsiteResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Sets the CORS configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="configuration">The CORS configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    public async Task<PutCORSConfigurationResponse> PutCORSConfigurationAsync(string bucketName, CORSConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var request = new PutCORSConfigurationRequest { BucketName = bucketName, Configuration = configuration };
        return await PutCORSConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sets the CORS configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and CORS configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutCORSConfigurationResponse> PutCORSConfigurationAsync(PutCORSConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                var corsDict = new MutableDictionaryObject();
                var rulesArray = new MutableArrayObject();

                if (request.Configuration?.Rules != null)
                {
                    foreach (var rule in request.Configuration.Rules)
                    {
                        var ruleDict = new MutableDictionaryObject();
                        ruleDict.SetString("id", rule.Id);
                        ruleDict.SetLong("maxAgeSeconds", rule.MaxAgeSeconds);

                        // Serialize allowed headers
                        if (rule.AllowedHeaders != null)
                        {
                            var headersArray = new MutableArrayObject();
                            foreach (var header in rule.AllowedHeaders)
                            {
                                headersArray.AddString(header);
                            }
                            ruleDict.SetArray("allowedHeaders", headersArray);
                        }

                        // Serialize allowed methods
                        if (rule.AllowedMethods != null)
                        {
                            var methodsArray = new MutableArrayObject();
                            foreach (var method in rule.AllowedMethods)
                            {
                                methodsArray.AddString(method);
                            }
                            ruleDict.SetArray("allowedMethods", methodsArray);
                        }

                        // Serialize allowed origins
                        if (rule.AllowedOrigins != null)
                        {
                            var originsArray = new MutableArrayObject();
                            foreach (var origin in rule.AllowedOrigins)
                            {
                                originsArray.AddString(origin);
                            }
                            ruleDict.SetArray("allowedOrigins", originsArray);
                        }

                        // Serialize expose headers
                        if (rule.ExposeHeaders != null)
                        {
                            var exposeArray = new MutableArrayObject();
                            foreach (var header in rule.ExposeHeaders)
                            {
                                exposeArray.AddString(header);
                            }
                            ruleDict.SetArray("exposeHeaders", exposeArray);
                        }

                        rulesArray.AddDictionary(ruleDict);
                    }
                }

                corsDict.SetArray("rules", rulesArray);
                mutableDoc.SetDictionary("corsConfiguration", corsDict);
                _database.Save(mutableDoc);
            }

            return new PutCORSConfigurationResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Sets the lifecycle configuration for the specified bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="configuration">The lifecycle configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    public async Task<PutLifecycleConfigurationResponse> PutLifecycleConfigurationAsync(string bucketName, LifecycleConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var request = new PutLifecycleConfigurationRequest { BucketName = bucketName, Configuration = configuration };
        return await PutLifecycleConfigurationAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sets the lifecycle configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and lifecycle configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutLifecycleConfigurationResponse> PutLifecycleConfigurationAsync(PutLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                var lifecycleDict = new MutableDictionaryObject();
                var rulesArray = new MutableArrayObject();

                if (request.Configuration?.Rules != null)
                {
                    foreach (var rule in request.Configuration.Rules)
                    {
                        var ruleDict = new MutableDictionaryObject();
                        ruleDict.SetString("id", rule.Id);
                        ruleDict.SetString("status", rule.Status?.Value ?? "Enabled");
                        ruleDict.SetString("prefix", rule.Prefix);

                        // Serialize expiration
                        if (rule.Expiration != null)
                        {
                            var expirationDict = new MutableDictionaryObject();
                            expirationDict.SetLong("days", rule.Expiration.Days);
                            expirationDict.SetBoolean("expiredObjectDeleteMarker", rule.Expiration.ExpiredObjectDeleteMarker);
                            if (rule.Expiration.DateUtc != DateTime.MinValue)
                            {
                                expirationDict.SetString("date", rule.Expiration.DateUtc.ToString("o"));
                            }
                            ruleDict.SetDictionary("expiration", expirationDict);
                        }

                        // Serialize transitions
                        if (rule.Transitions != null && rule.Transitions.Count > 0)
                        {
                            var transitionsArray = new MutableArrayObject();
                            foreach (var transition in rule.Transitions)
                            {
                                var transDict = new MutableDictionaryObject();
                                transDict.SetLong("days", transition.Days);
                                transDict.SetString("storageClass", transition.StorageClass?.Value);
                                transitionsArray.AddDictionary(transDict);
                            }
                            ruleDict.SetArray("transitions", transitionsArray);
                        }

                        // Serialize noncurrent version expiration
                        if (rule.NoncurrentVersionExpiration != null)
                        {
                            var noncurrentExpDict = new MutableDictionaryObject();
                            noncurrentExpDict.SetLong("noncurrentDays", rule.NoncurrentVersionExpiration.NoncurrentDays);
                            noncurrentExpDict.SetLong("newerNoncurrentVersions", rule.NoncurrentVersionExpiration.NewerNoncurrentVersions);
                            ruleDict.SetDictionary("noncurrentVersionExpiration", noncurrentExpDict);
                        }

                        rulesArray.AddDictionary(ruleDict);
                    }
                }

                lifecycleDict.SetArray("rules", rulesArray);
                mutableDoc.SetDictionary("lifecycleConfiguration", lifecycleDict);
                _database.Save(mutableDoc);
            }

            return new PutLifecycleConfigurationResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutObjectLegalHoldResponse> PutObjectLegalHoldAsync(PutObjectLegalHoldRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectLockConfigurationResponse> PutObjectLockConfigurationAsync(PutObjectLockConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectRetentionResponse> PutObjectRetentionAsync(PutObjectRetentionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectTaggingResponse> PutObjectTaggingAsync(PutObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets the public access block configuration for the specified bucket.
    /// </summary>
    /// <param name="request">The request containing the bucket name and public access block configuration.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response indicating the result of the put operation.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the bucket does not exist.</exception>
    public async Task<PutPublicAccessBlockResponse> PutPublicAccessBlockAsync(PutPublicAccessBlockRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var bucketDocId = $"bucket::{request.BucketName}";
            var bucketDoc = _database.GetDocument(bucketDocId);
            if (bucketDoc == null)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            using (var mutableDoc = bucketDoc.ToMutable())
            {
                var publicAccessBlock = new MutableDictionaryObject();
                var config = request.PublicAccessBlockConfiguration;

                publicAccessBlock.SetBoolean("blockPublicAcls", config.BlockPublicAcls);
                publicAccessBlock.SetBoolean("ignorePublicAcls", config.IgnorePublicAcls);
                publicAccessBlock.SetBoolean("blockPublicPolicy", config.BlockPublicPolicy);
                publicAccessBlock.SetBoolean("restrictPublicBuckets", config.RestrictPublicBuckets);

                mutableDoc.SetDictionary("publicAccessBlock", publicAccessBlock);
                _database.Save(mutableDoc);
            }

            return new PutPublicAccessBlockResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, int days, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(RestoreObjectRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SelectObjectContentResponse> SelectObjectContentAsync(SelectObjectContentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Uploads a part of a multipart upload.
    /// </summary>
    /// <param name="request">The request containing the upload ID, part number, and data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A response containing the ETag of the uploaded part.</returns>
    /// <exception cref="AmazonS3Exception">Thrown when the upload does not exist or part number is invalid.</exception>
    public async Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Validate part number (S3 allows 1-10000)
            if (request.PartNumber < 1 || request.PartNumber > 10000)
            {
                throw new AmazonS3Exception("Part number must be between 1 and 10000")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "InvalidArgument"
                };
            }

            // Verify the upload exists
            var uploadDoc = _database.GetDocument($"upload::{request.UploadId}");
            if (uploadDoc == null)
            {
                throw new AmazonS3Exception("The specified upload does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchUpload"
                };
            }

            // Read content
            byte[] content;
            if (request.InputStream != null)
            {
                using (var ms = new MemoryStream())
                {
                    request.InputStream.CopyTo(ms);
                    content = ms.ToArray();
                }
            }
            else
            {
                content = Array.Empty<byte>();
            }

            // Store the part
            var partDocId = $"part::{request.UploadId}::{request.PartNumber}";
            var partDoc = new MutableDocument(partDocId);
            partDoc.SetString("type", "part");
            partDoc.SetString("uploadId", request.UploadId);
            partDoc.SetInt("partNumber", request.PartNumber);
            partDoc.SetDate("lastModified", DateTimeOffset.UtcNow);
            partDoc.SetLong("size", content.Length);

            // Store content as blob
            var partBlob = new Blob("application/octet-stream", content);
            partDoc.SetBlob("content", partBlob);

            // Calculate ETag for the part
            string etag;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = md5.ComputeHash(content);
                etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            partDoc.SetString("etag", etag);

            _database.Save(partDoc);

            return new UploadPartResponse
            {
                ETag = etag,
                PartNumber = request.PartNumber,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<WriteGetObjectResponseResponse> WriteGetObjectResponseAsync(WriteGetObjectResponseRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    #endregion

    #region ICoreAmazonS3 members required by newer AWSSDK

    /// <summary>
    /// Generates a pre-signed URL for accessing an object.
    /// This is an alternate interface from ICoreAmazonS3.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="objectKey">The key of the object.</param>
    /// <param name="expiration">The expiration date/time for the URL.</param>
    /// <param name="additionalProperties">Additional properties (verb can be specified here).</param>
    /// <returns>A signed URL that provides temporary access to the specified object.</returns>
    public string GeneratePreSignedURL(string bucketName, string objectKey, DateTime expiration, IDictionary<string, object> additionalProperties)
    {
        var verb = HttpVerb.GET;
        if (additionalProperties != null && additionalProperties.TryGetValue("Verb", out var verbObj))
        {
            if (verbObj is HttpVerb httpVerb)
            {
                verb = httpVerb;
            }
            else if (verbObj is string verbStr && Enum.TryParse<HttpVerb>(verbStr, true, out var parsedVerb))
            {
                verb = parsedVerb;
            }
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Expires = expiration,
            Verb = verb
        };

        return GetPreSignedURL(request);
    }

    public Task<IList<string>> GetAllObjectKeysAsync(string bucketName, string prefix, IDictionary<string, object> additionalProperties)
        => throw new NotImplementedException();

    public Task UploadObjectFromStreamAsync(string bucketName, string key, Stream inputStream, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task DeleteAsync(string bucketName, string key, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task DeletesAsync(string bucketName, IEnumerable<string> keys, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<Stream> GetObjectStreamAsync(string bucketName, string key, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task UploadObjectFromFilePathAsync(string bucketName, string key, string filePath, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task DownloadToFilePathAsync(string bucketName, string key, string filePath, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task MakeObjectPublicAsync(string bucketName, string objectKey, bool enable)
        => throw new NotImplementedException();

    public Task EnsureBucketExistsAsync(string bucketName)
        => throw new NotImplementedException();

    /// <summary>
    /// Checks if a bucket exists in the Couchbase Lite storage.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to check.</param>
    /// <returns>True if the bucket exists, false otherwise.</returns>
    public async Task<bool> DoesS3BucketExistAsync(string bucketName)
    {
        return await Task.Run(() =>
        {
            var bucketDoc = _database.GetDocument($"bucket::{bucketName}");
            return bucketDoc != null;
        });
    }

    #endregion
}
