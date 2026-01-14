using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;

namespace PlanningPokerApp.Api;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly IAmazonS3 _s3Client;
    private const string BucketName = "poker-sessions";

    public SessionController(IAmazonS3 s3Client)
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
    public async Task<ActionResult<IEnumerable<PokerSession>>> GetAll()
    {
        var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BucketName });
        var sessions = new List<PokerSession>();

        foreach (var obj in response.S3Objects)
        {
            var getResponse = await _s3Client.GetObjectAsync(BucketName, obj.Key);
            using var reader = new StreamReader(getResponse.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var session = JsonSerializer.Deserialize<PokerSession>(json);
            if (session != null) sessions.Add(session);
        }

        return Ok(sessions.OrderByDescending(x => x.CreatedAt));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PokerSession>> GetById(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            var session = JsonSerializer.Deserialize<PokerSession>(json);
            return Ok(session);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<PokerSession>> Create([FromBody] CreateSessionRequest request)
    {
        var session = new PokerSession
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        await SaveSession(session);
        return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
    }

    [HttpPut("{id}/story")]
    public async Task<ActionResult<PokerSession>> UpdateStory(string id, [FromBody] UpdateStoryRequest request)
    {
        var session = await GetSession(id);
        if (session == null) return NotFound();

        session.CurrentStory = request.Story;
        session.Votes.Clear();
        session.IsRevealed = false;

        await SaveSession(session);
        return Ok(session);
    }

    [HttpPost("{id}/vote")]
    public async Task<ActionResult<PokerSession>> SubmitVote(string id, [FromBody] SubmitVoteRequest request)
    {
        var session = await GetSession(id);
        if (session == null) return NotFound();

        session.Votes.RemoveAll(v => v.ParticipantName == request.ParticipantName);
        session.Votes.Add(new Vote
        {
            ParticipantName = request.ParticipantName,
            Value = request.Value,
            VotedAt = DateTime.UtcNow
        });

        await SaveSession(session);
        return Ok(session);
    }

    [HttpPost("{id}/reveal")]
    public async Task<ActionResult<PokerSession>> RevealVotes(string id)
    {
        var session = await GetSession(id);
        if (session == null) return NotFound();

        session.IsRevealed = true;
        await SaveSession(session);
        return Ok(session);
    }

    [HttpPost("{id}/reset")]
    public async Task<ActionResult<PokerSession>> ResetVotes(string id)
    {
        var session = await GetSession(id);
        if (session == null) return NotFound();

        session.Votes.Clear();
        session.IsRevealed = false;
        await SaveSession(session);
        return Ok(session);
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

    private async Task<PokerSession?> GetSession(string id)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(BucketName, id);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<PokerSession>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveSession(PokerSession session)
    {
        var json = JsonSerializer.Serialize(session);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = session.Id,
            InputStream = stream,
            ContentType = "application/json"
        });
    }
}
