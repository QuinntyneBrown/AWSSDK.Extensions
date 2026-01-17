using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace EventLogger.Api;

[ApiController]
[Route("api/[controller]")]
public class EventController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "events";

    public EventController(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
        EnsureBucketExists().Wait();
    }

    private async Task EnsureBucketExists()
    {
        try { await _s3Client.PutBucketAsync(BucketName); }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists") { }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LogEvent>>> GetAll([FromQuery] string? level = null)
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var events = new List<LogEvent>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var logEvent = JsonSerializer.Deserialize<LogEvent>(json);
            if (logEvent != null)
            {
                if (string.IsNullOrEmpty(level) || logEvent.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(logEvent);
                }
            }
        }

        return Ok(events.OrderByDescending(x => x.Timestamp));
    }

    [HttpPost]
    public async Task<ActionResult<LogEvent>> Create([FromBody] CreateLogEventRequest request)
    {
        var logEvent = new LogEvent
        {
            Id = Guid.NewGuid().ToString(),
            Message = request.Message,
            Level = request.Level,
            Source = request.Source,
            Metadata = request.Metadata,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(logEvent);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = logEvent.Id,
            InputStream = stream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(GetAll), logEvent);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(BucketName, id);
            return NoContent();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpDelete]
    public async Task<ActionResult> Clear()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        foreach (var obj in response.S3Objects)
        {
            await _s3Client.DeleteObjectAsync(BucketName, obj.Key);
        }
        return NoContent();
    }
}
