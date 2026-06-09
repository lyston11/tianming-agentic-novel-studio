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

### Qdrant Container
- **Image**: `qdrant/qdrant:v1.8.0`
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

### Build Warnings
The test project has existing ambiguous reference warnings in `ProjectDataStubs.cs` and other files (CS0104 errors). These are pre-existing issues in the test infrastructure and do not affect the Qdrant integration tests.

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
