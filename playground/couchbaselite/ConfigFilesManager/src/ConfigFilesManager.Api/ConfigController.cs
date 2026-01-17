using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace ConfigFilesManager.Api;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string MetadataBucket = "config-metadata";
    private const string ContentBucket = "config-content";

    public ConfigController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<ConfigFile>>> GetAll([FromQuery] string? fileType = null)
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = MetadataBucket });
        var configs = new List<ConfigFile>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(MetadataBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var config = JsonSerializer.Deserialize<ConfigFile>(json);
            if (config != null)
            {
                if (string.IsNullOrEmpty(fileType) || config.FileType.Equals(fileType, StringComparison.OrdinalIgnoreCase))
                {
                    configs.Add(config);
                }
            }
        }

        return Ok(configs.OrderByDescending(x => x.ModifiedAt));
    }

    [HttpGet("types")]
    public async Task<ActionResult<IEnumerable<string>>> GetFileTypes()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = MetadataBucket });
        var types = new HashSet<string>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(MetadataBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var config = JsonSerializer.Deserialize<ConfigFile>(json);
            if (config != null && !string.IsNullOrEmpty(config.FileType))
            {
                types.Add(config.FileType);
            }
        }

        return Ok(types.OrderBy(x => x));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ConfigFile>> GetById(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(MetadataBucket, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var config = JsonSerializer.Deserialize<ConfigFile>(json);
            return Ok(config);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpGet("{id}/content")]
    public async Task<ActionResult<string>> GetContent(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(ContentBucket, id);
            using var reader = new StreamReader(response.ResponseStream);
            var content = await reader.ReadToEndAsync();
            return Ok(content);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<ConfigFile>> Create([FromBody] CreateConfigFileRequest request)
    {
        var config = new ConfigFile
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            FileType = request.FileType,
            Description = request.Description,
            Environment = request.Environment,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        using var contentStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(request.Content));
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = ContentBucket,
            Key = config.Id,
            InputStream = contentStream,
            ContentType = "text/plain"
        });

        var json = JsonSerializer.Serialize(config);
        using var metaStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = MetadataBucket,
            Key = config.Id,
            InputStream = metaStream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(GetById), new { id = config.Id }, config);
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
