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
    private readonly IKnowledgeDocumentIngestionService _documentIngestion;
    private readonly IKnowledgeUploadTextExtractor _uploadTextExtractor;
    private readonly IKnowledgeQueryService _knowledgeQueries;

    public KnowledgeController(
        IKnowledgeService knowledgeService,
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        ILogger<KnowledgeController> logger,
        IContentDocumentService contentDocuments,
        IKnowledgeDocumentIngestionService documentIngestion,
        IKnowledgeUploadTextExtractor uploadTextExtractor,
        IKnowledgeQueryService knowledgeQueries)
    {
        _knowledgeService = knowledgeService;
        _db = db;
        _currentUserService = currentUserService;
        _contentDocuments = contentDocuments;
        _logger = logger;
        _documentIngestion = documentIngestion;
        _uploadTextExtractor = uploadTextExtractor;
        _knowledgeQueries = knowledgeQueries;
    }

    [HttpGet("directories")]
    public async Task<IActionResult> ListDirectories(CancellationToken ct)
    {
        try
        {
            var result = await _knowledgeQueries.QueryAsync(new KnowledgeQueryRequest(
                KnowledgeQueryIntent.Inventory,
                KnowledgeQueryScope.UserLibrary,
                ProjectId: null,
                Limit: 1), ct);
            return Ok(result.Directories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list knowledge directories");
            return StatusCode(500, ApiErrors.Internal("Failed to list knowledge directories"));
        }
    }

    [HttpPost("directories")]
    public async Task<IActionResult> CreateDirectory(
        [FromBody] CreateKnowledgeDirectoryRequest request,
        CancellationToken ct)
    {
        try
        {
            request.IdempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                ? idempotencyKey.ToString()
                : string.Empty;
            var directory = await _knowledgeService.CreateKnowledgeDirectoryAsync(request, ct);
            return Ok(directory);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiErrors.BadRequest(ex.Message, code: "KNOWLEDGE_DIRECTORY_CONFLICT", recommendedAction: "请换一个目录名称后重试。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create knowledge directory");
            return StatusCode(500, ApiErrors.Internal("Failed to create knowledge directory"));
        }
    }

    [HttpPatch("directories/{key}")]
    public async Task<IActionResult> UpdateDirectory(
        string key,
        [FromBody] UpdateKnowledgeDirectoryRequest request,
        CancellationToken ct)
    {
        try
        {
            var directory = await _knowledgeService.UpdateKnowledgeDirectoryAsync(key, request, ct);
            return Ok(directory);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiErrors.BadRequest(ex.Message, code: "KNOWLEDGE_DIRECTORY_CONFLICT", recommendedAction: "请调整目录后重试。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update knowledge directory {DirectoryKey}", key);
            return StatusCode(500, ApiErrors.Internal("Failed to update knowledge directory"));
        }
    }

    [HttpDelete("directories/{key}")]
    public async Task<IActionResult> DeleteDirectory(string key, CancellationToken ct)
    {
        try
        {
            await _knowledgeService.DeleteKnowledgeDirectoryAsync(key, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiErrors.BadRequest(ex.Message, code: "KNOWLEDGE_DIRECTORY_CONFLICT", recommendedAction: "请先处理目录下条目后重试。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete knowledge directory {DirectoryKey}", key);
            return StatusCode(500, ApiErrors.Internal("Failed to delete knowledge directory"));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateKnowledge(
        [FromBody] CreateKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            request.IdempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey)
                ? idempotencyKey.ToString()
                : string.Empty;
            var knowledge = await _knowledgeService.CreateKnowledgeAsync(request, ct);
            return Ok(knowledge);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create knowledge entry");
            return StatusCode(500, ApiErrors.Internal("Failed to create knowledge entry"));
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListKnowledge(
        [FromQuery] string? projectId,
        CancellationToken ct)
    {
        try
        {
            var result = await _knowledgeQueries.QueryAsync(new KnowledgeQueryRequest(
                KnowledgeQueryIntent.Inventory,
                string.IsNullOrWhiteSpace(projectId)
                    ? KnowledgeQueryScope.UserLibrary
                    : KnowledgeQueryScope.CurrentProject,
                projectId,
                Limit: 1), ct);
            return Ok(result.Inventory);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list knowledge for project {ProjectId}", projectId);
            return StatusCode(500, ApiErrors.Internal("Failed to list knowledge"));
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get knowledge {KnowledgeId}", id);
            return StatusCode(500, ApiErrors.Internal("Failed to get knowledge"));
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update knowledge {KnowledgeId}", id);
            return StatusCode(500, ApiErrors.Internal("Failed to update knowledge"));
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
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete knowledge {KnowledgeId}", id);
            return StatusCode(500, ApiErrors.Internal("Failed to delete knowledge"));
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchKnowledge(
        [FromBody] SearchKnowledgeRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _knowledgeQueries.QueryAsync(new KnowledgeQueryRequest(
                KnowledgeQueryIntent.Retrieve,
                KnowledgeQueryScope.CurrentProject,
                request.ProjectId,
                request.Query,
                request.EntryType,
                request.TopK <= 0 ? 10 : Math.Clamp(request.TopK, 1, 50)), ct);
            return Ok(result.Matches);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search knowledge in project {ProjectId}", request.ProjectId);
            return StatusCode(500, ApiErrors.Internal("Failed to search knowledge"));
        }
    }

    [HttpPost("{id}/usage")]
    public async Task<IActionResult> IncrementUsage(
        string id,
        [FromBody] IncrementKnowledgeUsageRequest request,
        CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(ApiErrors.BadRequest("ProjectId is required"));

        try
        {
            var idempotencyKey = HttpContext?.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader) == true
                ? idempotencyKeyHeader.ToString()
                : request.IdempotencyKey ?? string.Empty;
            await _knowledgeService.IncrementUsageAsync(
                id,
                request.ProjectId,
                request.SessionId,
                request.RunId,
                idempotencyKey,
                ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiErrors.NotFound(ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment usage for knowledge {KnowledgeId}", id);
            return StatusCode(500, ApiErrors.Internal("Failed to increment usage"));
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(
        [FromForm] IFormFile file,
        [FromForm] string? projectId,
        [FromForm] string? title,
        CancellationToken ct = default)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(ApiErrors.BadRequest("No file provided"));
            if (string.IsNullOrWhiteSpace(projectId))
                return BadRequest(ApiErrors.BadRequest("projectId is required"));

            var userId = _currentUserService.GetUserId();
            var normalizedProjectId = projectId.Trim();
            var projectExists = await _db.NovelProjects
                .AsNoTracking()
                .AnyAsync(p => p.Id == normalizedProjectId && p.UserId == userId, ct);
            if (!projectExists)
                return NotFound(ApiErrors.NotFound("Project not found"));

            var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader)
                ? idempotencyKeyHeader.ToString().Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existingTask = await _db.KnowledgeProcessingTasks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(task =>
                        task.UserId == userId &&
                        task.ProjectId == normalizedProjectId &&
                        task.IdempotencyKey == idempotencyKey,
                        ct);
                if (existingTask != null)
                    return Ok(ToUploadResponse(existingTask));
            }

            var fileName = Path.GetFileName(file.FileName);
            var extraction = await _uploadTextExtractor.ExtractAsync(file, ct);
            var uploadBlob = await _documentIngestion.StoreUploadAsync(
                normalizedProjectId,
                fileName,
                file.ContentType,
                extraction.OriginalBytes,
                ct);

            var task = new Data.Entities.KnowledgeProcessingTask
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = normalizedProjectId,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                FileName = title ?? fileName,
                FileSize = file.Length,
                Status = "pending",
                Strategy = "single_pass",
                Progress = 0,
                CreatedAt = DateTime.UtcNow
            };
            task.UploadBlobId = uploadBlob.Id;

            _db.KnowledgeProcessingTasks.Add(task);
            await _db.SaveChangesAsync(ct);

            var uploadDocument = await _contentDocuments.SaveOrReplaceTextAsync(
                userId,
                normalizedProjectId,
                "knowledge_upload",
                task.Id,
                "upload_raw",
                task.FileName,
                extraction.Text,
                ct);
            task.UploadDocumentId = uploadDocument.Id;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("File uploaded: {FileName} ({FileSize} bytes) for user {UserId}, task {TaskId}",
                task.FileName, task.FileSize, userId, task.Id);

            return Ok(ToUploadResponse(task));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiErrors.Create("UNAUTHENTICATED", "User not authenticated", "authorization", recoverable: true, recommendedAction: "请重新登录后再试。"));
        }
        catch (KnowledgeUploadValidationException ex)
        {
            var error = ApiErrors.BadRequest(
                ex.Message,
                code: ex.IsTooLarge ? "KNOWLEDGE_FILE_TOO_LARGE" : "KNOWLEDGE_FILE_INVALID",
                recommendedAction: "请上传有效的 TXT、PDF 或 EPUB 文件后重试。");
            return ex.IsTooLarge ? StatusCode(StatusCodes.Status413PayloadTooLarge, error) : BadRequest(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file");
            return StatusCode(500, ApiErrors.Internal("Failed to upload file"));
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
                return NotFound(ApiErrors.NotFound("Task not found"));

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
            return Unauthorized(ApiErrors.Create("UNAUTHENTICATED", "User not authenticated", "authorization", recoverable: true, recommendedAction: "请重新登录后再试。"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get task status for {TaskId}", taskId);
            return StatusCode(500, ApiErrors.Internal("Failed to get task status"));
        }
    }

    private static object ToUploadResponse(Data.Entities.KnowledgeProcessingTask task) => new
    {
        taskId = task.Id,
        fileName = task.FileName,
        fileSize = task.FileSize,
        status = task.Status,
        message = "文件已上传，等待处理"
    };
}
