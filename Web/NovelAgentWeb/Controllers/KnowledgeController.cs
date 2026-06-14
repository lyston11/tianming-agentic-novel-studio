using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.Knowledge;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/knowledge")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IContentDocumentService _contentDocuments;
    private readonly ILogger<KnowledgeController> _logger;

    public KnowledgeController(
        IKnowledgeService knowledgeService,
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        ILogger<KnowledgeController> logger,
        IContentDocumentService contentDocuments)
    {
        _knowledgeService = knowledgeService;
        _db = db;
        _currentUserService = currentUserService;
        _contentDocuments = contentDocuments;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateKnowledge(
        [FromBody] CreateKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.CreateKnowledgeAsync(request, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create knowledge entry");
            return StatusCode(500, new { error = "Failed to create knowledge entry" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListKnowledge(
        [FromQuery] string projectId,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.ListKnowledgeAsync(projectId, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list knowledge for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to list knowledge" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetKnowledge(string id, CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.GetKnowledgeAsync(id, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to get knowledge" });
        }
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateKnowledge(
        string id,
        [FromBody] UpdateKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var knowledge = await _knowledgeService.UpdateKnowledgeAsync(id, request, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to update knowledge" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteKnowledge(string id, CancellationToken ct)
    {
        try
        {
            await _knowledgeService.DeleteKnowledgeAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to delete knowledge" });
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchKnowledge(
        [FromBody] SearchKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var results = await _knowledgeService.SearchKnowledgeAsync(request, ct);
            return Ok(results);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search knowledge in project {ProjectId}", request.ProjectId);
            return StatusCode(500, new { error = "Failed to search knowledge" });
        }
    }

    [HttpPost("{id}/usage")]
    public async Task<IActionResult> IncrementUsage(
        string id,
        [FromBody] IncrementKnowledgeUsageRequest request,
        CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(new { error = "ProjectId is required" });

        try
        {
            await _knowledgeService.IncrementUsageAsync(id, request.ProjectId, request.SessionId, request.RunId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for knowledge {KnowledgeId}", id);
            return StatusCode(500, new { error = "Failed to increment usage" });
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(
        [FromForm] IFormFile file,
        [FromForm] string? projectId,
        [FromForm] string? title)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });
            if (string.IsNullOrWhiteSpace(projectId))
                return BadRequest(new { error = "projectId is required" });

            var userId = _currentUserService.GetUserId();
            var normalizedProjectId = projectId.Trim();
            var projectExists = await _db.NovelProjects
                .AsNoTracking()
                .AnyAsync(p => p.Id == normalizedProjectId && p.UserId == userId);
            if (!projectExists)
                return NotFound(new { error = "Project not found" });

            var fileName = Path.GetFileName(file.FileName);
            var text = await ReadFormFileTextAsync(file);

            var task = new Data.Entities.KnowledgeProcessingTask
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = normalizedProjectId,
                FileName = title ?? fileName,
                FileSize = file.Length,
                Status = "pending",
                Strategy = "single_pass",
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.KnowledgeProcessingTasks.Add(task);
            await _db.SaveChangesAsync();

            var uploadDocument = await _contentDocuments.SaveOrReplaceTextAsync(
                userId,
                normalizedProjectId,
                "knowledge_upload",
                task.Id,
                "upload_raw",
                task.FileName,
                text);
            task.UploadDocumentId = uploadDocument.Id;
            await _db.SaveChangesAsync();

            _logger.LogInformation("File uploaded: {FileName} ({FileSize} bytes) for user {UserId}, task {TaskId}",
                task.FileName, task.FileSize, userId, task.Id);

            return Ok(new
            {
                taskId = task.Id,
                fileName = task.FileName,
                fileSize = task.FileSize,
                status = task.Status,
                message = "文件已上传，等待处理"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "User not authenticated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file");
            return StatusCode(500, new { error = "Failed to upload file" });
        }
    }

    [HttpGet("tasks/{taskId}")]
    public async Task<IActionResult> GetTaskStatus(string taskId)
    {
        try
        {
            var userId = _currentUserService.GetUserId();
            var task = await _db.KnowledgeProcessingTasks
                .Where(t => t.Id == taskId && t.UserId == userId)
                .FirstOrDefaultAsync();

            if (task == null)
                return NotFound(new { error = "Task not found" });

            return Ok(new KnowledgeProcessingTaskDto
            {
                Id = task.Id,
                FileName = task.FileName,
                FileSize = task.FileSize,
                Status = task.Status,
                Strategy = task.Strategy,
                Progress = task.Progress,
                TotalChunks = task.TotalChunks,
                ProcessedChunks = task.ProcessedChunks,
                ExtractedEntriesCount = task.ExtractedEntriesCount,
                ErrorMessage = task.ErrorMessage,
                StartedAt = task.StartedAt,
                CompletedAt = task.CompletedAt,
                CreatedAt = task.CreatedAt
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "User not authenticated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get task status for {TaskId}", taskId);
            return StatusCode(500, new { error = "Failed to get task status" });
        }
    }

    private static async Task<string> ReadFormFileTextAsync(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Uploaded knowledge content is empty.");
        return text;
    }
}
