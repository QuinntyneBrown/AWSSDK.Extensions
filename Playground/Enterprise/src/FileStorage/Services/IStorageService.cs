using FileStorage.Models;

namespace FileStorage.Services;

/// <summary>
/// Interface for file storage operations backed by CouchbaseS3Client.
/// </summary>
public interface IStorageService : IDisposable
{
    /// <summary>
    /// Saves a file to storage.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to store the file in.</param>
    /// <param name="key">The unique key/path for the file.</param>
    /// <param name="stream">The file content stream.</param>
    /// <param name="contentType">The MIME type of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved file metadata including version ID.</returns>
    Task<StoredFile> SaveFileAsync(string bucketName, string key, Stream stream, string? contentType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a file from storage.
    /// </summary>
    /// <param name="bucketName">The name of the bucket containing the file.</param>
    /// <param name="key">The unique key/path for the file.</param>
    /// <param name="versionId">Optional version ID to retrieve a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content and metadata.</returns>
    Task<StoredFileWithContent> GetFileAsync(string bucketName, string key, string? versionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all files in a bucket with optional prefix filter.
    /// </summary>
    /// <param name="bucketName">The name of the bucket to list files from.</param>
    /// <param name="prefix">Optional prefix to filter files by path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of file metadata.</returns>
    Task<IEnumerable<StoredFile>> GetFilesAsync(string bucketName, string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    /// <param name="bucketName">The name of the bucket containing the file.</param>
    /// <param name="key">The unique key/path for the file.</param>
    /// <param name="versionId">Optional version ID to delete a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the file was deleted, false if it didn't exist.</returns>
    Task<bool> DeleteFileAsync(string bucketName, string key, string? versionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a bucket exists, creating it if necessary.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables versioning on a bucket.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnableVersioningAsync(string bucketName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all versions of a file.
    /// </summary>
    /// <param name="bucketName">The name of the bucket containing the file.</param>
    /// <param name="key">The unique key/path for the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of file versions.</returns>
    Task<IEnumerable<FileVersion>> GetFileVersionsAsync(string bucketName, string key, CancellationToken cancellationToken = default);
}
