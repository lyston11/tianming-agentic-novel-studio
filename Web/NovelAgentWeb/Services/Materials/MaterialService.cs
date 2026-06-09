using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Materials;

/// <summary>
/// Service for managing materials (reference documents and source content).
/// </summary>
public class MaterialService : IMaterialService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MaterialService> _logger;

    public MaterialService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        IConfiguration configuration,
        ILogger<MaterialService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MaterialResponse> UploadMaterialAsync(
        UploadMaterialRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        // Save file to disk
        var storageRoot = _configuration["NovelAgent:StorageRoot"] ?? "App_Data";
        var materialsDir = Path.Combine(storageRoot, "Users", userId, "Projects", request.ProjectId, "Materials");
        Directory.CreateDirectory(materialsDir);

        var safeFileName = Path.GetFileName(request.File.FileName);
        var filePath = Path.Combine(materialsDir, safeFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await request.File.CopyToAsync(stream, ct);
        }

        // Create material entity
        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Title = request.Title,
            FilePath = filePath,
            Category = request.Category,
            Tags = request.Tags,
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Uploaded material {MaterialId} to project {ProjectId}", material.Id, request.ProjectId);

        return MapToResponse(material);
    }

    public async Task<MaterialResponse> CreateMaterialAsync(
        CreateMaterialRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Title = request.Title,
            Content = request.Content,
            ContentType = request.ContentType,
            Category = request.Category,
            Tags = request.Tags,
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created material {MaterialId} in project {ProjectId}", material.Id, request.ProjectId);

        return MapToResponse(material);
    }

    public async Task<List<MaterialResponse>> ListMaterialsAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var materials = await _db.Materials
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        return materials.Select(MapToResponse).ToList();
    }

    public async Task<MaterialResponse> GetMaterialAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        return MapToResponse(material);
    }

    public async Task<MaterialContentResponse> GetMaterialContentAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        string content;
        if (!string.IsNullOrEmpty(material.FilePath) && File.Exists(material.FilePath))
        {
            content = await File.ReadAllTextAsync(material.FilePath, ct);
        }
        else if (!string.IsNullOrEmpty(material.Content))
        {
            content = material.Content;
        }
        else
        {
            throw new InvalidOperationException($"Material {materialId} has no content available");
        }

        return new MaterialContentResponse
        {
            Id = material.Id,
            Title = material.Title,
            ContentType = material.ContentType,
            Content = content
        };
    }

    public async Task<MaterialResponse> UpdateMaterialAsync(
        string materialId, UpdateMaterialRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        if (request.Category != null)
            material.Category = request.Category;

        if (request.Tags != null)
            material.Tags = request.Tags;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated material {MaterialId}", materialId);

        return MapToResponse(material);
    }

    public async Task DeleteMaterialAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials
            .FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);

        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        // Delete file if exists
        if (!string.IsNullOrEmpty(material.FilePath) && File.Exists(material.FilePath))
        {
            try
            {
                File.Delete(material.FilePath);
                _logger.LogInformation("Deleted file for material {MaterialId}", materialId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file for material {MaterialId}", materialId);
            }
        }

        _db.Materials.Remove(material);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted material {MaterialId}", materialId);
    }

    private static MaterialResponse MapToResponse(Material material)
    {
        return new MaterialResponse
        {
            Id = material.Id,
            UserId = material.UserId,
            ProjectId = material.ProjectId,
            Title = material.Title,
            Category = material.Category,
            ContentType = material.ContentType,
            FilePath = material.FilePath,
            Tags = material.Tags,
            CreatedAt = material.CreatedAt,
            VectorChunkCount = material.VectorChunkCount
        };
    }
}
