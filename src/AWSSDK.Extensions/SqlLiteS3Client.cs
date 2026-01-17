// Copyright (c) Quinntyne Brown. All Rights Reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Amazon.Runtime;
using Amazon.Runtime.Endpoints;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AWSSDK.Extensions;

/// <summary>
/// SQLite implementation of Amazon S3 interface.
/// Uses SQLite for metadata and blob storage.
/// </summary>
public class SqlLiteS3Client : IAmazonS3, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _databasePath;
    private bool _disposed;

    public SqlLiteS3Client(string databasePath)
    {
        _databasePath = databasePath;
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        CreateTables();
    }

    public IS3PaginatorFactory Paginators => throw new NotImplementedException();
    public IClientConfig Config => throw new NotImplementedException();

    private void CreateTables()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS buckets (
                bucket_name TEXT PRIMARY KEY,
                creation_date TEXT NOT NULL,
                versioning_status TEXT,
                mfa_delete_enabled INTEGER DEFAULT 0,
                owner_id TEXT DEFAULT '123456789012',
                object_lock_enabled INTEGER DEFAULT 0,
                object_lock_mode TEXT,
                object_lock_days INTEGER,
                object_lock_years INTEGER
            );

            CREATE TABLE IF NOT EXISTS objects (
                id TEXT PRIMARY KEY,
                bucket_name TEXT NOT NULL,
                key TEXT NOT NULL,
                version_id TEXT,
                content BLOB,
                content_type TEXT DEFAULT 'application/octet-stream',
                size INTEGER DEFAULT 0,
                etag TEXT,
                last_modified TEXT NOT NULL,
                is_latest INTEGER DEFAULT 1,
                metadata TEXT,
                retention_mode TEXT,
                retention_until TEXT,
                legal_hold_status TEXT DEFAULT 'OFF',
                FOREIGN KEY (bucket_name) REFERENCES buckets(bucket_name)
            );

            CREATE TABLE IF NOT EXISTS versions (
                id TEXT PRIMARY KEY,
                bucket_name TEXT NOT NULL,
                key TEXT NOT NULL,
                version_id TEXT NOT NULL,
                content BLOB,
                content_type TEXT DEFAULT 'application/octet-stream',
                size INTEGER DEFAULT 0,
                etag TEXT,
                last_modified TEXT NOT NULL,
                is_latest INTEGER DEFAULT 0,
                is_delete_marker INTEGER DEFAULT 0,
                metadata TEXT,
                retention_mode TEXT,
                retention_until TEXT,
                legal_hold_status TEXT DEFAULT 'OFF',
                FOREIGN KEY (bucket_name) REFERENCES buckets(bucket_name)
            );

            CREATE TABLE IF NOT EXISTS delete_markers (
                id TEXT PRIMARY KEY,
                bucket_name TEXT NOT NULL,
                key TEXT NOT NULL,
                version_id TEXT NOT NULL,
                last_modified TEXT NOT NULL,
                is_latest INTEGER DEFAULT 1,
                FOREIGN KEY (bucket_name) REFERENCES buckets(bucket_name)
            );

            CREATE INDEX IF NOT EXISTS idx_objects_bucket_key ON objects(bucket_name, key);
            CREATE INDEX IF NOT EXISTS idx_versions_bucket_key ON versions(bucket_name, key);
            CREATE INDEX IF NOT EXISTS idx_delete_markers_bucket_key ON delete_markers(bucket_name, key);
        ";
        cmd.ExecuteNonQuery();

        // Add columns if they don't exist (for existing databases)
        AddColumnIfNotExists("buckets", "object_lock_enabled", "INTEGER DEFAULT 0");
        AddColumnIfNotExists("buckets", "object_lock_mode", "TEXT");
        AddColumnIfNotExists("buckets", "object_lock_days", "INTEGER");
        AddColumnIfNotExists("buckets", "object_lock_years", "INTEGER");
        AddColumnIfNotExists("objects", "retention_mode", "TEXT");
        AddColumnIfNotExists("objects", "retention_until", "TEXT");
        AddColumnIfNotExists("objects", "legal_hold_status", "TEXT DEFAULT 'OFF'");
        AddColumnIfNotExists("versions", "retention_mode", "TEXT");
        AddColumnIfNotExists("versions", "retention_until", "TEXT");
        AddColumnIfNotExists("versions", "legal_hold_status", "TEXT DEFAULT 'OFF'");
    }

    private void AddColumnIfNotExists(string tableName, string columnName, string columnDefinition)
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists
        }
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
            // Check if bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count > 0)
                {
                    throw new AmazonS3Exception("Bucket already exists")
                    {
                        StatusCode = HttpStatusCode.Conflict,
                        ErrorCode = "BucketAlreadyExists"
                    };
                }
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO buckets (bucket_name, creation_date) VALUES (@bucketName, @creationDate)";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@creationDate", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();

            return new PutBucketResponse { HttpStatusCode = HttpStatusCode.OK };
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
            // Check if bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Check if bucket is empty
            using (var objCheckCmd = _connection.CreateCommand())
            {
                objCheckCmd.CommandText = "SELECT COUNT(*) FROM objects WHERE bucket_name = @bucketName";
                objCheckCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var objCount = Convert.ToInt64(objCheckCmd.ExecuteScalar());
                if (objCount > 0)
                {
                    throw new AmazonS3Exception("Bucket is not empty")
                    {
                        StatusCode = HttpStatusCode.Conflict,
                        ErrorCode = "BucketNotEmpty"
                    };
                }
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM buckets WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.ExecuteNonQuery();

            return new DeleteBucketResponse { HttpStatusCode = HttpStatusCode.NoContent };
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
            var buckets = new List<S3Bucket>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT bucket_name, creation_date FROM buckets";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                buckets.Add(new S3Bucket
                {
                    BucketName = reader.GetString(0),
                    CreationDate = DateTime.Parse(reader.GetString(1))
                });
            }

            return new ListBucketsResponse
            {
                Buckets = buckets,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<GetBucketVersioningResponse> GetBucketVersioningAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new GetBucketVersioningRequest { BucketName = bucketName };
        return await GetBucketVersioningAsync(request, cancellationToken);
    }

    public async Task<GetBucketVersioningResponse> GetBucketVersioningAsync(GetBucketVersioningRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT versioning_status, mfa_delete_enabled, owner_id FROM buckets WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var versioningStatus = reader.IsDBNull(0) ? null : reader.GetString(0);
            var mfaDeleteEnabled = !reader.IsDBNull(1) && reader.GetInt64(1) == 1;
            var ownerId = reader.IsDBNull(2) ? "123456789012" : reader.GetString(2);

            // Validate ExpectedBucketOwner if specified
            if (!string.IsNullOrEmpty(request.ExpectedBucketOwner))
            {
                if (request.ExpectedBucketOwner != ownerId)
                {
                    throw new AmazonS3Exception("Access Denied")
                    {
                        StatusCode = HttpStatusCode.Forbidden,
                        ErrorCode = "AccessDenied"
                    };
                }
            }

            VersionStatus? status = null;
            if (versioningStatus == "Enabled")
                status = VersionStatus.Enabled;
            else if (versioningStatus == "Suspended")
                status = VersionStatus.Suspended;
            // When versioningStatus is null/empty (never configured), status remains null (matching AWS S3 behavior)

            return new GetBucketVersioningResponse
            {
                VersioningConfig = new S3BucketVersioningConfig
                {
                    Status = status,
                    EnableMfaDelete = mfaDeleteEnabled
                },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<PutBucketVersioningResponse> PutBucketVersioningAsync(PutBucketVersioningRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check if bucket exists and get current status
            string? currentStatus;
            string ownerId;
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT versioning_status, owner_id FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                using var reader = checkCmd.ExecuteReader();
                if (!reader.Read())
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
                currentStatus = reader.IsDBNull(0) ? null : reader.GetString(0);
                ownerId = reader.IsDBNull(1) ? "123456789012" : reader.GetString(1);
            }

            // Validate ExpectedBucketOwner if specified
            if (!string.IsNullOrEmpty(request.ExpectedBucketOwner))
            {
                if (request.ExpectedBucketOwner != ownerId)
                {
                    throw new AmazonS3Exception("Access Denied")
                    {
                        StatusCode = HttpStatusCode.Forbidden,
                        ErrorCode = "AccessDenied"
                    };
                }
            }

            var newStatus = request.VersioningConfig?.Status?.Value;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"UPDATE buckets
                SET versioning_status = @versioningStatus,
                    mfa_delete_enabled = @mfaDeleteEnabled
                WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@versioningStatus", newStatus ?? "Off");
            cmd.Parameters.AddWithValue("@mfaDeleteEnabled", request.VersioningConfig?.EnableMfaDelete == true ? 1 : 0);
            cmd.ExecuteNonQuery();

            return new PutBucketVersioningResponse { HttpStatusCode = HttpStatusCode.OK };
        }, cancellationToken);
    }

    #endregion

    #region Object Operations

    public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Verify bucket exists and get versioning status
            string? versioningStatus;
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT versioning_status FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var result = checkCmd.ExecuteScalar();
                if (result == null)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
                versioningStatus = result == DBNull.Value ? null : result.ToString();
            }

            var objectId = $"object::{request.BucketName}::{request.Key}";
            var isVersioningEnabled = versioningStatus == "Enabled";
            var isVersioningSuspended = versioningStatus == "Suspended";
            string? versionId = null;

            // Check if object exists
            byte[]? existingContent = null;
            string? existingEtag = null;
            string? existingVersionId = null;
            string? existingContentType = null;
            string? existingLastModified = null;
            string? existingMetadata = null;

            using (var existingCmd = _connection.CreateCommand())
            {
                existingCmd.CommandText = "SELECT content, etag, version_id, content_type, last_modified, metadata FROM objects WHERE id = @id";
                existingCmd.Parameters.AddWithValue("@id", objectId);
                using var reader = existingCmd.ExecuteReader();
                if (reader.Read())
                {
                    existingContent = reader.IsDBNull(0) ? null : (byte[])reader["content"];
                    existingEtag = reader.IsDBNull(1) ? null : reader.GetString(1);
                    existingVersionId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    existingContentType = reader.IsDBNull(3) ? null : reader.GetString(3);
                    existingLastModified = reader.IsDBNull(4) ? null : reader.GetString(4);
                    existingMetadata = reader.IsDBNull(5) ? null : reader.GetString(5);
                }
            }

            // Check conditional headers
            var ifNoneMatch = request.Headers?["If-None-Match"] ?? request.Headers?["x-amz-if-none-match"];
            var ifMatch = request.Headers?["If-Match"] ?? request.Headers?["x-amz-if-match"];

            if (ifNoneMatch == "*" && existingEtag != null)
            {
                throw new AmazonS3Exception("At least one of the preconditions you specified did not hold")
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    ErrorCode = "PreconditionFailed"
                };
            }

            if (!string.IsNullOrEmpty(ifMatch))
            {
                if (existingEtag == null)
                {
                    throw new AmazonS3Exception("At least one of the preconditions you specified did not hold")
                    {
                        StatusCode = HttpStatusCode.PreconditionFailed,
                        ErrorCode = "PreconditionFailed"
                    };
                }
                var normalizedIfMatch = ifMatch.Trim('"');
                var normalizedExistingETag = existingEtag?.Trim('"');
                if (normalizedExistingETag != normalizedIfMatch)
                {
                    throw new AmazonS3Exception("At least one of the preconditions you specified did not hold")
                    {
                        StatusCode = HttpStatusCode.PreconditionFailed,
                        ErrorCode = "PreconditionFailed"
                    };
                }
            }

            // Archive existing object if versioning is enabled
            if (isVersioningEnabled && existingEtag != null)
            {
                var archiveVersionId = existingVersionId ?? GenerateVersionId();
                ArchiveVersion(request.BucketName, request.Key, archiveVersionId, existingContent,
                    existingContentType, existingEtag, existingLastModified, existingMetadata);
                versionId = GenerateVersionId();
            }
            else if (isVersioningSuspended)
            {
                // Archive existing versioned object if it has a real version ID
                if (existingVersionId != null && existingVersionId != "null" && existingEtag != null)
                {
                    ArchiveVersion(request.BucketName, request.Key, existingVersionId, existingContent,
                        existingContentType, existingEtag, existingLastModified, existingMetadata);
                }
                versionId = "null";
            }
            else if (isVersioningEnabled)
            {
                versionId = GenerateVersionId();
            }

            // Prepare content
            byte[] content;
            if (request.InputStream != null)
            {
                using var ms = new MemoryStream();
                request.InputStream.CopyTo(ms);
                content = ms.ToArray();
            }
            else if (!string.IsNullOrEmpty(request.ContentBody))
            {
                content = Encoding.UTF8.GetBytes(request.ContentBody);
            }
            else
            {
                content = Array.Empty<byte>();
            }

            // Calculate ETag
            string etag;
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(content);
                etag = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            // Serialize metadata
            string? metadata = null;
            if (request.Metadata != null && request.Metadata.Count > 0)
            {
                var metadataDict = new Dictionary<string, string>();
                foreach (var key in request.Metadata.Keys)
                {
                    metadataDict[key] = request.Metadata[key];
                }
                metadata = System.Text.Json.JsonSerializer.Serialize(metadataDict);
            }

            // Insert or update object
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO objects (id, bucket_name, key, version_id, content, content_type, size, etag, last_modified, is_latest, metadata)
                VALUES (@id, @bucketName, @key, @versionId, @content, @contentType, @size, @etag, @lastModified, 1, @metadata)";
            cmd.Parameters.AddWithValue("@id", objectId);
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            cmd.Parameters.AddWithValue("@versionId", (object?)versionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@contentType", request.ContentType ?? "application/octet-stream");
            cmd.Parameters.AddWithValue("@size", content.Length);
            cmd.Parameters.AddWithValue("@etag", etag);
            cmd.Parameters.AddWithValue("@lastModified", DateTimeOffset.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@metadata", (object?)metadata ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            return new PutObjectResponse
            {
                ETag = etag,
                VersionId = versionId,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    private void ArchiveVersion(string bucketName, string key, string versionId, byte[]? content,
        string? contentType, string? etag, string? lastModified, string? metadata)
    {
        var versionDocId = $"version::{bucketName}::{key}::{versionId}";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO versions (id, bucket_name, key, version_id, content, content_type, size, etag, last_modified, is_latest, is_delete_marker, metadata)
            VALUES (@id, @bucketName, @key, @versionId, @content, @contentType, @size, @etag, @lastModified, 0, 0, @metadata)";
        cmd.Parameters.AddWithValue("@id", versionDocId);
        cmd.Parameters.AddWithValue("@bucketName", bucketName);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@versionId", versionId);
        cmd.Parameters.AddWithValue("@content", (object?)content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@contentType", contentType ?? "application/octet-stream");
        cmd.Parameters.AddWithValue("@size", content?.Length ?? 0);
        cmd.Parameters.AddWithValue("@etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastModified", lastModified ?? DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@metadata", (object?)metadata ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public async Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest { BucketName = bucketName, Key = key };
        return await GetObjectAsync(request, cancellationToken);
    }

    public async Task<GetObjectResponse> GetObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest { BucketName = bucketName, Key = key, VersionId = versionId };
        return await GetObjectAsync(request, cancellationToken);
    }

    public async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check if bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Handle specific version request
            if (!string.IsNullOrEmpty(request.VersionId))
            {
                if (request.VersionId == "null")
                {
                    // Get null version from objects table
                    return GetObjectFromTable("objects", request.BucketName, request.Key, null);
                }

                // Check for delete marker
                using (var dmCmd = _connection.CreateCommand())
                {
                    dmCmd.CommandText = "SELECT COUNT(*) FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    dmCmd.Parameters.AddWithValue("@key", request.Key);
                    dmCmd.Parameters.AddWithValue("@versionId", request.VersionId);
                    var dmCount = Convert.ToInt64(dmCmd.ExecuteScalar());
                    if (dmCount > 0)
                    {
                        throw new AmazonS3Exception("A Delete Marker cannot be retrieved using GET")
                        {
                            StatusCode = HttpStatusCode.MethodNotAllowed,
                            ErrorCode = "MethodNotAllowed"
                        };
                    }
                }

                // Check archived versions
                try
                {
                    return GetVersionFromTable(request.BucketName, request.Key, request.VersionId);
                }
                catch (AmazonS3Exception)
                {
                    // Not found in versions, check current object
                }

                // Check if version matches current object
                using (var currentCmd = _connection.CreateCommand())
                {
                    currentCmd.CommandText = "SELECT version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
                    currentCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    currentCmd.Parameters.AddWithValue("@key", request.Key);
                    var currentVersionId = currentCmd.ExecuteScalar();
                    if (currentVersionId != null && currentVersionId != DBNull.Value && currentVersionId.ToString() == request.VersionId)
                    {
                        return GetObjectFromTable("objects", request.BucketName, request.Key, request.VersionId);
                    }
                }

                throw new AmazonS3Exception("The specified version does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchVersion"
                };
            }

            // Check for delete marker
            using (var dmCmd = _connection.CreateCommand())
            {
                dmCmd.CommandText = "SELECT COUNT(*) FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND is_latest = 1";
                dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                dmCmd.Parameters.AddWithValue("@key", request.Key);
                var dmCount = Convert.ToInt64(dmCmd.ExecuteScalar());
                if (dmCount > 0)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Get current object
            return GetObjectFromTable("objects", request.BucketName, request.Key, null);
        }, cancellationToken);
    }

    private GetObjectResponse GetObjectFromTable(string table, string bucketName, string key, string? expectedVersionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT content, content_type, size, etag, last_modified, version_id, metadata FROM {table} WHERE bucket_name = @bucketName AND key = @key";
        cmd.Parameters.AddWithValue("@bucketName", bucketName);
        cmd.Parameters.AddWithValue("@key", key);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            throw new AmazonS3Exception("Object does not exist")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchKey"
            };
        }

        var content = reader.IsDBNull(0) ? Array.Empty<byte>() : (byte[])reader["content"];
        var contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
        var size = reader.GetInt64(2);
        var etag = reader.IsDBNull(3) ? null : reader.GetString(3);
        var lastModified = DateTime.Parse(reader.GetString(4));
        var versionId = reader.IsDBNull(5) ? null : reader.GetString(5);
        var metadataJson = reader.IsDBNull(6) ? null : reader.GetString(6);

        var response = new GetObjectResponse
        {
            BucketName = bucketName,
            Key = key,
            ContentLength = size,
            ETag = etag,
            LastModified = lastModified,
            VersionId = versionId ?? (table == "objects" ? "null" : versionId),
            HttpStatusCode = HttpStatusCode.OK,
            ResponseStream = new MemoryStream(content)
        };

        response.Headers.ContentType = contentType;

        if (!string.IsNullOrEmpty(metadataJson))
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    response.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return response;
    }

    private GetObjectResponse GetVersionFromTable(string bucketName, string key, string versionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT content, content_type, size, etag, last_modified, metadata FROM versions WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
        cmd.Parameters.AddWithValue("@bucketName", bucketName);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@versionId", versionId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            throw new AmazonS3Exception("The specified version does not exist")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchVersion"
            };
        }

        var content = reader.IsDBNull(0) ? Array.Empty<byte>() : (byte[])reader["content"];
        var contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
        var size = reader.GetInt64(2);
        var etag = reader.IsDBNull(3) ? null : reader.GetString(3);
        var lastModified = DateTime.Parse(reader.GetString(4));
        var metadataJson = reader.IsDBNull(5) ? null : reader.GetString(5);

        var response = new GetObjectResponse
        {
            BucketName = bucketName,
            Key = key,
            ContentLength = size,
            ETag = etag,
            LastModified = lastModified,
            VersionId = versionId,
            HttpStatusCode = HttpStatusCode.OK,
            ResponseStream = new MemoryStream(content)
        };

        response.Headers.ContentType = contentType;

        if (!string.IsNullOrEmpty(metadataJson))
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    response.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return response;
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest { BucketName = bucketName, Key = key };
        return await DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest { BucketName = bucketName, Key = key, VersionId = versionId };
        return await DeleteObjectAsync(request, cancellationToken);
    }

    public async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Get bucket info
            string? versioningStatus;
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT versioning_status FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var result = checkCmd.ExecuteScalar();
                if (result == null)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
                versioningStatus = result == DBNull.Value ? null : result.ToString();
            }

            var isVersioningEnabled = versioningStatus == "Enabled";
            var isVersioningSuspended = versioningStatus == "Suspended";

            // If specific version is provided, delete that version
            if (!string.IsNullOrEmpty(request.VersionId))
            {
                // Check delete markers
                using (var dmCmd = _connection.CreateCommand())
                {
                    dmCmd.CommandText = "DELETE FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    dmCmd.Parameters.AddWithValue("@key", request.Key);
                    dmCmd.Parameters.AddWithValue("@versionId", request.VersionId);
                    if (dmCmd.ExecuteNonQuery() > 0)
                    {
                        return new DeleteObjectResponse
                        {
                            DeleteMarker = "true",
                            VersionId = request.VersionId,
                            HttpStatusCode = HttpStatusCode.NoContent
                        };
                    }
                }

                // Check versions
                using (var vCmd = _connection.CreateCommand())
                {
                    vCmd.CommandText = "DELETE FROM versions WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    vCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    vCmd.Parameters.AddWithValue("@key", request.Key);
                    vCmd.Parameters.AddWithValue("@versionId", request.VersionId);
                    if (vCmd.ExecuteNonQuery() > 0)
                    {
                        return new DeleteObjectResponse
                        {
                            VersionId = request.VersionId,
                            HttpStatusCode = HttpStatusCode.NoContent
                        };
                    }
                }

                // Check current object
                using (var oCmd = _connection.CreateCommand())
                {
                    oCmd.CommandText = "SELECT version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
                    oCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    oCmd.Parameters.AddWithValue("@key", request.Key);
                    var currentVersionId = oCmd.ExecuteScalar();
                    if (currentVersionId != null && currentVersionId != DBNull.Value && currentVersionId.ToString() == request.VersionId)
                    {
                        using var delCmd = _connection.CreateCommand();
                        delCmd.CommandText = "DELETE FROM objects WHERE bucket_name = @bucketName AND key = @key";
                        delCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                        delCmd.Parameters.AddWithValue("@key", request.Key);
                        delCmd.ExecuteNonQuery();
                        return new DeleteObjectResponse
                        {
                            VersionId = request.VersionId,
                            HttpStatusCode = HttpStatusCode.NoContent
                        };
                    }
                }

                return new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent };
            }

            // Get existing object
            var objectId = $"object::{request.BucketName}::{request.Key}";
            byte[]? existingContent = null;
            string? existingEtag = null;
            string? existingVersionId = null;
            string? existingContentType = null;
            string? existingLastModified = null;
            string? existingMetadata = null;

            using (var existingCmd = _connection.CreateCommand())
            {
                existingCmd.CommandText = "SELECT content, etag, version_id, content_type, last_modified, metadata FROM objects WHERE id = @id";
                existingCmd.Parameters.AddWithValue("@id", objectId);
                using var reader = existingCmd.ExecuteReader();
                if (reader.Read())
                {
                    existingContent = reader.IsDBNull(0) ? null : (byte[])reader["content"];
                    existingEtag = reader.IsDBNull(1) ? null : reader.GetString(1);
                    existingVersionId = reader.IsDBNull(2) ? null : reader.GetString(2);
                    existingContentType = reader.IsDBNull(3) ? null : reader.GetString(3);
                    existingLastModified = reader.IsDBNull(4) ? null : reader.GetString(4);
                    existingMetadata = reader.IsDBNull(5) ? null : reader.GetString(5);
                }
            }

            if (isVersioningEnabled)
            {
                // Archive existing object if exists
                if (existingEtag != null)
                {
                    var archiveVersionId = existingVersionId ?? GenerateVersionId();
                    ArchiveVersion(request.BucketName, request.Key, archiveVersionId, existingContent,
                        existingContentType, existingEtag, existingLastModified, existingMetadata);

                    // Delete current object
                    using var delCmd = _connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                    delCmd.Parameters.AddWithValue("@id", objectId);
                    delCmd.ExecuteNonQuery();
                }

                // Create delete marker
                var deleteMarkerVersionId = GenerateVersionId();
                CreateDeleteMarker(request.BucketName, request.Key, deleteMarkerVersionId, true);

                return new DeleteObjectResponse
                {
                    DeleteMarker = "true",
                    VersionId = deleteMarkerVersionId,
                    HttpStatusCode = HttpStatusCode.NoContent
                };
            }
            else if (isVersioningSuspended)
            {
                // Archive existing versioned object if it has a real version ID
                if (existingVersionId != null && existingVersionId != "null" && existingEtag != null)
                {
                    ArchiveVersion(request.BucketName, request.Key, existingVersionId, existingContent,
                        existingContentType, existingEtag, existingLastModified, existingMetadata);
                }

                // Delete current object
                using (var delCmd = _connection.CreateCommand())
                {
                    delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                    delCmd.Parameters.AddWithValue("@id", objectId);
                    delCmd.ExecuteNonQuery();
                }

                // Delete existing null delete marker
                using (var delDmCmd = _connection.CreateCommand())
                {
                    delDmCmd.CommandText = "DELETE FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = 'null'";
                    delDmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    delDmCmd.Parameters.AddWithValue("@key", request.Key);
                    delDmCmd.ExecuteNonQuery();
                }

                // Create null delete marker
                CreateDeleteMarker(request.BucketName, request.Key, "null", true);

                return new DeleteObjectResponse
                {
                    DeleteMarker = "true",
                    VersionId = "null",
                    HttpStatusCode = HttpStatusCode.NoContent
                };
            }
            else
            {
                // Simple delete
                using var delCmd = _connection.CreateCommand();
                delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                delCmd.Parameters.AddWithValue("@id", objectId);
                delCmd.ExecuteNonQuery();

                return new DeleteObjectResponse { HttpStatusCode = HttpStatusCode.NoContent };
            }
        }, cancellationToken);
    }

    private void CreateDeleteMarker(string bucketName, string key, string versionId, bool isLatest)
    {
        var dmId = $"deletemarker::{bucketName}::{key}::{versionId}";
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO delete_markers (id, bucket_name, key, version_id, last_modified, is_latest)
            VALUES (@id, @bucketName, @key, @versionId, @lastModified, @isLatest)";
        cmd.Parameters.AddWithValue("@id", dmId);
        cmd.Parameters.AddWithValue("@bucketName", bucketName);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@versionId", versionId);
        cmd.Parameters.AddWithValue("@lastModified", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@isLatest", isLatest ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public async Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            if (request.Objects?.Count > 1000)
            {
                throw new AmazonS3Exception("The maximum number of objects that can be deleted in a single request is 1000")
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ErrorCode = "MaxKeysExceeded"
                };
            }

            // Get bucket info
            string? versioningStatus;
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT versioning_status FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var result = checkCmd.ExecuteScalar();
                if (result == null)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
                versioningStatus = result == DBNull.Value ? null : result.ToString();
            }

            var isVersioningEnabled = versioningStatus == "Enabled";
            var isVersioningSuspended = versioningStatus == "Suspended";

            var deletedObjects = new List<DeletedObject>();
            var errors = new List<DeleteError>();

            foreach (var keyVersion in request.Objects ?? new List<KeyVersion>())
            {
                try
                {
                    var objectId = $"object::{request.BucketName}::{keyVersion.Key}";

                    if (!string.IsNullOrEmpty(keyVersion.VersionId))
                    {
                        // Delete specific version
                        // Check delete markers
                        using (var dmCmd = _connection.CreateCommand())
                        {
                            dmCmd.CommandText = "DELETE FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                            dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                            dmCmd.Parameters.AddWithValue("@key", keyVersion.Key);
                            dmCmd.Parameters.AddWithValue("@versionId", keyVersion.VersionId);
                            if (dmCmd.ExecuteNonQuery() > 0)
                            {
                                deletedObjects.Add(new DeletedObject
                                {
                                    Key = keyVersion.Key,
                                    VersionId = keyVersion.VersionId,
                                    DeleteMarker = true,
                                    DeleteMarkerVersionId = keyVersion.VersionId
                                });
                                continue;
                            }
                        }

                        // Check versions
                        using (var vCmd = _connection.CreateCommand())
                        {
                            vCmd.CommandText = "DELETE FROM versions WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                            vCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                            vCmd.Parameters.AddWithValue("@key", keyVersion.Key);
                            vCmd.Parameters.AddWithValue("@versionId", keyVersion.VersionId);
                            if (vCmd.ExecuteNonQuery() > 0)
                            {
                                deletedObjects.Add(new DeletedObject
                                {
                                    Key = keyVersion.Key,
                                    VersionId = keyVersion.VersionId
                                });
                                continue;
                            }
                        }

                        // Check current object
                        using (var oCmd = _connection.CreateCommand())
                        {
                            oCmd.CommandText = "SELECT version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
                            oCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                            oCmd.Parameters.AddWithValue("@key", keyVersion.Key);
                            var currentVersionId = oCmd.ExecuteScalar();
                            if (currentVersionId != null && currentVersionId != DBNull.Value && currentVersionId.ToString() == keyVersion.VersionId)
                            {
                                using var delCmd = _connection.CreateCommand();
                                delCmd.CommandText = "DELETE FROM objects WHERE bucket_name = @bucketName AND key = @key";
                                delCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                                delCmd.Parameters.AddWithValue("@key", keyVersion.Key);
                                delCmd.ExecuteNonQuery();
                                deletedObjects.Add(new DeletedObject
                                {
                                    Key = keyVersion.Key,
                                    VersionId = keyVersion.VersionId
                                });
                                continue;
                            }
                        }

                        // Version not found - S3 still returns success
                        deletedObjects.Add(new DeletedObject
                        {
                            Key = keyVersion.Key,
                            VersionId = keyVersion.VersionId
                        });
                        continue;
                    }

                    // No version ID specified
                    // Get existing object
                    byte[]? existingContent = null;
                    string? existingEtag = null;
                    string? existingVersionId = null;
                    string? existingContentType = null;
                    string? existingLastModified = null;
                    string? existingMetadata = null;

                    using (var existingCmd = _connection.CreateCommand())
                    {
                        existingCmd.CommandText = "SELECT content, etag, version_id, content_type, last_modified, metadata FROM objects WHERE id = @id";
                        existingCmd.Parameters.AddWithValue("@id", objectId);
                        using var reader = existingCmd.ExecuteReader();
                        if (reader.Read())
                        {
                            existingContent = reader.IsDBNull(0) ? null : (byte[])reader["content"];
                            existingEtag = reader.IsDBNull(1) ? null : reader.GetString(1);
                            existingVersionId = reader.IsDBNull(2) ? null : reader.GetString(2);
                            existingContentType = reader.IsDBNull(3) ? null : reader.GetString(3);
                            existingLastModified = reader.IsDBNull(4) ? null : reader.GetString(4);
                            existingMetadata = reader.IsDBNull(5) ? null : reader.GetString(5);
                        }
                    }

                    if (isVersioningEnabled)
                    {
                        // Archive existing object if exists
                        if (existingEtag != null)
                        {
                            var archiveVersionId = existingVersionId ?? GenerateVersionId();
                            ArchiveVersion(request.BucketName, keyVersion.Key, archiveVersionId, existingContent,
                                existingContentType, existingEtag, existingLastModified, existingMetadata);

                            using var delCmd = _connection.CreateCommand();
                            delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                            delCmd.Parameters.AddWithValue("@id", objectId);
                            delCmd.ExecuteNonQuery();
                        }

                        // Create delete marker
                        var deleteMarkerVersionId = GenerateVersionId();
                        CreateDeleteMarker(request.BucketName, keyVersion.Key, deleteMarkerVersionId, true);

                        deletedObjects.Add(new DeletedObject
                        {
                            Key = keyVersion.Key,
                            DeleteMarker = true,
                            DeleteMarkerVersionId = deleteMarkerVersionId
                        });
                    }
                    else if (isVersioningSuspended)
                    {
                        // Archive existing versioned object
                        if (existingVersionId != null && existingVersionId != "null" && existingEtag != null)
                        {
                            ArchiveVersion(request.BucketName, keyVersion.Key, existingVersionId, existingContent,
                                existingContentType, existingEtag, existingLastModified, existingMetadata);
                        }

                        using (var delCmd = _connection.CreateCommand())
                        {
                            delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                            delCmd.Parameters.AddWithValue("@id", objectId);
                            delCmd.ExecuteNonQuery();
                        }

                        // Delete existing null delete marker
                        using (var delDmCmd = _connection.CreateCommand())
                        {
                            delDmCmd.CommandText = "DELETE FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = 'null'";
                            delDmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                            delDmCmd.Parameters.AddWithValue("@key", keyVersion.Key);
                            delDmCmd.ExecuteNonQuery();
                        }

                        CreateDeleteMarker(request.BucketName, keyVersion.Key, "null", true);

                        deletedObjects.Add(new DeletedObject
                        {
                            Key = keyVersion.Key,
                            DeleteMarker = true,
                            DeleteMarkerVersionId = "null"
                        });
                    }
                    else
                    {
                        // Simple delete
                        using (var delCmd = _connection.CreateCommand())
                        {
                            delCmd.CommandText = "DELETE FROM objects WHERE id = @id";
                            delCmd.Parameters.AddWithValue("@id", objectId);
                            delCmd.ExecuteNonQuery();
                        }

                        deletedObjects.Add(new DeletedObject { Key = keyVersion.Key });
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new DeleteError
                    {
                        Key = keyVersion.Key,
                        VersionId = keyVersion.VersionId,
                        Code = "InternalError",
                        Message = ex.Message
                    });
                }
            }

            if (request.Quiet)
            {
                deletedObjects.Clear();
            }

            return new DeleteObjectsResponse
            {
                DeletedObjects = deletedObjects,
                DeleteErrors = errors,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<ListVersionsResponse> ListVersionsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest { BucketName = bucketName };
        return await ListVersionsAsync(request, cancellationToken);
    }

    public async Task<ListVersionsResponse> ListVersionsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListVersionsRequest { BucketName = bucketName, Prefix = prefix };
        return await ListVersionsAsync(request, cancellationToken);
    }

    public async Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            var versions = new List<S3ObjectVersion>();

            // Get current objects
            using (var objCmd = _connection.CreateCommand())
            {
                objCmd.CommandText = "SELECT key, version_id, last_modified, etag, size FROM objects WHERE bucket_name = @bucketName";
                objCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                using var reader = objCmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                        continue;

                    versions.Add(new S3ObjectVersion
                    {
                        Key = key,
                        VersionId = reader.IsDBNull(1) ? "null" : reader.GetString(1),
                        IsLatest = true,
                        LastModified = DateTime.Parse(reader.GetString(2)),
                        ETag = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Size = reader.GetInt64(4),
                        StorageClass = S3StorageClass.Standard
                    });
                }
            }

            // Get archived versions
            using (var vCmd = _connection.CreateCommand())
            {
                vCmd.CommandText = "SELECT key, version_id, last_modified, etag, size FROM versions WHERE bucket_name = @bucketName";
                vCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                using var reader = vCmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                        continue;

                    versions.Add(new S3ObjectVersion
                    {
                        Key = key,
                        VersionId = reader.GetString(1),
                        IsLatest = false,
                        LastModified = DateTime.Parse(reader.GetString(2)),
                        ETag = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Size = reader.GetInt64(4),
                        StorageClass = S3StorageClass.Standard
                    });
                }
            }

            // Get delete markers
            using (var dmCmd = _connection.CreateCommand())
            {
                dmCmd.CommandText = "SELECT key, version_id, last_modified, is_latest FROM delete_markers WHERE bucket_name = @bucketName";
                dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                using var reader = dmCmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                        continue;

                    versions.Add(new S3ObjectVersion
                    {
                        Key = key,
                        VersionId = reader.GetString(1),
                        IsLatest = reader.GetInt64(3) == 1,
                        LastModified = DateTime.Parse(reader.GetString(2)),
                        IsDeleteMarker = true
                    });
                }
            }

            // Sort and limit
            versions = versions
                .OrderBy(v => v.Key)
                .ThenByDescending(v => v.LastModified)
                .ToList();

            var maxKeys = request.MaxKeys > 0 ? request.MaxKeys : 1000;
            var isTruncated = versions.Count > maxKeys;
            if (isTruncated)
            {
                versions = versions.Take(maxKeys).ToList();
            }

            return new ListVersionsResponse
            {
                Name = request.BucketName,
                Prefix = request.Prefix,
                Versions = versions,
                IsTruncated = isTruncated,
                MaxKeys = maxKeys,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    #endregion

    #region Helper Methods

    private static string GenerateVersionId()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }

    #endregion

    #region Dispose

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
                _connection?.Close();
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion

    #region Not Implemented Methods

    public Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

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

    public async Task<CopyObjectResponse> CopyObjectAsync(string sourceBucket, string sourceKey, string sourceVersionId, string destinationBucket, string destinationKey, CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,
            SourceVersionId = sourceVersionId,
            DestinationBucket = destinationBucket,
            DestinationKey = destinationKey
        };
        return await CopyObjectAsync(request, cancellationToken);
    }

    public async Task<CopyObjectResponse> CopyObjectAsync(CopyObjectRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            // Check if source bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Source bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Check if destination bucket exists
            string? destVersioningStatus;
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT versioning_status FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.DestinationBucket);
                var result = checkCmd.ExecuteScalar();
                if (result == null)
                {
                    throw new AmazonS3Exception("Destination bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
                destVersioningStatus = result == DBNull.Value ? null : result.ToString();
            }

            // Check for delete marker at source if no version specified
            if (string.IsNullOrEmpty(request.SourceVersionId))
            {
                using var dmCmd = _connection.CreateCommand();
                dmCmd.CommandText = "SELECT COUNT(*) FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND is_latest = 1";
                dmCmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                dmCmd.Parameters.AddWithValue("@key", request.SourceKey);
                var dmCount = Convert.ToInt64(dmCmd.ExecuteScalar());
                if (dmCount > 0)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Get source object
            byte[]? content = null;
            string? contentType = null;
            string? etag = null;
            string? metadata = null;

            if (!string.IsNullOrEmpty(request.SourceVersionId))
            {
                // Check for delete marker
                using (var dmCmd = _connection.CreateCommand())
                {
                    dmCmd.CommandText = "SELECT COUNT(*) FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    dmCmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                    dmCmd.Parameters.AddWithValue("@key", request.SourceKey);
                    dmCmd.Parameters.AddWithValue("@versionId", request.SourceVersionId);
                    var dmCount = Convert.ToInt64(dmCmd.ExecuteScalar());
                    if (dmCount > 0)
                    {
                        throw new AmazonS3Exception("Cannot copy a delete marker")
                        {
                            StatusCode = HttpStatusCode.BadRequest,
                            ErrorCode = "InvalidRequest"
                        };
                    }
                }

                // Try to get from versions
                using var vCmd = _connection.CreateCommand();
                vCmd.CommandText = "SELECT content, content_type, etag, metadata FROM versions WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                vCmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                vCmd.Parameters.AddWithValue("@key", request.SourceKey);
                vCmd.Parameters.AddWithValue("@versionId", request.SourceVersionId);
                using var vReader = vCmd.ExecuteReader();
                if (vReader.Read())
                {
                    content = vReader.IsDBNull(0) ? null : (byte[])vReader["content"];
                    contentType = vReader.IsDBNull(1) ? "application/octet-stream" : vReader.GetString(1);
                    etag = vReader.IsDBNull(2) ? null : vReader.GetString(2);
                    metadata = vReader.IsDBNull(3) ? null : vReader.GetString(3);
                }
                else
                {
                    vReader.Close();
                    // Check current object
                    using var oCmd = _connection.CreateCommand();
                    oCmd.CommandText = "SELECT content, content_type, etag, metadata, version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
                    oCmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                    oCmd.Parameters.AddWithValue("@key", request.SourceKey);
                    using var oReader = oCmd.ExecuteReader();
                    if (oReader.Read())
                    {
                        var currentVersionId = oReader.IsDBNull(4) ? null : oReader.GetString(4);
                        if (currentVersionId == request.SourceVersionId)
                        {
                            content = oReader.IsDBNull(0) ? null : (byte[])oReader["content"];
                            contentType = oReader.IsDBNull(1) ? "application/octet-stream" : oReader.GetString(1);
                            etag = oReader.IsDBNull(2) ? null : oReader.GetString(2);
                            metadata = oReader.IsDBNull(3) ? null : oReader.GetString(3);
                        }
                        else
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
                        throw new AmazonS3Exception("The specified version does not exist")
                        {
                            StatusCode = HttpStatusCode.NotFound,
                            ErrorCode = "NoSuchVersion"
                        };
                    }
                }
            }
            else
            {
                // Get current object
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT content, content_type, etag, metadata FROM objects WHERE bucket_name = @bucketName AND key = @key";
                cmd.Parameters.AddWithValue("@bucketName", request.SourceBucket);
                cmd.Parameters.AddWithValue("@key", request.SourceKey);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
                content = reader.IsDBNull(0) ? null : (byte[])reader["content"];
                contentType = reader.IsDBNull(1) ? "application/octet-stream" : reader.GetString(1);
                etag = reader.IsDBNull(2) ? null : reader.GetString(2);
                metadata = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            // Put the object to the destination
            var putRequest = new PutObjectRequest
            {
                BucketName = request.DestinationBucket,
                Key = request.DestinationKey,
                ContentType = contentType,
                InputStream = content != null ? new MemoryStream(content) : new MemoryStream()
            };

            if (!string.IsNullOrEmpty(metadata))
            {
                var metadataDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);
                if (metadataDict != null)
                {
                    foreach (var kvp in metadataDict)
                    {
                        putRequest.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }

            var putResponse = await PutObjectAsync(putRequest, cancellationToken);

            return new CopyObjectResponse
            {
                ETag = putResponse.ETag,
                VersionId = putResponse.VersionId,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CopyPartResponse> CopyPartAsync(string sourceBucket, string sourceKey, string sourceVersionId, string destinationBucket, string destinationKey, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CopyPartResponse> CopyPartAsync(CopyPartRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<CreateSessionResponse> CreateSessionAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task DeleteAsync(string bucketName, string objectKey, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
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

    public Task DeletesAsync(string bucketName, IEnumerable<string> objectKeys, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Endpoint DetermineServiceOperationEndpoint(AmazonWebServiceRequest request)
        => throw new NotImplementedException();

    public Task<bool> DoesS3BucketExistAsync(string bucketName)
        => throw new NotImplementedException();

    public Task DownloadToFilePathAsync(string bucketName, string objectKey, string filepath, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task EnsureBucketExistsAsync(string bucketName)
        => throw new NotImplementedException();

    public string GeneratePreSignedURL(string bucketName, string objectKey, DateTime expiration, IDictionary<string, object> additionalProperties)
        => throw new NotImplementedException();

    public Task<GetACLResponse> GetACLAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetACLResponse> GetACLAsync(GetACLRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IList<string>> GetAllObjectKeysAsync(string bucketName, string prefix, IDictionary<string, object> additionalProperties)
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

    public Task<GetObjectAttributesResponse> GetObjectAttributesAsync(GetObjectAttributesRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public async Task<GetObjectLegalHoldResponse> GetObjectLegalHoldAsync(GetObjectLegalHoldRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Get object legal hold status
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT legal_hold_status FROM objects WHERE bucket_name = @bucketName AND key = @key";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            var result = cmd.ExecuteScalar();
            if (result == null)
            {
                throw new AmazonS3Exception("Object does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                };
            }

            var status = result == DBNull.Value ? "OFF" : result.ToString();

            return new GetObjectLegalHoldResponse
            {
                LegalHold = new ObjectLockLegalHold
                {
                    Status = status == "ON" ? ObjectLockLegalHoldStatus.On : ObjectLockLegalHoldStatus.Off
                },
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<GetObjectLockConfigurationResponse> GetObjectLockConfigurationAsync(GetObjectLockConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT object_lock_enabled, object_lock_mode, object_lock_days, object_lock_years FROM buckets WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            var objectLockEnabled = !reader.IsDBNull(0) && reader.GetInt64(0) == 1;
            if (!objectLockEnabled)
            {
                throw new AmazonS3Exception("Object Lock configuration does not exist for this bucket")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "ObjectLockConfigurationNotFoundError"
                };
            }

            var mode = reader.IsDBNull(1) ? null : reader.GetString(1);
            var days = reader.IsDBNull(2) ? (int?)null : (int)reader.GetInt64(2);
            var years = reader.IsDBNull(3) ? (int?)null : (int)reader.GetInt64(3);

            var config = new ObjectLockConfiguration
            {
                ObjectLockEnabled = ObjectLockEnabled.Enabled
            };

            if (!string.IsNullOrEmpty(mode))
            {
                config.Rule = new ObjectLockRule
                {
                    DefaultRetention = new DefaultRetention
                    {
                        Mode = mode == "GOVERNANCE" ? ObjectLockRetentionMode.Governance : ObjectLockRetentionMode.Compliance,
                        Days = days ?? 0,
                        Years = years ?? 0
                    }
                };
            }

            return new GetObjectLockConfigurationResponse
            {
                ObjectLockConfiguration = config,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest { BucketName = bucketName, Key = key };
        return await GetObjectMetadataAsync(request, cancellationToken);
    }

    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest { BucketName = bucketName, Key = key, VersionId = versionId };
        return await GetObjectMetadataAsync(request, cancellationToken);
    }

    public async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Handle specific version request
            if (!string.IsNullOrEmpty(request.VersionId))
            {
                // Check for delete marker
                using (var dmCmd = _connection.CreateCommand())
                {
                    dmCmd.CommandText = "SELECT last_modified FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    dmCmd.Parameters.AddWithValue("@key", request.Key);
                    dmCmd.Parameters.AddWithValue("@versionId", request.VersionId);
                    using var dmReader = dmCmd.ExecuteReader();
                    if (dmReader.Read())
                    {
                        var lastModified = DateTime.Parse(dmReader.GetString(0));
                        var response = new GetObjectMetadataResponse
                        {
                            VersionId = request.VersionId,
                            DeleteMarker = "true",
                            LastModified = lastModified,
                            HttpStatusCode = HttpStatusCode.OK
                        };
                        return response;
                    }
                }

                // Check versions table
                using (var vCmd = _connection.CreateCommand())
                {
                    vCmd.CommandText = "SELECT content_type, size, etag, last_modified, metadata FROM versions WHERE bucket_name = @bucketName AND key = @key AND version_id = @versionId";
                    vCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    vCmd.Parameters.AddWithValue("@key", request.Key);
                    vCmd.Parameters.AddWithValue("@versionId", request.VersionId);
                    using var vReader = vCmd.ExecuteReader();
                    if (vReader.Read())
                    {
                        return BuildMetadataResponse(vReader, request.VersionId);
                    }
                }

                // Check current object
                using (var oCmd = _connection.CreateCommand())
                {
                    oCmd.CommandText = "SELECT content_type, size, etag, last_modified, metadata, version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
                    oCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                    oCmd.Parameters.AddWithValue("@key", request.Key);
                    using var oReader = oCmd.ExecuteReader();
                    if (oReader.Read())
                    {
                        var currentVersionId = oReader.IsDBNull(5) ? null : oReader.GetString(5);
                        if (currentVersionId == request.VersionId)
                        {
                            return BuildMetadataResponse(oReader, request.VersionId);
                        }
                    }
                }

                throw new AmazonS3Exception("The specified version does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchVersion"
                };
            }

            // Check for delete marker on current version
            using (var dmCmd = _connection.CreateCommand())
            {
                dmCmd.CommandText = "SELECT COUNT(*) FROM delete_markers WHERE bucket_name = @bucketName AND key = @key AND is_latest = 1";
                dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                dmCmd.Parameters.AddWithValue("@key", request.Key);
                var dmCount = Convert.ToInt64(dmCmd.ExecuteScalar());
                if (dmCount > 0)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Get current object
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT content_type, size, etag, last_modified, metadata, version_id FROM objects WHERE bucket_name = @bucketName AND key = @key";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new AmazonS3Exception("Object does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                };
            }

            var versionId = reader.IsDBNull(5) ? null : reader.GetString(5);
            return BuildMetadataResponse(reader, versionId);
        }, cancellationToken);
    }

    private GetObjectMetadataResponse BuildMetadataResponse(SqliteDataReader reader, string? versionId)
    {
        var contentType = reader.IsDBNull(0) ? "application/octet-stream" : reader.GetString(0);
        var size = reader.GetInt64(1);
        var etag = reader.IsDBNull(2) ? null : reader.GetString(2);
        var lastModified = DateTime.Parse(reader.GetString(3));
        var metadataJson = reader.IsDBNull(4) ? null : reader.GetString(4);

        var response = new GetObjectMetadataResponse
        {
            ContentLength = size,
            ETag = etag,
            LastModified = lastModified,
            VersionId = versionId,
            HttpStatusCode = HttpStatusCode.OK
        };

        response.Headers.ContentType = contentType;

        if (!string.IsNullOrEmpty(metadataJson))
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    response.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        return response;
    }

    public async Task<GetObjectRetentionResponse> GetObjectRetentionAsync(GetObjectRetentionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Get object retention
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT retention_mode, retention_until FROM objects WHERE bucket_name = @bucketName AND key = @key";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new AmazonS3Exception("Object does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchKey"
                };
            }

            var mode = reader.IsDBNull(0) ? null : reader.GetString(0);
            var until = reader.IsDBNull(1) ? null : reader.GetString(1);

            ObjectLockRetention? retention = null;
            if (!string.IsNullOrEmpty(mode) && !string.IsNullOrEmpty(until))
            {
                retention = new ObjectLockRetention
                {
                    Mode = mode == "GOVERNANCE" ? ObjectLockRetentionMode.Governance : ObjectLockRetentionMode.Compliance,
                    RetainUntilDate = DateTime.Parse(until)
                };
            }

            return new GetObjectRetentionResponse
            {
                Retention = retention,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<Stream> GetObjectStreamAsync(string bucketName, string objectKey, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTaggingResponse> GetObjectTaggingAsync(GetObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTorrentResponse> GetObjectTorrentAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<GetObjectTorrentResponse> GetObjectTorrentAsync(GetObjectTorrentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public string GetPreSignedURL(GetPreSignedUrlRequest request)
        => throw new NotImplementedException();

    public Task<string> GetPreSignedURLAsync(GetPreSignedUrlRequest request)
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

    public Task<ListDirectoryBucketsResponse> ListDirectoryBucketsAsync(ListDirectoryBucketsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListMultipartUploadsResponse> ListMultipartUploadsAsync(ListMultipartUploadsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public async Task<ListObjectsResponse> ListObjectsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsRequest { BucketName = bucketName };
        return await ListObjectsAsync(request, cancellationToken);
    }

    public async Task<ListObjectsResponse> ListObjectsAsync(string bucketName, string prefix, CancellationToken cancellationToken = default)
    {
        var request = new ListObjectsRequest { BucketName = bucketName, Prefix = prefix };
        return await ListObjectsAsync(request, cancellationToken);
    }

    public async Task<ListObjectsResponse> ListObjectsAsync(ListObjectsRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Get all keys that have delete markers as latest
            var deletedKeys = new HashSet<string>();
            using (var dmCmd = _connection.CreateCommand())
            {
                dmCmd.CommandText = "SELECT key FROM delete_markers WHERE bucket_name = @bucketName AND is_latest = 1";
                dmCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                using var dmReader = dmCmd.ExecuteReader();
                while (dmReader.Read())
                {
                    deletedKeys.Add(dmReader.GetString(0));
                }
            }

            // Get objects that are not deleted
            var objects = new List<S3Object>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT key, size, etag, last_modified FROM objects WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);

                // Skip if this key has a delete marker as latest
                if (deletedKeys.Contains(key))
                    continue;

                // Apply prefix filter
                if (!string.IsNullOrEmpty(request.Prefix) && !key.StartsWith(request.Prefix))
                    continue;

                objects.Add(new S3Object
                {
                    BucketName = request.BucketName,
                    Key = key,
                    Size = reader.GetInt64(1),
                    ETag = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastModified = DateTime.Parse(reader.GetString(3)),
                    StorageClass = S3StorageClass.Standard
                });
            }

            // Sort and limit
            objects = objects
                .OrderBy(o => o.Key)
                .ToList();

            var maxKeys = request.MaxKeys > 0 ? request.MaxKeys : 1000;
            var isTruncated = objects.Count > maxKeys;
            if (isTruncated)
            {
                objects = objects.Take(maxKeys).ToList();
            }

            return new ListObjectsResponse
            {
                Name = request.BucketName,
                Prefix = request.Prefix,
                S3Objects = objects,
                IsTruncated = isTruncated,
                MaxKeys = maxKeys,
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListPartsResponse> ListPartsAsync(string bucketName, string key, string uploadId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<ListPartsResponse> ListPartsAsync(ListPartsRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task MakeObjectPublicAsync(string bucketName, string objectKey, bool enable)
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

    public async Task<PutObjectLegalHoldResponse> PutObjectLegalHoldAsync(PutObjectLegalHoldRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Check object exists
            using (var objCmd = _connection.CreateCommand())
            {
                objCmd.CommandText = "SELECT COUNT(*) FROM objects WHERE bucket_name = @bucketName AND key = @key";
                objCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                objCmd.Parameters.AddWithValue("@key", request.Key);
                var count = Convert.ToInt64(objCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Update legal hold status
            var status = request.LegalHold?.Status?.Value == "ON" ? "ON" : "OFF";
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE objects SET legal_hold_status = @status WHERE bucket_name = @bucketName AND key = @key";
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            cmd.ExecuteNonQuery();

            return new PutObjectLegalHoldResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<PutObjectLockConfigurationResponse> PutObjectLockConfigurationAsync(PutObjectLockConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            var config = request.ObjectLockConfiguration;
            var enabled = config?.ObjectLockEnabled?.Value == "Enabled" ? 1 : 0;
            var mode = config?.Rule?.DefaultRetention?.Mode?.Value;
            var days = config?.Rule?.DefaultRetention?.Days;
            var years = config?.Rule?.DefaultRetention?.Years;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"UPDATE buckets SET
                object_lock_enabled = @enabled,
                object_lock_mode = @mode,
                object_lock_days = @days,
                object_lock_years = @years
                WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@enabled", enabled);
            cmd.Parameters.AddWithValue("@mode", (object?)mode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@days", days.HasValue && days.Value > 0 ? (object)days.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@years", years.HasValue && years.Value > 0 ? (object)years.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.ExecuteNonQuery();

            return new PutObjectLockConfigurationResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public async Task<PutObjectRetentionResponse> PutObjectRetentionAsync(PutObjectRetentionRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // Check bucket exists
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
                checkCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Bucket does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchBucket"
                    };
                }
            }

            // Check object exists
            using (var objCmd = _connection.CreateCommand())
            {
                objCmd.CommandText = "SELECT COUNT(*) FROM objects WHERE bucket_name = @bucketName AND key = @key";
                objCmd.Parameters.AddWithValue("@bucketName", request.BucketName);
                objCmd.Parameters.AddWithValue("@key", request.Key);
                var count = Convert.ToInt64(objCmd.ExecuteScalar());
                if (count == 0)
                {
                    throw new AmazonS3Exception("Object does not exist")
                    {
                        StatusCode = HttpStatusCode.NotFound,
                        ErrorCode = "NoSuchKey"
                    };
                }
            }

            // Update retention
            var mode = request.Retention?.Mode?.Value;
            var until = request.Retention != null && request.Retention.RetainUntilDate != DateTime.MinValue
                ? request.Retention.RetainUntilDate.ToString("o")
                : null;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE objects SET retention_mode = @mode, retention_until = @until WHERE bucket_name = @bucketName AND key = @key";
            cmd.Parameters.AddWithValue("@mode", (object?)mode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@until", (object?)until ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            cmd.Parameters.AddWithValue("@key", request.Key);
            cmd.ExecuteNonQuery();

            return new PutObjectRetentionResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    public Task<PutObjectTaggingResponse> PutObjectTaggingAsync(PutObjectTaggingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<PutPublicAccessBlockResponse> PutPublicAccessBlockAsync(PutPublicAccessBlockRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, int days, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, string versionId, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(string bucketName, string key, string versionId, int days, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<RestoreObjectResponse> RestoreObjectAsync(RestoreObjectRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SelectObjectContentResponse> SelectObjectContentAsync(SelectObjectContentRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task UploadObjectFromFilePathAsync(string bucketName, string objectKey, string filepath, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task UploadObjectFromStreamAsync(string bucketName, string objectKey, Stream stream, IDictionary<string, object> additionalProperties, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<WriteGetObjectResponseResponse> WriteGetObjectResponseAsync(WriteGetObjectResponseRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public async Task<HeadBucketResponse> HeadBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var request = new HeadBucketRequest { BucketName = bucketName };
        return await HeadBucketAsync(request, cancellationToken);
    }

    public async Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM buckets WHERE bucket_name = @bucketName";
            cmd.Parameters.AddWithValue("@bucketName", request.BucketName);
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            if (count == 0)
            {
                throw new AmazonS3Exception("Bucket does not exist")
                {
                    StatusCode = HttpStatusCode.NotFound,
                    ErrorCode = "NoSuchBucket"
                };
            }

            return new HeadBucketResponse
            {
                HttpStatusCode = HttpStatusCode.OK
            };
        }, cancellationToken);
    }

    #endregion
}
