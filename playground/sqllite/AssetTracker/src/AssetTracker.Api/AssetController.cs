using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace AssetTracker.Api;

[ApiController]
[Route("api/[controller]")]
public class AssetController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "assets";

    public AssetController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<Asset>>> GetAll([FromQuery] string? status = null)
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var assets = new List<Asset>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var asset = JsonSerializer.Deserialize<Asset>(json);
            if (asset != null)
            {
                if (string.IsNullOrEmpty(status) || asset.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(asset);
                }
            }
        }

        return Ok(assets.OrderBy(x => x.Name));
    }

    [HttpPost]
    public async Task<ActionResult<Asset>> Create([FromBody] CreateAssetRequest request)
    {
        var asset = new Asset
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Category = request.Category,
            Location = request.Location,
            AcquiredAt = request.AcquiredAt,
            Status = "available",
            CreatedAt = DateTime.UtcNow
        };

        await SaveAsset(asset);
        return CreatedAtAction(nameof(GetAll), asset);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Asset>> Update(string id, [FromBody] UpdateAssetRequest request)
    {
        var asset = await GetAsset(id);
        if (asset == null) return NotFound();

        asset.Status = request.Status;
        asset.Location = request.Location;
        asset.AssignedTo = request.AssignedTo;

        await SaveAsset(asset);
        return Ok(asset);
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

    private async Task<Asset?> GetAsset(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<Asset>(json);
        }
        catch { return null; }
    }

    private async Task SaveAsset(Asset asset)
    {
        var json = JsonSerializer.Serialize(asset);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = asset.Id,
            InputStream = stream,
            ContentType = "application/json"
        });
    }
}
