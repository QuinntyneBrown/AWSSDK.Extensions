using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace TelemetryDashboard.Api;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string TelemetryBucket = "telemetry-points";
    private const string PanelBucket = "dashboard-panels";

    public TelemetryController(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
        EnsureBucketsExist().Wait();
    }

    private async Task EnsureBucketsExist()
    {
        try { await _s3Client.PutBucketAsync(TelemetryBucket); } catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists") { }
        try { await _s3Client.PutBucketAsync(PanelBucket); } catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists") { }
    }

    [HttpGet("points")]
    public async Task<ActionResult<IEnumerable<TelemetryPoint>>> GetAllPoints()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = TelemetryBucket });
        var points = new List<TelemetryPoint>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(TelemetryBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var point = JsonSerializer.Deserialize<TelemetryPoint>(json);
            if (point != null) points.Add(point);
        }

        return Ok(points.OrderBy(x => x.Category).ThenBy(x => x.Name));
    }

    [HttpPost("points")]
    public async Task<ActionResult<TelemetryPoint>> CreatePoint([FromBody] CreateTelemetryPointRequest request)
    {
        var point = new TelemetryPoint
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Unit = request.Unit,
            Category = request.Category,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Value = request.MinValue,
            Timestamp = DateTime.UtcNow
        };

        await SaveTelemetryPoint(point);
        return CreatedAtAction(nameof(GetAllPoints), point);
    }

    [HttpPut("points/{id}/value")]
    public async Task<ActionResult<TelemetryPoint>> UpdateValue(string id, [FromBody] double value)
    {
        var point = await GetTelemetryPoint(id);
        if (point == null) return NotFound();

        point.Value = value;
        point.Timestamp = DateTime.UtcNow;
        await SaveTelemetryPoint(point);
        return Ok(point);
    }

    [HttpDelete("points/{id}")]
    public async Task<ActionResult> DeletePoint(string id)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(TelemetryBucket, id);
            return NoContent();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpGet("panels")]
    public async Task<ActionResult<IEnumerable<DashboardPanel>>> GetAllPanels()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = PanelBucket });
        var panels = new List<DashboardPanel>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(PanelBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var panel = JsonSerializer.Deserialize<DashboardPanel>(json);
            if (panel != null) panels.Add(panel);
        }

        return Ok(panels.OrderBy(x => x.Position));
    }

    [HttpPost("panels")]
    public async Task<ActionResult<DashboardPanel>> CreatePanel([FromBody] CreatePanelRequest request)
    {
        var panels = await GetAllPanelsInternal();
        var panel = new DashboardPanel
        {
            Id = Guid.NewGuid().ToString(),
            Title = request.Title,
            TelemetryPointId = request.TelemetryPointId,
            DisplayType = request.DisplayType,
            Position = panels.Count,
            CreatedAt = DateTime.UtcNow
        };

        await SavePanel(panel);
        return CreatedAtAction(nameof(GetAllPanels), panel);
    }

    [HttpDelete("panels/{id}")]
    public async Task<ActionResult> DeletePanel(string id)
    {
        try
        {
            await _s3Client.DeleteObjectAsync(PanelBucket, id);
            return NoContent();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    private async Task<TelemetryPoint?> GetTelemetryPoint(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(TelemetryBucket, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<TelemetryPoint>(json);
        }
        catch { return null; }
    }

    private async Task SaveTelemetryPoint(TelemetryPoint point)
    {
        var json = JsonSerializer.Serialize(point);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = TelemetryBucket,
            Key = point.Id,
            InputStream = stream,
            ContentType = "application/json"
        });
    }

    private async Task<List<DashboardPanel>> GetAllPanelsInternal()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = PanelBucket });
        var panels = new List<DashboardPanel>();
        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(PanelBucket, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var panel = JsonSerializer.Deserialize<DashboardPanel>(json);
            if (panel != null) panels.Add(panel);
        }
        return panels;
    }

    private async Task SavePanel(DashboardPanel panel)
    {
        var json = JsonSerializer.Serialize(panel);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = PanelBucket,
            Key = panel.Id,
            InputStream = stream,
            ContentType = "application/json"
        });
    }
}
