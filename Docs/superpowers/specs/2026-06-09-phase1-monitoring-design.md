# Phase 1: Monitoring & Auditing System Design

**Date:** 2026-06-09  
**Status:** Approved  
**Implementation Target:** Phase 1 of Migration Roadmap

## Overview

This design implements comprehensive monitoring and auditing for the Workspace system with B+ progressive enforcement: strict validation in development/test environments, logging-only mode in production to maintain stability.

## Goals

1. **Audit workspace access patterns** with detailed request context (userId, projectId, endpoint, method)
2. **Track violations** with configurable enforcement modes and severity levels
3. **Expose monitoring endpoints** for administrators to view stats, violations, and health status
4. **Enable centralized violation management** with flexible querying and cleanup

## Architecture

```
HTTP Request
    ↓
WorkspaceUsageAuditMiddleware (NEW)
    ├─ Extract: userId, projectId, endpoint, method
    ├─ Validate: userId present, workspace active
    ├─ Enforce: B+ mode (Strict vs LogOnly)
    └─ Log: structured logs + ViolationTracker
    ↓
WorkspaceFactory (ENHANCED)
    ├─ New: IsWorkspaceActive(userId, projectId)
    ├─ New: GetDetailedStats()
    └─ New: CheckHealth()
    ↓
WorkspaceMonitoringController (NEW)
    ├─ GET /api/admin/workspace/stats
    ├─ GET /api/admin/workspace/violations
    ├─ GET /api/admin/workspace/{userId}/{projectId}
    └─ POST /api/admin/workspace/{userId}/{projectId}/invalidate
    ↓
WorkspaceViolationTracker (NEW)
    ├─ Track violations in memory (ConcurrentDictionary)
    ├─ Query by severity, timeRange, userId, projectId
    └─ Cleanup old violations
```

## Component 1: WorkspaceUsageAuditMiddleware

**Purpose:** Intercept all workspace-related requests, validate context, and enforce audit rules.

**Key Features:**
- **Multi-source ProjectId extraction**: Route → Body → Query → Header
- **B+ Progressive Enforcement**:
  - `Development/Test`: Strict mode - return 400 on violations
  - `Production`: LogOnly mode - log violations but allow requests
- **Violation Types**: MissingUserId, MissingProjectId, WorkspaceNotActive
- **Integration**: Uses IWorkspaceFactory for validation, IWorkspaceViolationTracker for recording

**Configuration:**
```json
{
  "WorkspaceAudit": {
    "EnableStrictMode": false,  // false = LogOnly in production
    "LogViolations": true,
    "TrackViolations": true
  }
}
```

**Code Structure:**
```csharp
public class WorkspaceUsageAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WorkspaceUsageAuditMiddleware> _logger;
    private readonly IConfiguration _configuration;
    
    // Enforcement mode determined by environment + config
    private bool IsStrictMode => _configuration.GetValue<bool>("WorkspaceAudit:EnableStrictMode");
    
    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceFactory workspaceFactory,
        IWorkspaceViolationTracker violationTracker)
    {
        // 1. Extract context (userId, projectId, endpoint)
        // 2. Validate (check userId present, workspace active)
        // 3. On violation:
        //    - Log structured message
        //    - Track in ViolationTracker
        //    - Strict mode: return 400 BadRequest
        //    - LogOnly mode: allow request to proceed
        // 4. Call _next(context)
    }
}
```

## Component 2: Enhanced WorkspaceFactory API

**Purpose:** Extend IWorkspaceFactory with observability and health check methods.

**New Methods:**

1. **IsWorkspaceActive(userId, projectId)**: Check if workspace exists in cache
   - Returns: `bool`
   - Use case: Middleware validation before expensive operations

2. **GetDetailedStats()**: Return comprehensive metrics
   - Returns: `WorkspaceFactoryStats` object with:
     - TotalCacheHits, TotalCacheMisses, TotalEvictions (existing)
     - ActiveWorkspacesCount (new)
     - OldestWorkspaceAge (new)
     - AverageDatabaseLoadTime (new)
   - Use case: Admin dashboard display

3. **CheckHealth()**: Perform health diagnostics
   - Returns: `WorkspaceFactoryHealthStatus` object with:
     - IsHealthy (bool)
     - Message (string)
     - LastCheckTimestamp (DateTime)
   - Checks:
     - Cache capacity not exceeded
     - No excessive evictions (> 100/min indicates pressure)
     - Database connectivity test (simple query)
   - Use case: Distributed system monitoring

**Interface Extension:**
```csharp
public interface IWorkspaceFactory
{
    // Existing methods...
    Task<Workspace> GetOrCreateWorkspaceAsync(string userId, string projectId);
    WorkspaceFactoryStats GetStats();
    
    // New methods
    bool IsWorkspaceActive(string userId, string projectId);
    WorkspaceFactoryStats GetDetailedStats();
    Task<WorkspaceFactoryHealthStatus> CheckHealthAsync();
}
```

## Component 3: WorkspaceMonitoringController

**Purpose:** Admin API for monitoring workspace system health and violations.

**Authentication:** Requires JWT with `Admin` role.

**Endpoints:**

### 3.1 Stats Endpoint
```
GET /api/admin/workspace/stats
Response: {
  totalCacheHits: 1234,
  totalCacheMisses: 56,
  totalEvictions: 12,
  activeWorkspacesCount: 45,
  oldestWorkspaceAge: "00:15:32",
  averageDatabaseLoadTime: "00:00:00.123"
}
```

### 3.2 Violations Endpoint
```
GET /api/admin/workspace/violations
Query Params:
  - severity: Low|Medium|High|Critical (optional)
  - since: ISO8601 datetime (optional)
  - limit: int (default 100)
Response: [
  {
    type: "MissingUserId",
    severity: "High",
    timestamp: "2026-06-09T10:30:00Z",
    endpoint: "/api/agent/chat",
    method: "POST",
    userId: null,
    projectId: "abc123",
    message: "User ID missing in request context"
  }
]
```

### 3.3 Workspace Detail Endpoint
```
GET /api/admin/workspace/{userId}/{projectId}
Response: {
  isActive: true,
  cacheHitRate: 0.95,
  lastAccessed: "2026-06-09T10:25:00Z",
  databaseLoadTime: "00:00:00.087"
}
```

### 3.4 Invalidation Endpoint
```
POST /api/admin/workspace/{userId}/{projectId}/invalidate
Response: {
  success: true,
  message: "Workspace invalidated successfully"
}
```

**Controller Structure:**
```csharp
[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin/workspace")]
public class WorkspaceMonitoringController : ControllerBase
{
    private readonly IWorkspaceFactory _workspaceFactory;
    private readonly IWorkspaceViolationTracker _violationTracker;
    private readonly ILogger<WorkspaceMonitoringController> _logger;
    
    // Four action methods for the endpoints above
}
```

## Component 4: WorkspaceViolationTracker Service

**Purpose:** Centralized violation tracking with in-memory storage and flexible querying.

**Key Features:**
- **Thread-safe**: ConcurrentDictionary for concurrent request handling
- **Time-based retention**: Auto-cleanup of violations older than 24 hours
- **Query flexibility**: Filter by severity, timeRange, userId, projectId
- **Severity levels**: Low, Medium, High, Critical

**Service Interface:**
```csharp
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

**Data Model:**
```csharp
public class WorkspaceViolation
{
    public ViolationType Type { get; set; }
    public ViolationSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; }
    public string Endpoint { get; set; }
    public string Method { get; set; }
    public string? UserId { get; set; }
    public string? ProjectId { get; set; }
    public string Message { get; set; }
}

public enum ViolationType
{
    MissingUserId,
    MissingProjectId,
    WorkspaceNotActive
}

public enum ViolationSeverity
{
    Low,      // Informational
    Medium,   // Warning
    High,     // Error
    Critical  // System failure
}
```

**Implementation Notes:**
- Violations stored in `ConcurrentDictionary<Guid, WorkspaceViolation>`
- Background task runs every 5 minutes to cleanup old violations
- Default retention: 24 hours

## Dependency Injection Registration

**File:** `Web/NovelAgentWeb/Program.cs`

```csharp
// Workspace services
builder.Services.AddSingleton<IWorkspaceFactory, WorkspaceFactory>();
builder.Services.AddSingleton<IWorkspaceViolationTracker, WorkspaceViolationTracker>();

// Middleware registration (order matters!)
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UserContextMiddleware>();  // Existing
app.UseMiddleware<WorkspaceUsageAuditMiddleware>();  // NEW - after auth
```

## Testing Strategy

### Unit Tests
1. **WorkspaceUsageAuditMiddleware**:
   - ProjectId extraction from all sources (route, body, query, header)
   - Strict mode: returns 400 on violations
   - LogOnly mode: allows request to proceed
   - ViolationTracker called correctly

2. **WorkspaceFactory extensions**:
   - IsWorkspaceActive() returns correct cache status
   - GetDetailedStats() calculates metrics accurately
   - CheckHealthAsync() detects unhealthy states

3. **WorkspaceViolationTracker**:
   - Thread-safe concurrent tracking
   - Query filters work correctly
   - Old violations cleaned up

### Integration Tests
1. **End-to-end flow**:
   - Make request → middleware validates → factory checks → violation tracked
   - Admin queries violations → correct results returned

2. **Health check**:
   - CheckHealthAsync() detects database connectivity issues

### Manual Testing
1. **Admin dashboard**:
   - View stats in browser
   - Query violations with different filters
   - Invalidate workspace and verify cache cleared

## Performance Considerations

1. **Middleware overhead**: < 1ms per request (fast in-memory operations)
2. **ViolationTracker memory**: ~1KB per violation, 24-hour retention = ~100MB max for 100K violations
3. **Cache health check**: Rate-limited to once per minute to avoid overhead
4. **Database health check**: Simple SELECT 1 query, < 10ms

## Security

1. **Admin-only access**: All monitoring endpoints require `Admin` role
2. **No sensitive data exposure**: Violations log userId/projectId (not passwords/tokens)
3. **Rate limiting**: Consider adding rate limiter to admin endpoints in future

## Future Enhancements (Out of Scope for Phase 1)

1. **Persistent violation storage**: Write to database for long-term analytics
2. **Alerting integration**: Send critical violations to Slack/email
3. **Metrics export**: Prometheus/Grafana integration
4. **Rate limiting**: Per-user request quotas
5. **WebSocket notifications**: Real-time violation alerts to admin dashboard

## Success Criteria

✅ Middleware intercepts all workspace requests  
✅ Violations tracked with correct severity  
✅ Strict mode blocks invalid requests in dev/test  
✅ LogOnly mode allows requests in production  
✅ Admin can view stats and violations via API  
✅ Health check detects unhealthy states  
✅ No performance degradation (< 1ms overhead)

## Implementation Order

1. **WorkspaceViolationTracker** (foundation service)
2. **Enhanced WorkspaceFactory** (extend existing service)
3. **WorkspaceUsageAuditMiddleware** (depends on 1 & 2)
4. **WorkspaceMonitoringController** (depends on 1 & 2)
5. **DI registration in Program.cs** (wire everything together)
6. **Testing** (unit → integration → manual)

---

**Why B+ Progressive Enforcement:**

Production systems must remain stable. B+ allows us to:
- **Learn first**: Gather violation data in production without breaking user workflows
- **Fix proactively**: Address issues discovered in logs before enforcing strictly
- **Enforce gradually**: Enable strict mode once confident in validation logic
- **Maintain flexibility**: Keep LogOnly mode as permanent option for edge cases

This approach mirrors real-world distributed system observability: observe → learn → improve → enforce.
