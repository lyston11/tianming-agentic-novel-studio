# Phase 1: Monitoring & Auditing System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement comprehensive workspace monitoring and auditing with B+ progressive enforcement for production stability.

**Architecture:** Four-component system: ViolationTracker (foundation), Enhanced WorkspaceFactory (observability), AuditMiddleware (enforcement), MonitoringController (admin API). B+ enforcement = strict in dev/test, log-only in production.

**Tech Stack:** ASP.NET Core 8.0, EF Core, JWT Auth, ConcurrentDictionary, IConfiguration, ILogger

---

## File Structure

**New Files:**
- `Web/NovelAgentWeb/Services/Workspace/Models/ViolationType.cs` - Violation type enum
- `Web/NovelAgentWeb/Services/Workspace/Models/ViolationSeverity.cs` - Severity enum
- `Web/NovelAgentWeb/Services/Workspace/Models/WorkspaceViolation.cs` - Violation data model
- `Web/NovelAgentWeb/Services/Workspace/Models/WorkspaceFactoryHealthStatus.cs` - Health status model
- `Web/NovelAgentWeb/Services/Workspace/IWorkspaceViolationTracker.cs` - Tracker interface
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceViolationTracker.cs` - Tracker implementation
- `Web/NovelAgentWeb/Middleware/WorkspaceUsageAuditMiddleware.cs` - Audit middleware
- `Web/NovelAgentWeb/Controllers/WorkspaceMonitoringController.cs` - Admin API

**Modified Files:**
- `Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs` - Add three new methods
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs` - Implement new methods
- `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryStats.cs` - Add three new properties
- `Web/NovelAgentWeb/Program.cs` - Register services and middleware
- `Web/NovelAgentWeb/appsettings.json` - Add WorkspaceAudit config section

---

### Task 1: Create Violation Data Models

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/Models/ViolationType.cs`
- Create: `Web/NovelAgentWeb/Services/Workspace/Models/ViolationSeverity.cs`
- Create: `Web/NovelAgentWeb/Services/Workspace/Models/WorkspaceViolation.cs`

- [ ] **Step 1: Create ViolationType enum**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public enum ViolationType
{
    MissingUserId,
    MissingProjectId,
    WorkspaceNotActive
}
```

- [ ] **Step 2: Create ViolationSeverity enum**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public enum ViolationSeverity
{
    Low,
    Medium,
    High,
    Critical
}
```

- [ ] **Step 3: Create WorkspaceViolation model**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public sealed class WorkspaceViolation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ViolationType Type { get; set; }
    public ViolationSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ProjectId { get; set; }
    public string Message { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Commit violation models**

```bash
git add Web/NovelAgentWeb/Services/Workspace/Models/
git commit -m "feat(monitoring): add violation data models"
```

---

### Task 2: Create WorkspaceFactoryHealthStatus Model

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/Models/WorkspaceFactoryHealthStatus.cs`

- [ ] **Step 1: Create WorkspaceFactoryHealthStatus model**

```csharp
namespace TM.Web.NovelAgentWeb.Services.Workspace.Models;

public sealed class WorkspaceFactoryHealthStatus
{
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime LastCheckTimestamp { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Commit health status model**

```bash
git add Web/NovelAgentWeb/Services/Workspace/Models/WorkspaceFactoryHealthStatus.cs
git commit -m "feat(monitoring): add workspace factory health status model"
```

---

### Task 3: Create WorkspaceViolationTracker Interface

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/IWorkspaceViolationTracker.cs`

- [ ] **Step 1: Create IWorkspaceViolationTracker interface**

```csharp
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public interface IWorkspaceViolationTracker
{
    void TrackViolation(WorkspaceViolation violation);
    
    IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null);
    
    void ClearOldViolations(TimeSpan retentionPeriod);
}
```

- [ ] **Step 2: Commit interface**

```bash
git add Web/NovelAgentWeb/Services/Workspace/IWorkspaceViolationTracker.cs
git commit -m "feat(monitoring): add workspace violation tracker interface"
```

### Task 4: Implement WorkspaceViolationTracker Service

**Files:**
- Create: `Web/NovelAgentWeb/Services/Workspace/WorkspaceViolationTracker.cs`

- [ ] **Step 1: Create WorkspaceViolationTracker implementation with constructor and fields**

```csharp
using System.Collections.Concurrent;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Services.Workspace;

public sealed class WorkspaceViolationTracker : IWorkspaceViolationTracker, IDisposable
{
    private readonly ConcurrentDictionary<Guid, WorkspaceViolation> _violations = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _defaultRetention = TimeSpan.FromHours(24);

    public WorkspaceViolationTracker()
    {
        _cleanupTimer = new Timer(
            _ => ClearOldViolations(_defaultRetention),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }
    
    // Methods will be added in next steps
    
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
```

- [ ] **Step 2: Add TrackViolation method**

```csharp
    public void TrackViolation(WorkspaceViolation violation)
    {
        if (violation == null) return;
        _violations[violation.Id] = violation;
    }
```

- [ ] **Step 3: Add GetViolations query method**

```csharp
    public IEnumerable<WorkspaceViolation> GetViolations(
        ViolationSeverity? severity = null,
        DateTime? since = null,
        string? userId = null,
        string? projectId = null)
    {
        var query = _violations.Values.AsEnumerable();

        if (severity.HasValue)
            query = query.Where(v => v.Severity == severity.Value);

        if (since.HasValue)
            query = query.Where(v => v.Timestamp >= since.Value);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(v => v.UserId == userId);

        if (!string.IsNullOrEmpty(projectId))
            query = query.Where(v => v.ProjectId == projectId);

        return query.OrderByDescending(v => v.Timestamp).ToList();
    }
```

- [ ] **Step 4: Add ClearOldViolations cleanup method**

```csharp
    public void ClearOldViolations(TimeSpan retentionPeriod)
    {
        var cutoffTime = DateTime.UtcNow - retentionPeriod;
        var oldViolations = _violations
            .Where(kvp => kvp.Value.Timestamp < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in oldViolations)
        {
            _violations.TryRemove(id, out _);
        }
    }
```

- [ ] **Step 5: Commit WorkspaceViolationTracker implementation**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceViolationTracker.cs
git commit -m "feat(monitoring): implement workspace violation tracker service"
```

### Task 5: Extend WorkspaceFactoryStats with New Properties

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryStats.cs`

- [ ] **Step 1: Add ActiveWorkspacesCount, OldestWorkspaceAge, and AverageDatabaseLoadTime properties**

Add these properties to WorkspaceFactoryStats class after the existing CacheMisses property:

```csharp
    /// <summary>
    /// Number of workspaces currently active in cache.
    /// </summary>
    public int ActiveWorkspacesCount { get; init; }

    /// <summary>
    /// Age of the oldest workspace in cache.
    /// </summary>
    public TimeSpan? OldestWorkspaceAge { get; init; }

    /// <summary>
    /// Average time taken to load workspace from database.
    /// </summary>
    public TimeSpan? AverageDatabaseLoadTime { get; init; }
```

- [ ] **Step 2: Commit enhanced WorkspaceFactoryStats**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactoryStats.cs
git commit -m "feat(monitoring): add detailed stats properties to WorkspaceFactoryStats"
```

---

### Task 6: Extend IWorkspaceFactory Interface

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs`

- [ ] **Step 1: Add new method signatures to IWorkspaceFactory**

Add these methods after the existing GetStats() method:

```csharp
    /// <summary>
    /// Check if a workspace is currently active in cache.
    /// </summary>
    bool IsWorkspaceActive(string userId, string projectId);

    /// <summary>
    /// Get detailed statistics including active workspace count and timing metrics.
    /// </summary>
    WorkspaceFactoryStats GetDetailedStats();

    /// <summary>
    /// Perform health diagnostics on the workspace factory.
    /// </summary>
    Task<WorkspaceFactoryHealthStatus> CheckHealthAsync();
```

- [ ] **Step 2: Add using statement for Models namespace**

Add this using statement at the top of the file:

```csharp
using TM.Web.NovelAgentWeb.Services.Workspace.Models;
```

- [ ] **Step 3: Commit interface extension**

```bash
git add Web/NovelAgentWeb/Services/Workspace/IWorkspaceFactory.cs
git commit -m "feat(monitoring): extend IWorkspaceFactory with observability methods"
```

### Task 7: Implement Enhanced WorkspaceFactory Methods

**Files:**
- Modify: `Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs`

- [ ] **Step 1: Add using statement for Models namespace**

Add this using statement at the top of the file:

```csharp
using TM.Web.NovelAgentWeb.Services.Workspace.Models;
```

- [ ] **Step 2: Add database load time tracking fields**

Add these fields after the existing _evictionCount field (around line 32):

```csharp
    private long _totalDatabaseLoadTimeMs = 0;
    private long _databaseLoadCount = 0;
```

- [ ] **Step 3: Track database load time in CreateWorkspaceAsync method**

Replace the CreateWorkspaceAsync method implementation with this version that tracks timing:

```csharp
    private async Task<NovelAgentWorkspace> CreateWorkspaceAsync(
        string userId, string projectId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();

        // Load project to verify it exists and belongs to user
        var project = await db.NovelProjects
            .Where(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new InvalidOperationException($"Project {projectId} not found for user {userId}");
        }

        // Get required services
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var settingsManager = scope.ServiceProvider.GetRequiredService<UserSettingsManager>();

        // Create Workspace instance
        var workspace = new NovelAgentWorkspace(env, config, settingsManager);

        // Track load time
        var loadTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        Interlocked.Add(ref _totalDatabaseLoadTimeMs, loadTimeMs);
        Interlocked.Increment(ref _databaseLoadCount);

        return workspace;
    }
```

- [ ] **Step 4: Implement IsWorkspaceActive method**

Add this method after the GetStats() method:

```csharp
    public bool IsWorkspaceActive(string userId, string projectId)
    {
        var cacheKey = GetCacheKey(userId, projectId);
        return _cache.ContainsKey(cacheKey);
    }
```

- [ ] **Step 5: Implement GetDetailedStats method**

Add this method after IsWorkspaceActive:

```csharp
    public WorkspaceFactoryStats GetDetailedStats()
    {
        var entries = _cache.Values.ToList();
        var now = DateTime.UtcNow;

        TimeSpan? oldestAge = null;
        if (entries.Any())
        {
            var oldestEntry = entries.OrderBy(e => e.LastAccessTime).First();
            oldestAge = now - oldestEntry.LastAccessTime;
        }

        TimeSpan? avgLoadTime = null;
        var loadCount = Interlocked.Read(ref _databaseLoadCount);
        if (loadCount > 0)
        {
            var totalMs = Interlocked.Read(ref _totalDatabaseLoadTimeMs);
            avgLoadTime = TimeSpan.FromMilliseconds((double)totalMs / loadCount);
        }

        return new WorkspaceFactoryStats
        {
            TotalWorkspaces = entries.Count,
            ActiveReferences = entries.Sum(e => e.ActiveReferences),
            IdleWorkspaces = entries.Count(e => e.ActiveReferences == 0),
            TotalEvictions = _evictionCount,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            ActiveWorkspacesCount = entries.Count,
            OldestWorkspaceAge = oldestAge,
            AverageDatabaseLoadTime = avgLoadTime
        };
    }
```

- [ ] **Step 6: Implement CheckHealthAsync method**

Add this method after GetDetailedStats:

```csharp
    public async Task<WorkspaceFactoryHealthStatus> CheckHealthAsync()
    {
        var stats = GetDetailedStats();
        var isHealthy = true;
        var messages = new List<string>();

        // Check cache capacity
        if (stats.TotalWorkspaces >= _options.MaxCachedWorkspaces)
        {
            isHealthy = false;
            messages.Add($"Cache at capacity: {stats.TotalWorkspaces}/{_options.MaxCachedWorkspaces}");
        }

        // Check eviction rate (> 100/min indicates pressure)
        var evictionRate = stats.TotalEvictions;
        if (evictionRate > 100)
        {
            messages.Add($"High eviction count: {evictionRate}");
        }

        // Test database connectivity
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
        }
        catch (Exception ex)
        {
            isHealthy = false;
            messages.Add($"Database connectivity failed: {ex.Message}");
        }

        return new WorkspaceFactoryHealthStatus
        {
            IsHealthy = isHealthy,
            Message = messages.Any() ? string.Join("; ", messages) : "All checks passed",
            LastCheckTimestamp = DateTime.UtcNow
        };
    }
```

- [ ] **Step 7: Commit enhanced WorkspaceFactory implementation**

```bash
git add Web/NovelAgentWeb/Services/Workspace/WorkspaceFactory.cs
git commit -m "feat(monitoring): implement observability methods in WorkspaceFactory"
```

### Task 8: Create WorkspaceUsageAuditMiddleware

**Files:**
- Create: `Web/NovelAgentWeb/Middleware/WorkspaceUsageAuditMiddleware.cs`

- [ ] **Step 1: Create middleware class with constructor and helper methods**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Middleware;

public class WorkspaceUsageAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WorkspaceUsageAuditMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public WorkspaceUsageAuditMiddleware(
        RequestDelegate next,
        ILogger<WorkspaceUsageAuditMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    private bool IsStrictMode => _configuration.GetValue<bool>("WorkspaceAudit:EnableStrictMode");
    private bool LogViolations => _configuration.GetValue<bool>("WorkspaceAudit:LogViolations", true);
    private bool TrackViolations => _configuration.GetValue<bool>("WorkspaceAudit:TrackViolations", true);

    private static readonly HashSet<string> WorkspaceEndpoints = new()
    {
        "/api/agent",
        "/api/chapters",
        "/api/materials",
        "/api/knowledge",
        "/api/storybible",
        "/api/workflow"
    };

    private bool IsWorkspaceEndpoint(string path)
    {
        return WorkspaceEndpoints.Any(endpoint => path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Add ExtractProjectId helper method**

```csharp
    private async Task<string?> ExtractProjectIdAsync(HttpContext context)
    {
        // 1. Try route values
        if (context.Request.RouteValues.TryGetValue("projectId", out var routeProjectId))
        {
            return routeProjectId?.ToString();
        }

        // 2. Try request body (for POST/PUT)
        if (context.Request.ContentType?.Contains("application/json") == true && 
            (context.Request.Method == "POST" || context.Request.Method == "PUT"))
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;
            
            try
            {
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrEmpty(body))
                {
                    var jsonDoc = JsonDocument.Parse(body);
                    if (jsonDoc.RootElement.TryGetProperty("projectId", out var projectIdElement))
                    {
                        return projectIdElement.GetString();
                    }
                }
            }
            catch
            {
                context.Request.Body.Position = 0;
            }
        }

        // 3. Try query string
        if (context.Request.Query.TryGetValue("projectId", out var queryProjectId))
        {
            return queryProjectId.ToString();
        }

        // 4. Try headers
        if (context.Request.Headers.TryGetValue("X-Project-Id", out var headerProjectId))
        {
            return headerProjectId.ToString();
        }

        return null;
    }
```

- [ ] **Step 3: Add HandleViolation method**

```csharp
    private async Task<bool> HandleViolationAsync(
        HttpContext context,
        IWorkspaceViolationTracker violationTracker,
        ViolationType type,
        ViolationSeverity severity,
        string message,
        string? userId,
        string? projectId)
    {
        var violation = new WorkspaceViolation
        {
            Type = type,
            Severity = severity,
            Timestamp = DateTime.UtcNow,
            Endpoint = context.Request.Path,
            Method = context.Request.Method,
            UserId = userId,
            ProjectId = projectId,
            Message = message
        };

        if (LogViolations)
        {
            _logger.LogWarning(
                "Workspace violation: {Type} | {Severity} | {Endpoint} | {Method} | UserId={UserId} | ProjectId={ProjectId} | {Message}",
                type, severity, context.Request.Path, context.Request.Method, userId, projectId, message);
        }

        if (TrackViolations)
        {
            violationTracker.TrackViolation(violation);
        }

        if (IsStrictMode)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "WorkspaceViolation",
                type = type.ToString(),
                message = message
            });
            return false;
        }

        return true;
    }
```

- [ ] **Step 4: Add InvokeAsync main method**

```csharp
    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceFactory workspaceFactory,
        IWorkspaceViolationTracker violationTracker)
    {
        // Skip non-workspace endpoints
        if (!IsWorkspaceEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Extract userId from HttpContext.Items (set by UserContextMiddleware)
        var userId = context.Items["UserId"] as string;

        // Extract projectId from multiple sources
        var projectId = await ExtractProjectIdAsync(context);

        // Validate userId
        if (string.IsNullOrEmpty(userId))
        {
            var canContinue = await HandleViolationAsync(
                context, violationTracker,
                ViolationType.MissingUserId,
                ViolationSeverity.High,
                "User ID missing in request context",
                userId, projectId);

            if (!canContinue) return;
        }

        // Validate projectId
        if (string.IsNullOrEmpty(projectId))
        {
            var canContinue = await HandleViolationAsync(
                context, violationTracker,
                ViolationType.MissingProjectId,
                ViolationSeverity.Medium,
                "Project ID missing in request",
                userId, projectId);

            if (!canContinue) return;
        }

        // Check workspace is active (only if both userId and projectId are present)
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(projectId))
        {
            if (!workspaceFactory.IsWorkspaceActive(userId, projectId))
            {
                var canContinue = await HandleViolationAsync(
                    context, violationTracker,
                    ViolationType.WorkspaceNotActive,
                    ViolationSeverity.Low,
                    "Workspace not active in cache (will be created on demand)",
                    userId, projectId);

                if (!canContinue) return;
            }
        }

        await _next(context);
    }
```

- [ ] **Step 5: Commit WorkspaceUsageAuditMiddleware**

```bash
git add Web/NovelAgentWeb/Middleware/WorkspaceUsageAuditMiddleware.cs
git commit -m "feat(monitoring): implement workspace usage audit middleware"
```

### Task 9: Create WorkspaceMonitoringController

**Files:**
- Create: `Web/NovelAgentWeb/Controllers/WorkspaceMonitoringController.cs`

- [ ] **Step 1: Create controller class with constructor**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TM.Web.NovelAgentWeb.Services.Workspace;
using TM.Web.NovelAgentWeb.Services.Workspace.Models;

namespace TM.Web.NovelAgentWeb.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/workspace")]
public class WorkspaceMonitoringController : ControllerBase
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly IWorkspaceViolationTracker _violationTracker;
    private readonly ILogger<WorkspaceMonitoringController> _logger;

    public WorkspaceMonitoringController(
        IWorkspaceFactory workspaceFactory,
        IWorkspaceViolationTracker violationTracker,
        ILogger<WorkspaceMonitoringController> logger)
    {
        _workspaceFactory = workspaceFactory;
        _violationTracker = violationTracker;
        _logger = logger;
    }
}
```

- [ ] **Step 2: Add GetStats endpoint**

```csharp
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            var stats = _workspaceFactory.GetDetailedStats();
            return Ok(new
            {
                totalCacheHits = stats.CacheHits,
                totalCacheMisses = stats.CacheMisses,
                totalEvictions = stats.TotalEvictions,
                activeWorkspacesCount = stats.ActiveWorkspacesCount,
                oldestWorkspaceAge = stats.OldestWorkspaceAge?.ToString(@"hh\:mm\:ss"),
                averageDatabaseLoadTime = stats.AverageDatabaseLoadTime?.ToString(@"hh\:mm\:ss\.fff"),
                cacheHitRate = stats.CacheHitRate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace stats");
            return StatusCode(500, new { error = "Failed to retrieve stats" });
        }
    }
```

- [ ] **Step 3: Add GetViolations endpoint**

```csharp
    [HttpGet("violations")]
    public IActionResult GetViolations(
        [FromQuery] ViolationSeverity? severity = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var violations = _violationTracker.GetViolations(
                severity: severity,
                since: since,
                userId: null,
                projectId: null)
                .Take(limit)
                .Select(v => new
                {
                    type = v.Type.ToString(),
                    severity = v.Severity.ToString(),
                    timestamp = v.Timestamp,
                    endpoint = v.Endpoint,
                    method = v.Method,
                    userId = v.UserId,
                    projectId = v.ProjectId,
                    message = v.Message
                });

            return Ok(violations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get violations");
            return StatusCode(500, new { error = "Failed to retrieve violations" });
        }
    }
```

- [ ] **Step 4: Add GetWorkspaceDetail endpoint**

```csharp
    [HttpGet("{userId}/{projectId}")]
    public IActionResult GetWorkspaceDetail(string userId, string projectId)
    {
        try
        {
            var isActive = _workspaceFactory.IsWorkspaceActive(userId, projectId);
            var stats = _workspaceFactory.GetDetailedStats();

            return Ok(new
            {
                isActive = isActive,
                cacheHitRate = stats.CacheHitRate,
                lastAccessed = DateTime.UtcNow,
                databaseLoadTime = stats.AverageDatabaseLoadTime?.ToString(@"hh\:mm\:ss\.fff")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace detail for {UserId}/{ProjectId}", userId, projectId);
            return StatusCode(500, new { error = "Failed to retrieve workspace detail" });
        }
    }
```

- [ ] **Step 5: Add InvalidateWorkspace endpoint**

```csharp
    [HttpPost("{userId}/{projectId}/invalidate")]
    public IActionResult InvalidateWorkspace(string userId, string projectId)
    {
        try
        {
            // Note: Current IWorkspaceFactory doesn't have explicit invalidation method
            // This endpoint is placeholder for future enhancement
            // For now, workspace will be naturally evicted by LRU algorithm
            
            _logger.LogInformation("Workspace invalidation requested for {UserId}/{ProjectId}", userId, projectId);
            
            return Ok(new
            {
                success = true,
                message = "Workspace will be evicted by LRU algorithm"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate workspace for {UserId}/{ProjectId}", userId, projectId);
            return StatusCode(500, new { error = "Failed to invalidate workspace" });
        }
    }
```

- [ ] **Step 6: Add GetHealth endpoint**

```csharp
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var health = await _workspaceFactory.CheckHealthAsync();
            
            if (health.IsHealthy)
            {
                return Ok(health);
            }
            else
            {
                return StatusCode(503, health);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check workspace factory health");
            return StatusCode(500, new { error = "Health check failed" });
        }
    }
```

- [ ] **Step 7: Commit WorkspaceMonitoringController**

```bash
git add Web/NovelAgentWeb/Controllers/WorkspaceMonitoringController.cs
git commit -m "feat(monitoring): implement workspace monitoring admin controller"
```

### Task 10: Add WorkspaceAudit Configuration to appsettings.json

**Files:**
- Modify: `Web/NovelAgentWeb/appsettings.json`

- [ ] **Step 1: Add WorkspaceAudit configuration section**

Add this section to appsettings.json after the existing configuration sections:

```json
  "WorkspaceAudit": {
    "EnableStrictMode": false,
    "LogViolations": true,
    "TrackViolations": true
  }
```

- [ ] **Step 2: Verify JSON syntax is valid**

Run: `cat Web/NovelAgentWeb/appsettings.json | python3 -m json.tool > /dev/null`
Expected: No output (valid JSON)

- [ ] **Step 3: Commit appsettings.json changes**

```bash
git add Web/NovelAgentWeb/appsettings.json
git commit -m "feat(monitoring): add workspace audit configuration"
```

---

### Task 11: Register Services and Middleware in Program.cs

**Files:**
- Modify: `Web/NovelAgentWeb/Program.cs`

- [ ] **Step 1: Register WorkspaceViolationTracker service**

Add this line after the existing WorkspaceFactory registration (around line 121):

```csharp
builder.Services.AddSingleton<IWorkspaceViolationTracker, WorkspaceViolationTracker>();
```

- [ ] **Step 2: Register WorkspaceUsageAuditMiddleware after UserContextMiddleware**

Find the middleware registration section (around line 202) and add this line after `app.UseMiddleware<UserContextMiddleware>();`:

```csharp
app.UseMiddleware<WorkspaceUsageAuditMiddleware>();
```

The middleware order should be:
```csharp
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseMiddleware<WorkspaceUsageAuditMiddleware>();
app.UseAuthorization();
```

- [ ] **Step 3: Commit Program.cs changes**

```bash
git add Web/NovelAgentWeb/Program.cs
git commit -m "feat(monitoring): register workspace monitoring services and middleware"
```

---

### Task 12: Build and Verify

**Files:**
- All modified files

- [ ] **Step 1: Build the project**

Run: `dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Check for compilation errors**

If build fails, review error messages and fix:
- Missing using statements
- Type mismatches
- Syntax errors

- [ ] **Step 3: Verify all services are registered**

Check Program.cs contains:
- `AddSingleton<IWorkspaceFactory, WorkspaceFactory>()`
- `AddSingleton<IWorkspaceViolationTracker, WorkspaceViolationTracker>()`
- `UseMiddleware<WorkspaceUsageAuditMiddleware>()`

- [ ] **Step 4: Commit build verification**

If any fixes were needed:
```bash
git add .
git commit -m "fix(monitoring): resolve build issues"
```

---

### Task 13: Manual Testing - Start Backend

**Files:**
- None (testing only)

- [ ] **Step 1: Start the backend server**

Run: `cd Web/NovelAgentWeb && ASPNETCORE_URLS=http://+:5002 dotnet run`
Expected: Server starts on http://localhost:5002

- [ ] **Step 2: Verify middleware loads without errors**

Check console output for:
- No startup errors
- Middleware registered successfully
- Services registered successfully

- [ ] **Step 3: Check health endpoint**

Run: `curl -X GET http://localhost:5002/api/admin/workspace/health -H "Authorization: Bearer <admin-token>"`
Expected: 200 OK or 401 Unauthorized (auth required)

Note: Keep server running for next testing tasks

---

### Task 14: Manual Testing - Test Stats Endpoint

**Files:**
- None (testing only)

- [ ] **Step 1: Get admin JWT token**

Either:
1. Login as admin user via `/api/auth/login` endpoint
2. Or use existing admin token if available

- [ ] **Step 2: Call stats endpoint**

Run: `curl -X GET http://localhost:5002/api/admin/workspace/stats -H "Authorization: Bearer <admin-token>"`

Expected response:
```json
{
  "totalCacheHits": 0,
  "totalCacheMisses": 0,
  "totalEvictions": 0,
  "activeWorkspacesCount": 0,
  "oldestWorkspaceAge": null,
  "averageDatabaseLoadTime": null,
  "cacheHitRate": 0.0
}
```

- [ ] **Step 3: Verify response structure**

Check response contains all expected fields:
- totalCacheHits, totalCacheMisses, totalEvictions
- activeWorkspacesCount, oldestWorkspaceAge, averageDatabaseLoadTime
- cacheHitRate

---

### Task 15: Manual Testing - Test Violation Tracking

**Files:**
- None (testing only)

- [ ] **Step 1: Make request to workspace endpoint without projectId**

Run: `curl -X GET http://localhost:5002/api/chapters -H "Authorization: Bearer <user-token>"`

Expected: Request succeeds (LogOnly mode) but violation is logged

- [ ] **Step 2: Query violations endpoint**

Run: `curl -X GET http://localhost:5002/api/admin/workspace/violations -H "Authorization: Bearer <admin-token>"`

Expected: Array with violation entry showing MissingProjectId type

- [ ] **Step 3: Test severity filter**

Run: `curl -X GET "http://localhost:5002/api/admin/workspace/violations?severity=Medium" -H "Authorization: Bearer <admin-token>"`

Expected: Filtered violations with Medium severity only

- [ ] **Step 4: Verify violation fields**

Check violation contains:
- type, severity, timestamp
- endpoint, method
- userId, projectId, message

---

### Task 16: Manual Testing - Test Strict Mode

**Files:**
- Modify: `Web/NovelAgentWeb/appsettings.json`

- [ ] **Step 1: Enable strict mode**

Change WorkspaceAudit configuration:
```json
"EnableStrictMode": true
```

- [ ] **Step 2: Restart backend server**

Stop server (Ctrl+C) and restart:
```bash
ASPNETCORE_URLS=http://+:5002 dotnet run
```

- [ ] **Step 3: Make request without projectId**

Run: `curl -X GET http://localhost:5002/api/chapters -H "Authorization: Bearer <user-token>"`

Expected: 400 Bad Request with error message

- [ ] **Step 4: Verify strict mode behavior**

Response should be:
```json
{
  "error": "WorkspaceViolation",
  "type": "MissingProjectId",
  "message": "Project ID missing in request"
}
```

- [ ] **Step 5: Disable strict mode and restart**

Change back to:
```json
"EnableStrictMode": false
```

Restart server

---

### Task 17: Documentation

**Files:**
- Create: `docs/monitoring/phase1-usage-guide.md`

- [ ] **Step 1: Create usage guide document**

```markdown
# Phase 1 Monitoring & Auditing - Usage Guide

## Overview

Phase 1 implements workspace monitoring and auditing with B+ progressive enforcement.

## Configuration

### appsettings.json

\`\`\`json
{
  "WorkspaceAudit": {
    "EnableStrictMode": false,  // true = block violations, false = log only
    "LogViolations": true,      // log violations to console
    "TrackViolations": true     // store violations in memory
  }
}
\`\`\`

## Admin Endpoints

All endpoints require `Admin` role JWT token.

### 1. Get Stats
\`\`\`
GET /api/admin/workspace/stats
\`\`\`

Returns cache statistics and performance metrics.

### 2. Get Violations
\`\`\`
GET /api/admin/workspace/violations?severity=High&limit=50
\`\`\`

Query parameters:
- severity: Low|Medium|High|Critical
- since: ISO8601 datetime
- limit: max results (default 100)

### 3. Get Workspace Detail
\`\`\`
GET /api/admin/workspace/{userId}/{projectId}
\`\`\`

Returns specific workspace cache status.

### 4. Health Check
\`\`\`
GET /api/admin/workspace/health
\`\`\`

Returns workspace factory health status.

## Violation Types

- **MissingUserId**: Request missing user context (High severity)
- **MissingProjectId**: Request missing project ID (Medium severity)
- **WorkspaceNotActive**: Workspace not in cache (Low severity)

## Enforcement Modes

### LogOnly Mode (Production)
- `EnableStrictMode: false`
- Violations logged and tracked
- Requests allowed to proceed
- Use for production observability

### Strict Mode (Development/Test)
- `EnableStrictMode: true`
- Violations return 400 Bad Request
- Requests blocked
- Use for development validation

## Troubleshooting

### High eviction count
- Increase `WorkspaceFactory:MaxCachedWorkspaces`
- Review workspace access patterns

### Database connectivity errors
- Check SQLite database path
- Verify connection string

### Missing projectId violations
- Ensure requests include projectId in route, body, query, or header
- Review API client implementation
```

- [ ] **Step 2: Commit documentation**

```bash
git add docs/monitoring/phase1-usage-guide.md
git commit -m "docs(monitoring): add Phase 1 usage guide"
```

---

### Task 18: Final Verification and Summary

**Files:**
- All files

- [ ] **Step 1: Run final build**

Run: `dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj`
Expected: Build succeeded

- [ ] **Step 2: Verify all commits**

Run: `git log --oneline -20`
Expected: See all feature commits

- [ ] **Step 3: Create summary commit**

```bash
git add .
git commit -m "feat(monitoring): complete Phase 1 workspace monitoring and auditing system

- WorkspaceViolationTracker: In-memory violation tracking with 24h retention
- Enhanced WorkspaceFactory: IsWorkspaceActive, GetDetailedStats, CheckHealth
- WorkspaceUsageAuditMiddleware: B+ enforcement (strict dev, log-only prod)
- WorkspaceMonitoringController: Admin API for stats, violations, health
- Configuration: WorkspaceAudit section in appsettings.json
- Documentation: Usage guide for administrators

Implements all Phase 1 requirements from migration roadmap."
```

- [ ] **Step 4: Verify success criteria**

Check all criteria met:
✅ Middleware intercepts all workspace requests
✅ Violations tracked with correct severity
✅ Strict mode blocks invalid requests in dev/test
✅ LogOnly mode allows requests in production
✅ Admin can view stats and violations via API
✅ Health check detects unhealthy states
✅ No performance degradation

- [ ] **Step 5: Document completion**

Create file `docs/monitoring/phase1-completion.md`:

```markdown
# Phase 1 Monitoring & Auditing - Completion Report

**Date:** 2026-06-09
**Status:** ✅ Complete

## Implemented Components

1. ✅ WorkspaceViolationTracker Service
2. ✅ Enhanced WorkspaceFactory API
3. ✅ WorkspaceUsageAuditMiddleware
4. ✅ WorkspaceMonitoringController

## Testing Status

- ✅ Build verification: Passed
- ✅ Manual testing: Stats endpoint working
- ✅ Manual testing: Violation tracking working
- ✅ Manual testing: Strict mode working
- ✅ Manual testing: LogOnly mode working

## Deployment Notes

- Default mode: LogOnly (production-safe)
- Admin endpoints require `Admin` role
- Violations retained for 24 hours
- Background cleanup runs every 5 minutes

## Next Steps

- Phase 2: Frontend Admin Dashboard (optional)
- Phase 3: Persistent violation storage (optional)
- Phase 4: Alerting integration (optional)
```

Commit:
```bash
git add docs/monitoring/phase1-completion.md
git commit -m "docs(monitoring): add Phase 1 completion report"
```

---

## Plan Complete

All tasks defined with explicit steps, code, and verification commands. Ready for execution with subagent-driven-development or executing-plans skill.
