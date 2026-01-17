using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace TodoApp.Api;

[ApiController]
[Route("api/[controller]")]
public class TodoController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "todos";

    public TodoController(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
        EnsureBucketExists().Wait();
    }

    private async Task EnsureBucketExists()
    {
        try
        {
            await _s3Client.PutBucketAsync(BucketName);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyExists")
        {
        }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoItem>>> GetAll()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var items = new List<TodoItem>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var item = JsonSerializer.Deserialize<TodoItem>(json);
            if (item != null) items.Add(item);
        }

        return Ok(items.OrderByDescending(x => x.CreatedAt));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TodoItem>> GetById(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var item = JsonSerializer.Deserialize<TodoItem>(json);
            return Ok(item);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<TodoItem>> Create([FromBody] CreateTodoItemRequest request)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = request.Title,
            Description = request.Description,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(item);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = item.Id,
            InputStream = stream,
            ContentType = "application/json"
        });

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TodoItem>> Update(string id, [FromBody] UpdateTodoItemRequest request)
    {
        try
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var existingJson = await reader.ReadToEndAsync();
            var existingItem = JsonSerializer.Deserialize<TodoItem>(existingJson);

            if (existingItem == null) return NotFound();

            existingItem.Title = request.Title;
            existingItem.Description = request.Description;
            existingItem.IsCompleted = request.IsCompleted;
            if (request.IsCompleted && !existingItem.CompletedAt.HasValue)
            {
                existingItem.CompletedAt = DateTime.UtcNow;
            }

            var json = JsonSerializer.Serialize(existingItem);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = id,
                InputStream = stream,
                ContentType = "application/json"
            });

            return Ok(existingItem);
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
            await _s3Client.DeleteObjectAsync(BucketName, id);
            return NoContent();
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }
}
