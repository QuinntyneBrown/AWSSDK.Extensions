using FileStorage.Models;
using FileStorage.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Endpoints;

public static class FileStorageEndpoints
{
    public static void MapFileStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/files")
            .WithTags("File Storage");

        // Bucket management
        group.MapPost("/buckets/{bucketName}", CreateBucket)
            .WithName("CreateBucket")
            .WithDescription("Creates a new bucket");

        group.MapPost("/buckets/{bucketName}/versioning", EnableVersioning)
            .WithName("EnableVersioning")
            .WithDescription("Enables versioning on a bucket");

        // Versioning operations - must be defined before catch-all routes
        group.MapGet("/versions/{bucketName}/{*key}", GetFileVersions)
            .WithName("GetFileVersions")
            .WithDescription("Lists all versions of a file");

        // File operations
        group.MapPost("/{bucketName}/{*key}", UploadFile)
            .WithName("UploadFile")
            .WithDescription("Uploads a file to storage")
            .DisableAntiforgery();

        group.MapGet("/{bucketName}/{*key}", GetFile)
            .WithName("GetFile")
            .WithDescription("Downloads a file from storage");

        group.MapGet("/{bucketName}", ListFiles)
            .WithName("ListFiles")
            .WithDescription("Lists all files in a bucket");

        group.MapDelete("/{bucketName}/{*key}", DeleteFile)
            .WithName("DeleteFile")
            .WithDescription("Deletes a file from storage");
    }

    private static async Task<IResult> CreateBucket(
        string bucketName,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        await storageService.EnsureBucketExistsAsync(bucketName, cancellationToken);
        return Results.Created($"/api/files/buckets/{bucketName}", new { BucketName = bucketName });
    }

    private static async Task<IResult> EnableVersioning(
        string bucketName,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        await storageService.EnableVersioningAsync(bucketName, cancellationToken);
        return Results.Ok(new { BucketName = bucketName, Versioning = "Enabled" });
    }

    private static async Task<IResult> UploadFile(
        string bucketName,
        string key,
        HttpRequest request,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        var contentType = request.ContentType ?? "application/octet-stream";
        var result = await storageService.SaveFileAsync(
            bucketName,
            key,
            request.Body,
            contentType,
            cancellationToken);

        return Results.Created($"/api/files/{bucketName}/{key}", result);
    }

    private static async Task<IResult> GetFile(
        string bucketName,
        string key,
        [FromQuery] string? versionId,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await storageService.GetFileAsync(bucketName, key, versionId, cancellationToken);
            return Results.File(
                file.Content,
                file.ContentType ?? "application/octet-stream",
                key.Split('/').LastOrDefault() ?? key);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { Message = $"File '{key}' not found in bucket '{bucketName}'" });
        }
    }

    private static async Task<IResult> ListFiles(
        string bucketName,
        [FromQuery] string? prefix,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        var files = await storageService.GetFilesAsync(bucketName, prefix, cancellationToken);
        return Results.Ok(files);
    }

    private static async Task<IResult> DeleteFile(
        string bucketName,
        string key,
        [FromQuery] string? versionId,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        var deleted = await storageService.DeleteFileAsync(bucketName, key, versionId, cancellationToken);
        if (deleted)
        {
            return Results.NoContent();
        }
        return Results.NotFound(new { Message = $"File '{key}' not found in bucket '{bucketName}'" });
    }

    private static async Task<IResult> GetFileVersions(
        string bucketName,
        string key,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        var versions = await storageService.GetFileVersionsAsync(bucketName, key, cancellationToken);
        return Results.Ok(versions);
    }
}
