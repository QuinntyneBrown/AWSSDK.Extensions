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

    #region Not Implemented Operations

    public Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<CopyObjectResponse> CopyObjectAsync(CopyObjectRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListObjectsResponse> ListObjectsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListObjectsResponse> ListObjectsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListObjectsResponse> ListObjectsAsync(ListObjectsRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListVersionsResponse> ListVersionsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListVersionsResponse> ListVersionsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    // Newer async interface members introduced in recent AWSSDK versions

    public Task<string> GetPreSignedURLAsync(GetPreSignedUrlRequest request)
    {
        throw new NotImplementedException();
    }

    public Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string sourceVersionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string uploadId, string partNumber, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

    public Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

    public Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CopyPartResponse> CopyPartAsync(CopyPartRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketAnalyticsConfigurationResponse> DeleteBucketAnalyticsConfigurationAsync(DeleteBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketEncryptionResponse> DeleteBucketEncryptionAsync(DeleteBucketEncryptionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketIntelligentTieringConfigurationResponse> DeleteBucketIntelligentTieringConfigurationAsync(DeleteBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketInventoryConfigurationResponse> DeleteBucketInventoryConfigurationAsync(DeleteBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketMetricsConfigurationResponse> DeleteBucketMetricsConfigurationAsync(DeleteBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketOwnershipControlsResponse> DeleteBucketOwnershipControlsAsync(DeleteBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketPolicyResponse> DeleteBucketPolicyAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketPolicyResponse> DeleteBucketPolicyAsync(DeleteBucketPolicyRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketReplicationResponse> DeleteBucketReplicationAsync(DeleteBucketReplicationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketTaggingResponse> DeleteBucketTaggingAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketTaggingResponse> DeleteBucketTaggingAsync(DeleteBucketTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketWebsiteResponse> DeleteBucketWebsiteAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteBucketWebsiteResponse> DeleteBucketWebsiteAsync(DeleteBucketWebsiteRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteCORSConfigurationResponse> DeleteCORSConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteCORSConfigurationResponse> DeleteCORSConfigurationAsync(DeleteCORSConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteLifecycleConfigurationResponse> DeleteLifecycleConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteLifecycleConfigurationResponse> DeleteLifecycleConfigurationAsync(DeleteLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeleteObjectTaggingResponse> DeleteObjectTaggingAsync(DeleteObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<DeletePublicAccessBlockResponse> DeletePublicAccessBlockAsync(DeletePublicAccessBlockRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void EnsureBucketExists(string bucketName)
        => throw new NotImplementedException();

    public Task<GetACLResponse> GetACLAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetACLResponse> GetACLAsync(GetACLRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketAccelerateConfigurationResponse> GetBucketAccelerateConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketAccelerateConfigurationResponse> GetBucketAccelerateConfigurationAsync(GetBucketAccelerateConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketAnalyticsConfigurationResponse> GetBucketAnalyticsConfigurationAsync(GetBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketEncryptionResponse> GetBucketEncryptionAsync(GetBucketEncryptionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketIntelligentTieringConfigurationResponse> GetBucketIntelligentTieringConfigurationAsync(GetBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketInventoryConfigurationResponse> GetBucketInventoryConfigurationAsync(GetBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLocationResponse> GetBucketLocationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLocationResponse> GetBucketLocationAsync(GetBucketLocationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLoggingResponse> GetBucketLoggingAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketLoggingResponse> GetBucketLoggingAsync(GetBucketLoggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketMetricsConfigurationResponse> GetBucketMetricsConfigurationAsync(GetBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketNotificationResponse> GetBucketNotificationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketNotificationResponse> GetBucketNotificationAsync(GetBucketNotificationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketOwnershipControlsResponse> GetBucketOwnershipControlsAsync(GetBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketPolicyResponse> GetBucketPolicyAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketPolicyResponse> GetBucketPolicyAsync(GetBucketPolicyRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

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

    public Task<GetBucketVersioningResponse> GetBucketVersioningAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketVersioningResponse> GetBucketVersioningAsync(GetBucketVersioningRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketWebsiteResponse> GetBucketWebsiteAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetBucketWebsiteResponse> GetBucketWebsiteAsync(GetBucketWebsiteRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetCORSConfigurationResponse> GetCORSConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetCORSConfigurationResponse> GetCORSConfigurationAsync(GetCORSConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetLifecycleConfigurationResponse> GetLifecycleConfigurationAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetLifecycleConfigurationResponse> GetLifecycleConfigurationAsync(GetLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

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

    public string GetPreSignedURL(GetPreSignedUrlRequest request)
        => throw new NotImplementedException();

    public Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(InitiateMultipartUploadRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketAnalyticsConfigurationsResponse> ListBucketAnalyticsConfigurationsAsync(ListBucketAnalyticsConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketIntelligentTieringConfigurationsResponse> ListBucketIntelligentTieringConfigurationsAsync(ListBucketIntelligentTieringConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketInventoryConfigurationsResponse> ListBucketInventoryConfigurationsAsync(ListBucketInventoryConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListBucketMetricsConfigurationsResponse> ListBucketMetricsConfigurationsAsync(ListBucketMetricsConfigurationsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListPartsResponse> ListPartsAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListPartsResponse> ListPartsAsync(ListPartsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void MakeObjectPublic(string bucketName, string objectKey, bool enable)
        => throw new NotImplementedException();

    public Task<PutACLResponse> PutACLAsync(PutACLRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketAccelerateConfigurationResponse> PutBucketAccelerateConfigurationAsync(PutBucketAccelerateConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketAnalyticsConfigurationResponse> PutBucketAnalyticsConfigurationAsync(PutBucketAnalyticsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketEncryptionResponse> PutBucketEncryptionAsync(PutBucketEncryptionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketIntelligentTieringConfigurationResponse> PutBucketIntelligentTieringConfigurationAsync(PutBucketIntelligentTieringConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketInventoryConfigurationResponse> PutBucketInventoryConfigurationAsync(PutBucketInventoryConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketLoggingResponse> PutBucketLoggingAsync(PutBucketLoggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketMetricsConfigurationResponse> PutBucketMetricsConfigurationAsync(PutBucketMetricsConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketNotificationResponse> PutBucketNotificationAsync(PutBucketNotificationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketOwnershipControlsResponse> PutBucketOwnershipControlsAsync(PutBucketOwnershipControlsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketPolicyResponse> PutBucketPolicyAsync(string bucketName, string policy, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketPolicyResponse> PutBucketPolicyAsync(string bucketName, string policy, string contentMD5, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketPolicyResponse> PutBucketPolicyAsync(PutBucketPolicyRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

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

    public Task<PutBucketVersioningResponse> PutBucketVersioningAsync(PutBucketVersioningRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketWebsiteResponse> PutBucketWebsiteAsync(string bucketName, WebsiteConfiguration websiteConfiguration, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutBucketWebsiteResponse> PutBucketWebsiteAsync(PutBucketWebsiteRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutCORSConfigurationResponse> PutCORSConfigurationAsync(string bucketName, CORSConfiguration configuration, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutCORSConfigurationResponse> PutCORSConfigurationAsync(PutCORSConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutLifecycleConfigurationResponse> PutLifecycleConfigurationAsync(string bucketName, LifecycleConfiguration configuration, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutLifecycleConfigurationResponse> PutLifecycleConfigurationAsync(PutLifecycleConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectLegalHoldResponse> PutObjectLegalHoldAsync(PutObjectLegalHoldRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectLockConfigurationResponse> PutObjectLockConfigurationAsync(PutObjectLockConfigurationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectRetentionResponse> PutObjectRetentionAsync(PutObjectRetentionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutObjectTaggingResponse> PutObjectTaggingAsync(PutObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutPublicAccessBlockResponse> PutPublicAccessBlockAsync(PutPublicAccessBlockRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, int days, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(RestoreObjectRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SelectObjectContentResponse> SelectObjectContentAsync(SelectObjectContentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<WriteGetObjectResponseResponse> WriteGetObjectResponseAsync(WriteGetObjectResponseRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    #endregion

    #region ICoreAmazonS3 members required by newer AWSSDK

    public string GeneratePreSignedURL(string bucketName, string objectKey, DateTime expiration, IDictionary<string, object> additionalProperties)
        => throw new NotImplementedException();

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

    public Task<bool> DoesS3BucketExistAsync(string bucketName)
        => throw new NotImplementedException();

    #endregion
}
