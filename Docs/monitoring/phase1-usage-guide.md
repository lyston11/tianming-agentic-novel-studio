# Phase 1 Monitoring & Auditing - Usage Guide

## Overview

Phase 1 implements workspace monitoring and auditing with B+ progressive enforcement.

## Configuration

### appsettings.json

```json
{
  "WorkspaceAudit": {
    "EnableStrictMode": false,  // true = block violations, false = log only
    "LogViolations": true,      // log violations to console
    "TrackViolations": true     // store violations in memory
  }
}
```

## Admin Endpoints

All endpoints require `Admin` role JWT token.

### 1. Get Stats
```
GET /api/admin/workspace/stats
```

Returns cache statistics and performance metrics.

### 2. Get Violations
```
GET /api/admin/workspace/violations?severity=High&limit=50
```

Query parameters:
- severity: Low|Medium|High|Critical
- since: ISO8601 datetime
- limit: max results (default 100)

### 3. Get Workspace Detail
```
GET /api/admin/workspace/{userId}/{projectId}
```

Returns specific workspace cache status.

### 4. Health Check
```
GET /api/admin/workspace/health
```

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
