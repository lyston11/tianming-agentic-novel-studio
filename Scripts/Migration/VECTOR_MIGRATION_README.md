# Vector Migration Tool - Task 1.5

## Overview

This tool migrates file-based vector embeddings from JSON files to Qdrant vector database, enabling high-performance similarity search for the multi-user novel agent system.

## Purpose

- **Migrate legacy vector data** from `chapter_embeddings.json` and `chunk_embeddings.json` to Qdrant
- **Create Qdrant collections** for each project with proper indexing
- **Map legacy chapter IDs** to new database GUIDs from Task 1.4 migration
- **Verify migration** by comparing vector counts and testing similarity search
- **Backup original files** for 30-day retention with feature flag support

## Architecture

### Source Data Structure

**chapter_embeddings.json:**
```json
{
  "model": "bge-small-zh-v1.5",
  "dimension": 512,
  "embeddings": [
    {
      "chapter_id": "chapter_1",
      "chapter_number": 1,
      "title": "第一章",
      "content": "章节内容...",
      "embedding": [0.123, -0.456, ...],
      "project_id": "AgenticNovelStudio",
      "created_at": "2026-06-01T10:00:00Z"
    }
  ]
}
```

**chunk_embeddings.json:**
```json
{
  "model": "bge-small-zh-v1.5",
  "dimension": 512,
  "embeddings": [
    {
      "chunk_id": "chunk_1",
      "chapter_id": "chapter_1",
      "chunk_index": 0,
      "content": "分块内容...",
      "embedding": [0.789, -0.234, ...],
      "project_id": "AgenticNovelStudio",
      "created_at": "2026-06-01T10:00:00Z"
    }
  ]
}
```

### Target Qdrant Structure

**Collection Naming:** `project_{project_id}`

**Vector Configuration:**
- Dimension: 512 (bge-small-zh-v1.5 model)
- Distance Metric: Cosine
- Index Type: HNSW (m=16, ef_construct=100)

**Payload Structure:**
```json
{
  "user_id": "guid",
  "project_id": "guid",
  "source_type": "chapter|chunk",
  "source_id": "guid",
  "chapter_id": "guid",
  "chunk_index": 0,
  "content": "原始文本内容",
  "metadata": {
    "legacy_id": "chapter_1",
    "chapter_number": 1,
    "title": "章节标题",
    "migrated_at": "2026-06-08T..."
  }
}
```

**Payload Indexes:**
- `user_id` (Keyword) - Critical for tenant isolation
- `project_id` (Keyword) - Collection-level filtering
- `source_type` (Keyword) - Filter by chapter/chunk

## Components

### 1. VectorMigrationService.cs

Main migration logic:
- Load legacy vector JSON files
- Validate vector dimensions (must be 512)
- Map legacy chapter IDs to database GUIDs
- Create Qdrant collections per project
- Batch insert vectors (100 per batch)
- Create payload indexes
- Verify migration success
- Test similarity search

### 2. VectorMigrationTool.cs

Console application entry point:
- Parse command-line arguments
- Configure dependency injection
- Set up logging
- Execute migration
- Display results

### 3. LegacyVectorModels.cs

Data models for legacy vector files:
- `LegacyChapterEmbedding` - Chapter-level vectors
- `LegacyChunkEmbedding` - Chunk-level vectors
- `VectorMigrationResult` - Migration statistics
- `ProjectVectorStats` - Per-project metrics

### 4. SampleVectorGenerator.cs

Generate test data when no legacy vectors exist:
- Create sample chapter embeddings
- Create sample chunk embeddings
- Normalized random vectors for testing
- Cleanup utilities

## Prerequisites

1. **Task 1.1 Completed:** SQLite database initialized
2. **Task 1.2 Completed:** Qdrant Docker container running at localhost:6333
3. **Task 1.3 Completed:** IVectorStore service implemented
4. **Task 1.4 Completed:** JSON data migrated to SQLite (projects, chapters)

## Usage

### Basic Migration

```bash
cd Scripts/Migration
./run-vector-migration.sh
```

### Command-Line Options

```bash
# Show help
./run-vector-migration.sh --help

# Force migration (overwrite existing collections)
./run-vector-migration.sh --force

# Skip backup creation
./run-vector-migration.sh --no-backup

# Verbose logging
./run-vector-migration.sh --verbose

# Run similarity search test after migration
./run-vector-migration.sh --test-search

# Combine options
./run-vector-migration.sh --force --verbose --test-search

# Skip prerequisite checks
./run-vector-migration.sh --skip-checks

# Direct dotnet run
cd Scripts/Migration
dotnet run --project VectorMigrationTool.csproj -- --force --test-search
```

## Migration Process

### Step 1: Prerequisite Checks

```bash
✓ Qdrant is running (http://localhost:6333)
✓ Database exists (novelagent.db)
✓ Projects: 3
✓ Chapters: 15
⚠ chapter_embeddings.json not found
⚠ chunk_embeddings.json not found
```

### Step 2: Load Legacy Vectors

```
Loading chapter embeddings from Config/guides/chapter_embeddings.json
Loaded 15 chapter embeddings
Loading chunk embeddings from Config/guides/chunk_embeddings.json
Loaded 45 chunk embeddings
```

### Step 3: Validate Dimensions

```
✓ Chapter embeddings dimension: 512
✓ Chunk embeddings dimension: 512
```

### Step 4: Build Chapter Mapping

```
Built chapter mapping with 15 entries
  chapter_1 -> a1b2c3d4-...
  chapter_2 -> e5f6g7h8-...
  ...
```

### Step 5: Migrate Projects

```
Project: 项目A (id: xxx)
  ✓ Collection created: project_xxx
  ✓ Imported 5 chapter vectors
  ✓ Imported 15 chunk vectors
  ✓ Total: 20 vectors

Project: 项目B (id: yyy)
  ✓ Collection created: project_yyy
  ✓ Imported 10 chapter vectors
  ✓ Imported 30 chunk vectors
  ✓ Total: 40 vectors
```

### Step 6: Verification

```
Verifying migration...
  Project xxx: ✓ Vector count matches (20)
  Project yyy: ✓ Vector count matches (40)
```

### Step 7: Backup

```
Creating vector backup at: App_Data/Backup/20260608_210000/VectorIndexes/
  ✓ Backed up chapter_embeddings.json
  ✓ Backed up chunk_embeddings.json
  ✓ Created backup metadata
```

## Output Example

```
=== Vector Migration Tool: JSON to Qdrant ===

============================================================
Vector migration completed successfully in 3.45s
- Projects Processed: 2
- Collections Created: 2
- Chapter Vectors: 15
- Chunk Vectors: 45
- Total Vectors: 60
- Skipped (Invalid): 0
- Backup Location: App_Data/Backup/20260608_210000/VectorIndexes
============================================================

Testing similarity search...
Search returned 5 results:
  - Score: 0.8234, SourceType: chapter, Content: 第一章的内容...
  - Score: 0.7891, SourceType: chunk, Content: 分块1的内容...
  - Score: 0.7456, SourceType: chunk, Content: 分块2的内容...
  - Score: 0.7123, SourceType: chapter, Content: 第二章的内容...
  - Score: 0.6987, SourceType: chunk, Content: 分块3的内容...
Similarity search test: PASSED
```

## Handling Missing Vector Files

If no legacy vector files exist (current situation), the migration will:

1. Log a warning: "No legacy vector embedding files found"
2. Skip migration gracefully
3. Return success with 0 vectors migrated
4. Not create backup

**This is by design** - the infrastructure is ready for when vector files are generated.

## Feature Flag for Fallback

Add to `appsettings.json`:

```json
{
  "VectorStore": {
    "UseQdrant": true,
    "FallbackToFileIndex": false,
    "FileIndexPath": "App_Data/Backup/20260608_210000/VectorIndexes"
  }
}
```

- `UseQdrant`: Use Qdrant for vector search (default: true)
- `FallbackToFileIndex`: Fall back to file-based index if Qdrant fails (default: false)
- `FileIndexPath`: Path to backup vector files for fallback

## Error Handling

### Common Issues

**1. Qdrant Not Running**
```
ERROR: Failed to connect to Qdrant at localhost:6333
Solution: docker-compose up -d
```

**2. Database Not Found**
```
ERROR: Database not found at App_Data/Database/novelagent.db
Solution: Run Task 1.1 (database initialization) first
```

**3. No Projects in Database**
```
ERROR: No projects found in database
Solution: Run Task 1.4 (JSON to SQLite migration) first
```

**4. Vector Dimension Mismatch**
```
ERROR: Vector dimension 1024 does not match expected 512
Solution: Check embedding model configuration
```

**5. Collection Already Exists**
```
WARNING: Collection already exists for project xxx, skipping
Solution: Use --force flag to overwrite
```

## Idempotency

The migration is **idempotent** - can be run multiple times safely:

- Without `--force`: Skips existing collections
- With `--force`: Overwrites existing collections
- Backup is created each time (timestamped)
- Chapter mapping is rebuilt from current database state

## Testing

### Manual Testing

```bash
# 1. Check prerequisites
./run-vector-migration.sh --skip-checks

# 2. Run migration with verbose output
./run-vector-migration.sh --verbose --test-search

# 3. Verify in Qdrant UI
open http://localhost:6333/dashboard

# 4. Check collection info
curl http://localhost:6333/collections/project_{project_id}
```

### Integration Testing

See `Scripts/Migration/TESTING.md` for comprehensive test scenarios.

## Performance

### Batch Size Configuration

Default: 100 vectors per batch (configurable in `appsettings.json`)

```json
{
  "Qdrant": {
    "BatchSize": 100
  }
}
```

### Expected Performance

- Small project (10 chapters, 30 chunks): ~1-2 seconds
- Medium project (50 chapters, 150 chunks): ~5-8 seconds
- Large project (200 chapters, 600 chunks): ~20-30 seconds

Network latency and disk I/O are the main bottlenecks.

## Troubleshooting

### Enable Debug Logging

```bash
./run-vector-migration.sh --verbose
```

### Check Qdrant Logs

```bash
docker logs qdrant
```

### Verify Collection Creation

```bash
curl http://localhost:6333/collections
```

### Inspect Vector Payload

```bash
curl -X POST http://localhost:6333/collections/project_{id}/points/scroll \
  -H "Content-Type: application/json" \
  -d '{"limit": 1, "with_payload": true, "with_vector": false}'
```

## Next Steps

After successful migration:

1. **Task 2.4:** Refactor Vector Retrieval Service to use Qdrant
2. **Task 4.2:** Write integration tests for Qdrant
3. **Implement vector generation** for new chapters (embedding service)
4. **Set up automatic re-indexing** when chapters are updated

## Files Created

```
Scripts/Migration/
├── VectorMigrationService.cs          # Main migration logic
├── VectorMigrationTool.cs             # Console app entry point
├── VectorMigrationTool.csproj         # Project file
├── SampleVectorGenerator.cs           # Test data generator
├── Models/
│   └── LegacyVectorModels.cs         # Data models
├── run-vector-migration.sh            # Run script
└── VECTOR_MIGRATION_README.md         # This file
```

## References

- Qdrant Documentation: https://qdrant.tech/documentation/
- bge-small-zh-v1.5 Model: https://huggingface.co/BAAI/bge-small-zh-v1.5
- Task 1.3: IVectorStore Service Implementation
- Design Doc: docs/superpowers/specs/2026-06-08-multi-user-novel-agent-design.md
