namespace FileStorage.Models;

/// <summary>
/// Represents metadata for a stored file.
/// </summary>
public class StoredFile
{
    /// <summary>
    /// The bucket name where the file is stored.
    /// </summary>
    public required string BucketName { get; set; }

    /// <summary>
    /// The unique key/path for the file.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// The version ID of the file.
    /// </summary>
    public string? VersionId { get; set; }

    /// <summary>
    /// The ETag (entity tag) for the file.
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// The MIME content type of the file.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// The last modified timestamp.
    /// </summary>
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Represents a stored file with its content.
/// </summary>
public class StoredFileWithContent : StoredFile
{
    /// <summary>
    /// The file content as a stream.
    /// </summary>
    public required Stream Content { get; set; }
}

/// <summary>
/// Represents a version of a file.
/// </summary>
public class FileVersion
{
    /// <summary>
    /// The version ID.
    /// </summary>
    public required string VersionId { get; set; }

    /// <summary>
    /// The key of the file.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Whether this version is the latest.
    /// </summary>
    public bool IsLatest { get; set; }

    /// <summary>
    /// The last modified timestamp.
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// The size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Whether this is a delete marker.
    /// </summary>
    public bool IsDeleteMarker { get; set; }
}
