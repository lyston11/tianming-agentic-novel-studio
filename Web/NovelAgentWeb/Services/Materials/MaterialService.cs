using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Content;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Services.Vectorization;

namespace TM.Web.NovelAgentWeb.Services.Materials;

/// <summary>
/// Service for managing materials (reference documents and source content).
/// </summary>
public class MaterialService : IMaterialService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMaterialVectorizationService _vectorization;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<MaterialService> _logger;
    private readonly IContentDocumentService _contentDocuments;

    public MaterialService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        IMaterialVectorizationService vectorization,
        IVectorStore vectorStore,
        ILogger<MaterialService> logger,
        IContentDocumentService contentDocuments)
    {
        _db = db;
        _currentUserService = currentUserService;
        _vectorization = vectorization;
        _vectorStore = vectorStore;
        _logger = logger;
        _contentDocuments = contentDocuments;
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

        var safeFileName = Path.GetFileName(request.File.FileName);
        var content = await ReadFormFileTextAsync(request.File, ct);

        // Create material entity
        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Title = request.Title,
            ContentType = request.File.ContentType,
            Category = request.Category,
            Tags = request.Tags,
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        await SaveMaterialContentDocumentAsync(userId, material, request.Title, content, ct);
        await TryVectorizeMaterialAsync(material.Id, userId, ct);

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
            ContentType = request.ContentType,
            Category = request.Category,
            Tags = request.Tags,
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        await SaveMaterialContentDocumentAsync(userId, material, request.Title, request.Content, ct);
        await TryVectorizeMaterialAsync(material.Id, userId, ct);

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

        var content = await _contentDocuments.GetTextAsync(userId, material.ProjectId, "material", material.Id, "material_raw", ct);

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

        if (request.Title != null)
            material.Title = request.Title;

        if (request.Category != null)
            material.Category = request.Category;

        if (request.Tags != null)
            material.Tags = request.Tags;

        await _db.SaveChangesAsync(ct);
        await TryVectorizeMaterialAsync(material.Id, userId, ct);

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

        await TryDeleteMaterialVectorsAsync(userId, material, ct);

        await _contentDocuments.DeleteBySourceAsync(userId, material.ProjectId, "material", material.Id, "material_raw", ct);

        _db.Materials.Remove(material);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted material {MaterialId}", materialId);
    }

    private async Task TryVectorizeMaterialAsync(string materialId, string userId, CancellationToken ct)
    {
        try
        {
            await _vectorization.VectorizeMaterialAsync(materialId, userId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to vectorize material {MaterialId}", materialId);
        }
    }

    private async Task SaveMaterialContentDocumentAsync(
        string userId,
        Material material,
        string title,
        string? content,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Material content is empty.");

        var document = await _contentDocuments.SaveOrReplaceTextAsync(
            userId,
            material.ProjectId,
            "material",
            material.Id,
            "material_raw",
            title,
            content,
            ct);
        material.RawDocumentId = document.Id;
        await _db.SaveChangesAsync(ct);
    }

    private async Task TryDeleteMaterialVectorsAsync(string userId, Material material, CancellationToken ct)
    {
        try
        {
            var filters = new Dictionary<string, object>
            {
                ["source_type"] = "material",
                ["source_id"] = material.Id
            };

            if (!string.IsNullOrWhiteSpace(material.ProjectId))
                filters["project_id"] = material.ProjectId;

            await _vectorStore.DeleteVectorsByFilterAsync(userId, filters, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete vectors for material {MaterialId}", material.Id);
        }
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
            Tags = material.Tags,
            CreatedAt = material.CreatedAt,
            VectorChunkCount = material.VectorChunkCount
        };
    }

    private static async Task<string> ReadFormFileTextAsync(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Material content is empty.");
        return text;
    }
}
