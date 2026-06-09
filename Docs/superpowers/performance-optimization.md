# Performance Optimization - Stress Testing

This document describes the performance optimization implementation for the multi-user novel agent system.

## Overview

The performance optimization includes:
1. **Memory caching** for frequently accessed data (UserSettings, ProjectMetadata)
2. **Query optimization** using EF Core best practices
3. **SQL query logging** for performance monitoring
4. **Stress testing** to validate performance targets

## Performance Targets

- **API Response Time**: <100ms (P95) under normal load
- **Concurrent Users**: 100 simultaneous requests without degradation
- **Database Query Time**: <10ms for indexed queries
- **Cache Hit Rate**: >80% for cached endpoints after warm-up

## Caching Strategy

### Cache Configuration

- **UserSettings**: 5-minute TTL (cached by userId)
- **ProjectMetadata**: 1-minute TTL (cached by projectId)
- **Implementation**: ASP.NET Core IMemoryCache with custom wrapper
- **Invalidation**: Automatic on update/delete operations

### Cache Keys

- `usersettings:{userId}` - User settings cache key
- `project:{projectId}` - Project metadata cache key

### Cache Invalidation

Cache is invalidated in the following scenarios:
- UserSettings: On update via `UpdateUserSettingsAsync`
- ProjectMetadata: On update via `UpdateProjectAsync` or delete via `DeleteProjectAsync`

## Query Optimization

### SQL Query Logging

SQL query logging is enabled in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

This logs all SQL queries with execution time to help identify:
- Slow queries (>10ms)
- N+1 query patterns
- Missing indexes

### N+1 Query Prevention

The following services already use `.Include()` to prevent N+1 queries:
- **ChapterService**: Uses `.Include(c => c.Project)` when loading chapters
- **ProjectService**: Uses projection (Select) to fetch only needed fields

### Index Verification

The following indexes are configured in `NovelAgentDbContext`:
- `users.username` (unique)
- `users.email` (unique)
- `novel_projects.storage_project_name` (unique)
- `chapters.project_id`
- `chapters.volume_id`
- `chapters.status`
- `foreshadows.project_id`
- `foreshadows.status`
- `characters.project_id`
- `materials.user_id`
- `materials.project_id`
- `agent_memories.user_id, project_id` (composite)

## Stress Testing

### Running the Stress Test

1. **Install k6**:
   ```bash
   # macOS
   brew install k6

   # Linux
   sudo apt-get install k6

   # Windows
   choco install k6
   ```

2. **Start the application**:
   ```bash
   cd Web/NovelAgentWeb
   dotnet run
   ```

3. **Run the stress test**:
   ```bash
   # Option 1: Provide JWT token
   k6 run --env BASE_URL=http://localhost:5000 --env JWT_TOKEN=your_token_here Tests/NovelAgentRegression/Performance/stress-test.js

   # Option 2: Let the script create test users
   k6 run --env BASE_URL=http://localhost:5000 Tests/NovelAgentRegression/Performance/stress-test.js
   ```

### Test Scenario

The stress test simulates the following user flow:
1. Get user settings (tests UserSettings caching)
2. Get projects list (tests pagination and query performance)
3. Create a project
4. Get single project (tests first request, cache miss)
5. Get project again (tests cache hit)
6. Update project (tests cache invalidation)
7. Delete project (cleanup)

### Load Profile

- **Ramp-up**: 0 → 50 users over 30s
- **Scale-up**: 50 → 100 users over 1 minute
- **Sustained load**: 100 users for 2 minutes
- **Ramp-down**: 100 → 0 users over 30s

### Success Criteria

The stress test passes if:
- ✅ P95 response time < 100ms
- ✅ Error rate < 1%
- ✅ No database connection errors
- ✅ Cache hit rate > 80% for cached endpoints

## Monitoring

### Key Metrics to Monitor

1. **Response Times** (from k6 output):
   - P50, P95, P99 percentiles
   - Min, Max, Average

2. **Cache Performance** (from application logs):
   - Cache hit/miss ratio
   - Cache eviction rate

3. **Database Performance** (from SQL logs):
   - Query execution time
   - Number of queries per request

4. **Resource Usage**:
   - Memory consumption
   - CPU usage
   - Database connections

### Reading the Results

After running the stress test, check `stress-test-results.json` for detailed metrics:

```json
{
  "metrics": {
    "http_req_duration": {
      "values": {
        "p(50)": 45.2,
        "p(95)": 87.5,
        "p(99)": 125.3
      }
    },
    "http_req_failed": {
      "values": {
        "rate": 0.005  // 0.5% error rate
      }
    }
  }
}
```

## Production Recommendations

For production deployment:

1. **Distributed Cache**: Replace IMemoryCache with Redis for multi-instance deployments
2. **Database Indexes**: Verify all indexes are created in production database
3. **Connection Pooling**: Configure proper connection pool size in connection string
4. **Query Logging**: Set to Warning level in production (currently Information in dev)
5. **APM Integration**: Add Application Performance Monitoring (e.g., Application Insights)

## Troubleshooting

### High Response Times

If P95 > 100ms:
1. Check SQL logs for slow queries (>10ms)
2. Verify cache is working (check for "Cache hit" logs)
3. Check database connection pool size
4. Verify indexes exist on queried columns

### Low Cache Hit Rate

If cache hit rate < 80%:
1. Verify cache TTL is appropriate for data volatility
2. Check for excessive cache invalidation
3. Ensure cache keys are consistent

### Database Connection Errors

If database connection errors occur:
1. Increase connection pool size in connection string
2. Check for connection leaks (missing `using` statements)
3. Verify database server capacity

## Performance Baseline

After implementation, the baseline performance should be:

| Metric | Target | Actual |
|--------|--------|--------|
| P50 Response Time | <50ms | TBD after testing |
| P95 Response Time | <100ms | TBD after testing |
| P99 Response Time | <200ms | TBD after testing |
| Error Rate | <1% | TBD after testing |
| Cache Hit Rate | >80% | TBD after testing |

Run the stress test and update this table with actual results.
