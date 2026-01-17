using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace PhotoGalleryApp.Api;

[ApiController]
[Route("api/[controller]")]
public class PhotoController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string MetadataBucket = "photo-metadata";
    private const string ContentBucket = "photo-content";

    public PhotoController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<Photo>>> GetAll()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = MetadataBucket });
        var photos = new List<Photo>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(MetadataBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var photo = JsonSerializer.Deserialize<Photo>(json);
            if (photo != null) photos.Add(photo);
        }

        return Ok(photos.OrderByDescending(x => x.UploadedAt));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetContent(string id)
    {
        try
        {
            var metaResponse = await _s3Client.GetObjectAsync(MetadataBucket, id);
            using var metaReader = new StreamReader(metaResponse.ResponseStream);
            var json = await metaReader.ReadToEndAsync();
            var photo = JsonSerializer.Deserialize<Photo>(json);

            var contentResponse = await _s3Client.GetObjectAsync(ContentBucket, id);
            return File(contentResponse.ResponseStream, photo?.ContentType ?? "image/jpeg");
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<Photo>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var photo = new Photo
        {
            Id = Guid.NewGuid().ToString(),
            Name = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            UploadedAt = DateTime.UtcNow
        };

        using var contentStream = new MemoryStream();
        await file.CopyToAsync(contentStream);
        contentStream.Position = 0;

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = ContentBucket,
            Key = photo.Id,
            InputStream = contentStream,
            ContentType = file.ContentType
        });

        var json = JsonSerializer.Serialize(photo);
        using var metaStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = MetadataBucket,
            Key = photo.Id,
            InputStream = metaStream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(GetContent), new { id = photo.Id }, photo);
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
