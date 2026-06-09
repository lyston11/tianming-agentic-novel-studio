# Phase 3: API Refactoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build 4 new REST controllers (Materials, Knowledge, StoryBible, Workflow) and refactor AgentController to use WorkspaceFactory

**Architecture:** RESTful API with JWT authentication, user isolation via ICurrentUserService, dependency injection for services, SSE streaming for Agent

**Tech Stack:** ASP.NET Core 8.0, Entity Framework Core, JWT Bearer authentication, SignalR/SSE

**Current State:** AuthController, SettingsController, ProjectController, ChaptersController exist. AgentController uses singleton NovelAgentWorkspace. Need to add Materials, Knowledge, StoryBible, Workflow controllers and refactor Agent to use WorkspaceFactory.

---

## Task 1: MaterialsController - File Upload and List Endpoints

**Files:**
- Create: `Web/NovelAgentWeb/Controllers/MaterialsController.cs`
- Create: `Web/NovelAgentWeb/Services/Materials/IMaterialService.cs`
- Create: `Web/NovelAgentWeb/Services/Materials/MaterialService.cs`
- Create: `Web/NovelAgentWeb/DTOs/MaterialDTOs.cs`

**Note:** Materials already exist in database (Material entity). We need service layer and controller.

- [ ] **Step 1: Create MaterialDTOs**

```csharp
// Web/NovelAgentWeb/DTOs/MaterialDTOs.cs
namespace TM.Web.NovelAgentWeb.DTOs;

public record UploadMaterialRequest(string FileName, string? Category);

public record CreateMaterialRequest(string FileName, string Content, string? Category, string SourceType);

public record UpdateMaterialRequest(string? Category, Dictionary<string, string>? Tags);

public record MaterialResponse(
    string Id,
    string ProjectId,
    string FileName,
    string? Category,
    Dictionary<string, string>? Tags,
    int VectorChunkCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record MaterialContentResponse(string Id, string Content);
```

- [ ] **Step 2: Create IMaterialService interface**

```csharp
// Web/NovelAgentWeb/Services/Materials/IMaterialService.cs
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Materials;

public interface IMaterialService
{
    Task<MaterialResponse> UploadMaterialAsync(string projectId, IFormFile file, string? category, CancellationToken ct = default);
    Task<MaterialResponse> CreateMaterialAsync(string projectId, CreateMaterialRequest request, CancellationToken ct = default);
    Task<List<MaterialResponse>> ListMaterialsAsync(string projectId, CancellationToken ct = default);
    Task<MaterialResponse> GetMaterialAsync(string materialId, CancellationToken ct = default);
    Task<MaterialContentResponse> GetMaterialContentAsync(string materialId, CancellationToken ct = default);
    Task<MaterialResponse> UpdateMaterialAsync(string materialId, UpdateMaterialRequest request, CancellationToken ct = default);
    Task DeleteMaterialAsync(string materialId, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create MaterialService implementation (Part 1 - Upload and List)**

```csharp
// Web/NovelAgentWeb/Services/Materials/MaterialService.cs
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Materials;

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
        string projectId, IFormFile file, string? category, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        
        // Verify project ownership
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        // Save file to disk
        var storageRoot = _configuration["NovelAgent:StorageRoot"] ?? "App_Data";
        var materialsDir = Path.Combine(storageRoot, "Users", userId, "Projects", projectId, "Materials");
        Directory.CreateDirectory(materialsDir);
        
        var filePath = Path.Combine(materialsDir, file.FileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Create material entity
        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            FileName = file.FileName,
            FilePath = filePath,
            Category = category,
            SourceType = "UserUpload",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Uploaded material {MaterialId} to project {ProjectId}", material.Id, projectId);

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

    private static MaterialResponse MapToResponse(Material material)
    {
        return new MaterialResponse(
            material.Id,
            material.ProjectId!,
            material.FileName,
            material.Category,
            material.Tags,
            material.VectorChunkCount,
            material.CreatedAt,
            material.UpdatedAt
        );
    }

    public async Task<MaterialResponse> CreateMaterialAsync(
        string projectId, CreateMaterialRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var material = new Material
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = projectId,
            FileName = request.FileName,
            Content = request.Content,
            Category = request.Category,
            SourceType = request.SourceType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);

        return MapToResponse(material);
    }

    public async Task<MaterialResponse> GetMaterialAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);
        
        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        return MapToResponse(material);
    }

    public async Task<MaterialContentResponse> GetMaterialContentAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);
        
        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        string content;
        if (!string.IsNullOrEmpty(material.FilePath))
        {
            content = await File.ReadAllTextAsync(material.FilePath, ct);
        }
        else
        {
            content = material.Content ?? string.Empty;
        }

        return new MaterialContentResponse(material.Id, content);
    }

    public async Task<MaterialResponse> UpdateMaterialAsync(
        string materialId, UpdateMaterialRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);
        
        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        if (request.Category != null)
            material.Category = request.Category;
        
        if (request.Tags != null)
            material.Tags = request.Tags;

        material.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MapToResponse(material);
    }

    public async Task DeleteMaterialAsync(string materialId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == materialId && m.UserId == userId, ct);
        
        if (material == null)
            throw new KeyNotFoundException($"Material {materialId} not found");

        // Delete file if exists
        if (!string.IsNullOrEmpty(material.FilePath) && File.Exists(material.FilePath))
        {
            File.Delete(material.FilePath);
        }

        _db.Materials.Remove(material);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Register MaterialService in Program.cs**

```csharp
builder.Services.AddScoped<IMaterialService, MaterialService>();
```

- [ ] **Step 5: Create MaterialsController**

```csharp
// Web/NovelAgentWeb/Controllers/MaterialsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Materials;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api/materials")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly IMaterialService _materialService;

    public MaterialsController(IMaterialService materialService)
    {
        _materialService = materialService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadMaterial(
        [FromQuery] string projectId,
        [FromQuery] string? category,
        IFormFile file,
        CancellationToken ct)
    {
        var material = await _materialService.UploadMaterialAsync(projectId, file, category, ct);
        return Ok(material);
    }

    [HttpPost]
    public async Task<IActionResult> CreateMaterial(
        [FromQuery] string projectId,
        [FromBody] CreateMaterialRequest request,
        CancellationToken ct)
    {
        var material = await _materialService.CreateMaterialAsync(projectId, request, ct);
        return Ok(material);
    }

    [HttpGet]
    public async Task<IActionResult> ListMaterials([FromQuery] string projectId, CancellationToken ct)
    {
        var materials = await _materialService.ListMaterialsAsync(projectId, ct);
        return Ok(materials);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMaterial(string id, CancellationToken ct)
    {
        var material = await _materialService.GetMaterialAsync(id, ct);
        return Ok(material);
    }

    [HttpGet("{id}/content")]
    public async Task<IActionResult> GetMaterialContent(string id, CancellationToken ct)
    {
        var content = await _materialService.GetMaterialContentAsync(id, ct);
        return Ok(content);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateMaterial(
        string id,
        [FromBody] UpdateMaterialRequest request,
        CancellationToken ct)
    {
        var material = await _materialService.UpdateMaterialAsync(id, request, ct);
        return Ok(material);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteMaterial(string id, CancellationToken ct)
    {
        await _materialService.DeleteMaterialAsync(id, ct);
        return NoContent();
    }
}
```

- [ ] **Step 6: Build and commit**

```bash
dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
git add Web/NovelAgentWeb/Controllers/MaterialsController.cs \
        Web/NovelAgentWeb/Services/Materials/ \
        Web/NovelAgentWeb/DTOs/MaterialDTOs.cs \
        Web/NovelAgentWeb/Program.cs
git commit -m "feat(api): add MaterialsController with CRUD operations"
```

---

## Task 2: MaterialsController - Analyze Endpoint with Vectorization

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Materials/IMaterialService.cs`
- Modify: `Web/NovelAgentWeb/Services/Materials/MaterialService.cs`
- Modify: `Web/NovelAgentWeb/Controllers/MaterialsController.cs`

- [ ] **Step 1: Add AnalyzeMaterial method to interface**

```csharp
// Add to IMaterialService
Task AnalyzeMaterialAsync(string materialId, CancellationToken ct = default);
```

// __CONTINUE_HERE__
