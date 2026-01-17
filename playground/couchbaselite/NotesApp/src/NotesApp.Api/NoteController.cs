using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace NotesApp.Api;

[ApiController]
[Route("api/[controller]")]
public class NoteController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "notes";

    public NoteController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<Note>>> GetAll()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var notes = new List<Note>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var note = JsonSerializer.Deserialize<Note>(json);
            if (note != null) notes.Add(note);
        }

        return Ok(notes.OrderByDescending(x => x.ModifiedAt));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Note>> GetById(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var note = JsonSerializer.Deserialize<Note>(json);
            return Ok(note);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<Note>> Create([FromBody] CreateNoteRequest request)
    {
        var note = new Note
        {
            Id = Guid.NewGuid().ToString(),
            Title = request.Title,
            Content = request.Content,
            Color = request.Color,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        await SaveNote(note);
        return CreatedAtAction(nameof(GetById), new { id = note.Id }, note);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Note>> Update(string id, [FromBody] CreateNoteRequest request)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var note = JsonSerializer.Deserialize<Note>(json);

            if (note == null) return NotFound();

            note.Title = request.Title;
            note.Content = request.Content;
            note.Color = request.Color;
            note.ModifiedAt = DateTime.UtcNow;

            await SaveNote(note);
            return Ok(note);
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

    private async Task SaveNote(Note note)
    {
        var json = JsonSerializer.Serialize(note);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = note.Id,
            InputStream = stream,
            ContentType = "application/json"
        });
    }
}
