# Qdrant Integration Tests

## Overview

This directory contains comprehensive integration tests for the Qdrant vector store implementation using Testcontainers.

## Test Files

### QdrantTestFixture.cs
xUnit class fixture that manages the lifecycle of a Qdrant Docker container for all tests in the test class.

**Features:**
- Starts Qdrant v1.8.0 container before tests
- Configures QdrantVectorStore with container connection details
- Stops and cleans up container after all tests complete
- Uses gRPC port 6334 for communication

### QdrantIntegrationTests.cs
Comprehensive integration test suite covering all Qdrant operations.

## Test Coverage

### 1. Collection Management
- **Test_CreateCollection_Success** - Verifies collection creation with HNSW config
- **Test_GetCollectionInfo_ReturnsCorrectConfig** - Validates collection metadata (dimension: 512, distance: Cosine, index: HNSW)
- **Test_CreateCollection_Idempotent** - Ensures multiple create calls don't fail
- **Test_DeleteCollection_Success** - Confirms collection deletion and cleanup

### 2. Vector Operations
- **Test_UpsertVectors_Single_Success** - Inserts single vector with payload
- **Test_UpsertVectors_Batch_Success** - Inserts 50 vectors in batch
- **Test_UpsertVectors_InvalidDimension_ThrowsException** - Validates dimension checking

### 3. Search Operations
- **Test_SearchSimilar_ReturnsResults** - Basic similarity search (top-K)
- **Test_SearchSimilar_InvalidQueryDimension_ThrowsException** - Validates query vector dimension
- **Test_SearchWithUserFilter_IsolatesData** - **CRITICAL**: Verifies multi-tenant isolation by user_id
- **Test_SearchWithSourceTypeFilter_ReturnsFilteredResults** - Tests filtering by source_type

### 4. Data Isolation
- **Test_SearchWithUserFilter_IsolatesData** 
  - Creates vectors for two users (user_alice, user_bob)
  - Searches with user_id filter
  - Verifies no cross-contamination between users
  - **This is the critical test for multi-tenant security**

### 5. Performance
- **Test_BatchInsert_Performance_Under5Seconds**
  - Inserts 1000 vectors with 512 dimensions
  - Measures execution time
  - Asserts completion < 5 seconds
  - Verifies all vectors were inserted

### 6. Data Management
- **Test_DeleteVectorsByFilter_Success**
  - Inserts vectors with different source types
  - Deletes by filter (e.g., source_type = "chapter")
  - Verifies only filtered data was deleted

## Test Patterns

### Collection Naming
Each test uses a unique project ID to avoid conflicts:
```csharp
var projectId = GenerateTestProjectId(); // e.g., "test_project_abc123"
```

### Cleanup
All tests clean up their collections in `finally` blocks:
```csharp
try {
    // Test code
} finally {
    await _vectorStore.DeleteCollectionAsync(projectId);
}
```

### Indexing Delay
Tests that immediately search after insertion include a 500ms delay to allow Qdrant to index:
```csharp
await Task.Delay(500); // Wait for indexing
```

## Running Tests

### Prerequisites
- Docker must be running (Testcontainers requires Docker)
- .NET 8.0 SDK

### Run All Qdrant Tests
```bash
cd Tests/NovelAgentRegression
dotnet test --filter "FullyQualifiedName~QdrantIntegrationTests"
```

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~QdrantIntegrationTests.Test_SearchWithUserFilter_IsolatesData"
```

### Run Performance Test Only
```bash
dotnet test --filter "FullyQualifiedName~QdrantIntegrationTests.Test_BatchInsert_Performance"
```

## Configuration

## Version Matrix

Keep the Qdrant server image, .NET client, test container image, and vector payload format aligned. The current supported matrix is:

| Component | Current value | Source of truth | Compatibility notes |
| --- | --- | --- | --- |
| Qdrant server | `qdrant/qdrant:v1.18.1` | `docker-compose.yml`; `QdrantTestFixture.cs`; `VectorizationIntegrationTests.cs` | Production and regression tests use the same server image before changing vector serialization or payload indexes. |
| .NET client | `Qdrant.Client` `1.18.1` | `Web/NovelAgentWeb/NovelAgentWeb.csproj` | `QdrantVectorStore` intentionally writes unnamed dense vectors through `Vector.Data` for the current server image. Run the integration suite before upgrading the client or server. |
| Testcontainers adapter | `Testcontainers.Qdrant` `4.12.0` | `Tests/NovelAgentRegression/NovelAgentRegression.csproj` | The adapter launches the same `qdrant/qdrant:v1.18.1` image used by local compose. |
| Transport | HTTP `6333`; gRPC `6334` | `docker-compose.yml`; `ProgramConfigurationTests` | Health checks use HTTP `Qdrant:BaseUrl`; vector operations use gRPC `Qdrant:Host` and `Qdrant:Port`. |
| Vector dimension | `512` | app configuration; test fixtures | Matches `bge-small-zh-v1.5`. Changing the embedding model requires rebuilding affected user collections. |
| Collection boundary | per-user collection `novel_agent_{userId}` | `QdrantVectorStore.GetCollectionName` | Project isolation is enforced by payload filters inside each user collection. User isolation must never rely only on project filters. |

### Upgrade Checklist

1. Update all three image references together: `docker-compose.yml`, `QdrantTestFixture.cs`, and `VectorizationIntegrationTests.cs`.
2. Check `Qdrant.Client` release notes for unnamed vector API changes, then update `QdrantVectorStore.CreateUnnamedVector` only after the integration tests fail for that exact compatibility issue.
3. Run `dotnet test Tests/Unit/Unit.csproj --filter FullyQualifiedName~Qdrant` to catch configuration and payload contract regressions.
4. Run the Testcontainers suite with Docker running:

```bash
cd Tests/NovelAgentRegression
dotnet test --filter "FullyQualifiedName~QdrantIntegrationTests|FullyQualifiedName~VectorizationIntegrationTests"
```

5. If vector dimension, payload schema, or collection naming changes, follow the rebuild SOP below before considering the upgrade complete.

## Rebuild SOP

Qdrant stores derived search indexes. SQLite remains the source of truth for projects, chapters, materials, knowledge, memory rows, outbox events, and `content_vector_points`.

### Safe Retry For Failed Indexing

Use this path when Qdrant is healthy again and outbox rows are already present.

1. Confirm service health:

```bash
curl -fsS http://localhost:6333/health
```

2. List failed or pending index outbox rows through the admin API:

```bash
curl -H "Authorization: Bearer <admin-token>" \
  "http://localhost:5002/api/index/outbox?status=retryable_failed&limit=100"
```

3. Retry a specific event with an idempotency key:

```bash
curl -X POST \
  -H "Authorization: Bearer <admin-token>" \
  -H "Idempotency-Key: qdrant-retry-<event-id>" \
  "http://localhost:5002/api/index/outbox/<event-id>/retry"
```

4. Repeat until `pending` and `retryable_failed` rows are drained for the target project or user.
5. Verify retrieval paths that depend on the rebuilt source type: material search, knowledge search, memory recall, chapter recall, or Story Bible canon recall.

### Collection Rebuild After Storage Loss Or Schema Change

Use this path only after deciding that Qdrant collections can be recreated from canonical database state.

1. Stop the API worker before changing Qdrant storage so the outbox dispatcher cannot write during the reset:

```bash
docker compose stop api
```

2. Snapshot current Qdrant storage for rollback:

```bash
docker run --rm \
  -v tianming-agentic-novel-studio_qdrant-data:/from:ro \
  -v "$PWD:/backup" alpine:3.20 \
  tar -czf /backup/qdrant-backup-$(date +%Y%m%d%H%M%S).tgz -C /from .
```

3. Stop Qdrant and recreate its named volume only after the snapshot exists:

```bash
docker compose stop qdrant
docker compose rm -sf qdrant
docker volume rm tianming-agentic-novel-studio_qdrant-data
docker compose up -d qdrant
```

4. Start the API and confirm `/health` reports Qdrant, Redis, database, and embedding as healthy:

```bash
docker compose up -d api
curl -fsS http://localhost:5002/health
```

5. Retry existing outbox rows with the admin API. Current production code does not include a single "rebuild every vector from SQLite" command; if no outbox rows exist for a source that must be reindexed, create a scoped maintenance task rather than manually editing Qdrant.
6. After rebuild, run the Qdrant integration tests and a user-facing retrieval smoke test for each indexed source type in the target environment.

### Indexed Source Types

The production dispatcher currently indexes or deletes these Qdrant payload source types:

| Outbox event | Aggregate | Qdrant source type |
| --- | --- | --- |
| `index_chapter_content` | `chapter_version` | `chapter` |
| `delete_chapter_content` | `chapter` | `chapter` |
| `delete_project_content` | `project` | all source types in the project |
| `index_material_content` | `material` | `material` |
| `delete_material_content` | `material` | `material` |
| `index_knowledge_content` | `knowledge` | `knowledge` |
| `delete_knowledge_content` | `knowledge` | `knowledge` |
| `index_memory_content` | `memory` | `memory` |
| `index_story_bible_canon` | `story_bible` | `story_bible_canon` |

### Qdrant Container
- **Image**: `qdrant/qdrant:v1.18.1`
- **Port**: 6334 (gRPC)
- **Vector Dimension**: 512 (bge-small-zh-v1.5 model)
- **Distance Metric**: Cosine
- **Index**: HNSW (m=16, ef_construct=100)
- **Batch Size**: 100 vectors per operation

### Test Data Structure
```csharp
VectorData {
    Id: string (UUID)
    Vector: float[512]
    UserId: string
    ProjectId: string
    SourceType: string (chapter, character, etc.)
    SourceId: string
    ChapterId: string?
    ChunkIndex: int?
    Content: string?
    Metadata: Dictionary<string, object>?
}
```

## Known Issues

### Test Execution
If Docker is not running, Testcontainers will fail with a connection error. Ensure Docker Desktop (or Docker daemon) is running before executing tests.

## Success Criteria

✅ **All tests should pass, verifying:**
1. Collection CRUD operations work correctly
2. Vector insert (single and batch) succeeds
3. Similarity search returns expected results
4. User_id filtering provides complete data isolation
5. Batch insert of 1000 vectors completes in < 5 seconds
6. Payload indexes (user_id, project_id, source_type) work correctly
7. Container lifecycle management works properly

## Future Enhancements

Potential additions to the test suite:
- Test with different vector dimensions
- Test concurrent operations
- Test error handling for network failures
- Test collection update operations
- Test point-level CRUD operations by ID
- Load testing with larger datasets (10k+ vectors)
