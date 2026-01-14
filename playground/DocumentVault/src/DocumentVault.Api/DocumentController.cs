using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace DocumentVault.Api;

[ApiController]
[Route("api/[controller]")]
public class DocumentController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string MetadataBucket = "document-metadata";
    private const string ContentBucket = "document-content";

    public DocumentController(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
        EnsureBucketsExist().Wait();
    }

    private async Task EnsureBucketsExist()
    {
        try { await _s3Client.PutBucketAsync(MetadataBucket); } catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists") { }
        try { await _s3Client.PutBucketAsync(ContentBucket); } catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists") { }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Document>>> GetAll([FromQuery] string? tag = null)
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = MetadataBucket });
        var documents = new List<Document>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(MetadataBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var document = JsonSerializer.Deserialize<Document>(json);
            if (document != null)
            {
                if (string.IsNullOrEmpty(tag) || document.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    documents.Add(document);
                }
            }
        }

        return Ok(documents.OrderByDescending(x => x.UploadedAt));
    }

    [HttpGet("tags")]
    public async Task<ActionResult<IEnumerable<string>>> GetAllTags()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = MetadataBucket });
        var tags = new HashSet<string>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(MetadataBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var document = JsonSerializer.Deserialize<Document>(json);
            if (document != null)
            {
                foreach (var tag in document.Tags)
                {
                    tags.Add(tag);
                }
            }
        }

        return Ok(tags.OrderBy(x => x));
    }

    [HttpGet("{id}/download")]
    public async Task<ActionResult> Download(string id)
    {
        try
        {
            var metaResponse = await _s3Client.GetObjectAsync(MetadataBucket, id);
            using var metaReader = new StreamReader(metaResponse.ResponseStream);
            var json = await metaReader.ReadToEndAsync();
            var document = JsonSerializer.Deserialize<Document>(json);

            var contentResponse = await _s3Client.GetObjectAsync(ContentBucket, id);
            return File(contentResponse.ResponseStream, document?.ContentType ?? "application/octet-stream", document?.Name);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<Document>> Upload(IFormFile file, [FromForm] string? tags = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var document = new Document
        {
            Id = Guid.NewGuid().ToString(),
            Name = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            Tags = string.IsNullOrEmpty(tags) ? new List<string>() : tags.Split(',').Select(t => t.Trim()).ToList(),
            UploadedAt = DateTime.UtcNow
        };

        using var contentStream = new MemoryStream();
        await file.CopyToAsync(contentStream);
        contentStream.Position = 0;

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = ContentBucket,
            Key = document.Id,
            InputStream = contentStream,
            ContentType = file.ContentType
        });

        var json = JsonSerializer.Serialize(document);
        using var metaStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = MetadataBucket,
            Key = document.Id,
            InputStream = metaStream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(Download), new { id = document.Id }, document);
    }

    [HttpPut("{id}/tags")]
    public async Task<ActionResult<Document>> UpdateTags(string id, [FromBody] List<string> tags)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(MetadataBucket, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var document = JsonSerializer.Deserialize<Document>(json);

            if (document == null) return NotFound();

            document.Tags = tags;

            var updatedJson = JsonSerializer.Serialize(document);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedJson));

            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = MetadataBucket,
                Key = id,
                InputStream = stream,
                ContentType = "application/json"
            });

            return Ok(document);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(MetadataBucket, id);
            await _s3Client.DeleteObjectAsync(ContentBucket, id);
            return NoContent();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }
}
