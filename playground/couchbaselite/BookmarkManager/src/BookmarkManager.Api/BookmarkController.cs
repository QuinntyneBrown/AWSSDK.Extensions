using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace BookmarkManager.Api;

[ApiController]
[Route("api/[controller]")]
public class BookmarkController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "bookmarks";

    public BookmarkController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<Bookmark>>> GetAll([FromQuery] string? category = null)
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var bookmarks = new List<Bookmark>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var bookmark = JsonSerializer.Deserialize<Bookmark>(json);
            if (bookmark != null)
            {
                if (string.IsNullOrEmpty(category) || bookmark.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    bookmarks.Add(bookmark);
                }
            }
        }

        return Ok(bookmarks.OrderByDescending(x => x.CreatedAt));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var categories = new HashSet<string>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var bookmark = JsonSerializer.Deserialize<Bookmark>(json);
            if (bookmark != null && !string.IsNullOrEmpty(bookmark.Category))
            {
                categories.Add(bookmark.Category);
            }
        }

        return Ok(categories.OrderBy(x => x));
    }

    [HttpPost]
    public async Task<ActionResult<Bookmark>> Create([FromBody] CreateBookmarkRequest request)
    {
        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid().ToString(),
            Title = request.Title,
            Url = request.Url,
            Category = request.Category,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(bookmark);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = bookmark.Id,
            InputStream = stream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(GetAll), bookmark);
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
}
