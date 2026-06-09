# Novel Agent Multi-User Migration - Implementation Roadmap

> **For agentic workers:** This is a roadmap document. Each Phase should be expanded into a detailed step-by-step plan when ready to execute. Use superpowers:writing-plans to create the detailed plan for each phase.

**Goal:** 分5个Phase将小说Agent系统从单用户架构迁移到多用户SaaS架构

**Total Estimated Time:** 15-20 days

**Current Status:** Phase 0 (Design Complete) ✅

---

## Phase 0: Design & Planning ✅ COMPLETE

**Duration:** 1 day  
**Status:** ✅ Complete

**Deliverables:**
- [x] Architecture design document
- [x] Database schema design
- [x] Workspace lifecycle design
- [x] API refactoring plan
- [x] This roadmap document

**Document:** `docs/superpowers/specs/2026-06-09-novel-agent-multi-user-architecture.md`

---

## Phase 1: Database & WorkspaceFactory

**Duration:** 3-5 days  
**Status:** 🔲 Not Started

**Goal:** 建立多用户数据库基础和Workspace生命周期管理

### Task Checklist

#### 1.1 Database Schema
- [ ] Create EF Core entity classes (StoryConstitution, VolumeArc, Character, ForeshadowEntry, WorldSetting, AgentRun)
- [ ] Update NovelAgentDbContext with new DbSets
- [ ] Generate EF Core migration
- [ ] Run migration and verify table creation
- [ ] Write unit tests for entity validation

#### 1.2 WorkspaceFactory Implementation
- [ ] Create WorkspaceEntry class (reference counting, LRU tracking)
- [ ] Create IWorkspaceFactory interface
- [ ] Implement WorkspaceFactory (LRU pool, eviction logic)
- [ ] Add WorkspaceFactoryOptions configuration
- [ ] Write unit tests (LRU eviction, reference counting, thread safety)
- [ ] Add background eviction timer (30s interval)

#### 1.3 Repository Layer
- [ ] Create IStoryBibleRepository interface
- [ ] Implement StoryBibleRepository (CRUD for all StoryBible entities)
- [ ] Write integration tests with in-memory SQLite
- [ ] Add transaction support for multi-entity saves

#### 1.4 NovelAgentWorkspace Thread Safety
- [ ] Add SemaphoreSlim locks to Orchestrator operations
- [ ] Test concurrent access scenarios
- [ ] Update Program.cs DI registration

**Exit Criteria:**
- ✅ All database tables created
- ✅ WorkspaceFactory can create/cache/evict Workspace instances
- ✅ Repository layer tests pass (>90% coverage)
- ✅ Thread safety tests pass (100 concurrent operations)

---

## Phase 2: Vector Storage (Qdrant)

**Duration:** 2-3 days  
**Status:** 🔲 Not Started

**Goal:** 实现基于Qdrant的语义检索系统

### Task Checklist

#### 2.1 Qdrant Collection Management
- [ ] Create QdrantCollectionManager service
- [ ] Implement collection creation with proper schema
- [ ] Implement collection deletion (user cleanup)
- [ ] Add collection existence check

#### 2.2 QdrantRetrievalService
- [ ] Create IQdrantRetrievalService interface
- [ ] Implement SearchInProjectAsync (单项目检索)
- [ ] Implement SearchAcrossProjectsAsync (跨项目检索)
- [ ] Implement DetectSimilarPatternsAsync (相似度检测)
- [ ] Implement HybridSearchAsync (向量+BM25)
- [ ] Write unit tests with mock Qdrant client

#### 2.3 Vectorization Pipeline
- [ ] Create IMaterialVectorizationService interface
- [ ] Implement material chunking (1500 token/chunk, 200 overlap)
- [ ] Implement embedding generation (batch processing)
- [ ] Implement Qdrant upsert with proper payload
- [ ] Add progress tracking for large materials
- [ ] Write integration tests with real Qdrant

#### 2.4 KnowledgeEntry Vectorization
- [ ] Implement KnowledgeEntryVectorizationService
- [ ] Add vector_id field to knowledge_entries table
- [ ] Migration to add vector tracking

**Exit Criteria:**
- ✅ Qdrant collections created per user
- ✅ Material chunking produces correct overlaps
- ✅ Semantic search returns relevant results (manual testing)
- ✅ Similar pattern detection works (>0.85 similarity threshold)

---

## Phase 3: API Refactoring

**Duration:** 5-7 days  
**Status:** 🔲 Not Started

**Goal:** 重建所有Controller，集成WorkspaceFactory和新数据层

### Task Checklist

#### 3.1 MaterialsController (NEW)
- [ ] POST /api/materials/upload
- [ ] POST /api/materials (text ingest)
- [ ] GET /api/materials (list by project)
- [ ] GET /api/materials/{id}
- [ ] GET /api/materials/{id}/content
- [ ] PATCH /api/materials/{id}
- [ ] DELETE /api/materials/{id}
- [ ] POST /api/materials/{id}/analyze
- [ ] Add [Authorize] and userId filtering
- [ ] Write integration tests

#### 3.2 KnowledgeController (NEW)
- [ ] POST /api/knowledge/search
- [ ] GET /api/knowledge/entries
- [ ] POST /api/knowledge/entries
- [ ] PATCH /api/knowledge/entries/{id}
- [ ] DELETE /api/knowledge/entries/{id}
- [ ] POST /api/knowledge/detect-duplicates
- [ ] Add [Authorize] and userId filtering
- [ ] Write integration tests

#### 3.3 StoryBibleController (NEW)
- [ ] GET /api/story-bible
- [ ] POST /api/story-bible/constitution
- [ ] POST /api/story-bible/volumes
- [ ] PATCH /api/story-bible/volumes/{id}
- [ ] POST /api/story-bible/characters
- [ ] PATCH /api/story-bible/characters/{id}
- [ ] POST /api/story-bible/foreshadows
- [ ] PATCH /api/story-bible/foreshadows/{id}/resolve
- [ ] POST /api/story-bible/world-settings
- [ ] PATCH /api/story-bible/world-settings/{id}
- [ ] Add [Authorize] and userId filtering
- [ ] Write integration tests

#### 3.4 WorkflowController (NEW)
- [ ] GET /api/workflow
- [ ] POST /api/workflow/plan-foundation
- [ ] POST /api/workflow/plan-volume
- [ ] POST /api/workflow/plan-chapter
- [ ] POST /api/workflow/generate-chapter
- [ ] POST /api/workflow/review-chapter
- [ ] POST /api/workflow/commit-chapter
- [ ] Add [Authorize] and userId filtering
- [ ] Write integration tests

#### 3.5 AgentController Refactoring
- [ ] Integrate WorkspaceFactory.AcquireAsync
- [ ] Update CreateSession to acquire Workspace
- [ ] Update SendMessage to call Touch()
- [ ] Add session cleanup (Release)
- [ ] Test SSE streaming with new Workspace
- [ ] Write integration tests

**Exit Criteria:**
- ✅ All new Controllers implemented with [Authorize]
- ✅ All endpoints tested (Postman collection or integration tests)
- ✅ WorkspaceFactory integrated into AgentController
- ✅ No access to other users' data (security test)

---

## Phase 4: Frontend Migration

**Duration:** 3-4 days  
**Status:** 🔲 Not Started

**Goal:** 更新前端调用新API，移除对旧API的依赖

### Task Checklist

#### 4.1 API Layer Update (src/api/)
- [ ] Remove old API functions (getWorkspace, getStoryBible, getMaterials)
- [ ] Add new Materials API functions
- [ ] Add new Knowledge API functions
- [ ] Add new StoryBible API functions
- [ ] Add new Workflow API functions
- [ ] Update types.ts with new response types
- [ ] Test all API functions in browser console

#### 4.2 MaterialsPage.tsx
- [ ] Replace getMaterials with new API
- [ ] Update uploadMaterial to use new endpoint
- [ ] Update material list rendering
- [ ] Update material analysis UI
- [ ] Test upload → analyze → display flow

#### 4.3 WorkflowPage.tsx
- [ ] Replace workflow API calls
- [ ] Update chapter planning UI
- [ ] Update chapter generation UI
- [ ] Test full workflow cycle

#### 4.4 LibraryPage.tsx
- [ ] Replace getNovelLibrary with new API
- [ ] Update StoryBible display
- [ ] Test project switching

#### 4.5 Rail.tsx Component
- [ ] Remove getWorkspace call
- [ ] Use currentProject from ProjectContext
- [ ] Test project name display

#### 4.6 E2E Testing
- [ ] Test login → create project → upload material
- [ ] Test create chapter → generate content
- [ ] Test agent conversation
- [ ] Test logout and re-login

**Exit Criteria:**
- ✅ All pages load without console errors
- ✅ Materials upload and display correctly
- ✅ Workflow functions work end-to-end
- ✅ No references to old API endpoints

---

## Phase 5: Data Migration

**Duration:** 1-2 days  
**Status:** 🔲 Not Started

**Goal:** 迁移现有数据到新架构，验证完整性

### Task Checklist

#### 5.1 Backup Existing Data
- [ ] Create backup script
- [ ] Backup App_Data directory
- [ ] Backup database file
- [ ] Timestamp backup folder

#### 5.2 File System Reorganization
- [ ] Scan App_Data/Projects/AgenticNovelStudio/
- [ ] Create default admin user in database
- [ ] Create default project in database
- [ ] Move files to Users/{adminId}/Projects/{projectId}/
- [ ] Update file paths in database

#### 5.3 JSON to SQLite Migration
- [ ] Read StoryBible.json
- [ ] Parse and insert into story_constitutions
- [ ] Parse and insert into volume_arcs
- [ ] Parse and insert into characters
- [ ] Parse and insert into foreshadow_ledger
- [ ] Verify all records migrated

#### 5.4 Materials Migration
- [ ] Verify materials table has all entries
- [ ] Move material files to new paths
- [ ] Update file_path column in materials table

#### 5.5 Vectorization of Existing Data
- [ ] Create Qdrant collections for admin user
- [ ] Vectorize all materials
- [ ] Vectorize all knowledge entries
- [ ] Verify vector count matches entity count

#### 5.6 Validation & Testing
- [ ] Run data integrity checks
- [ ] Test login as admin
- [ ] Test opening migrated project
- [ ] Test viewing migrated materials
- [ ] Test agent conversation with migrated data

#### 5.7 Rollback Plan
- [ ] Document rollback steps
- [ ] Test rollback on copy of data
- [ ] Create rollback script

**Exit Criteria:**
- ✅ All data migrated successfully
- ✅ No data loss (count verification)
- ✅ System works with migrated data
- ✅ Rollback tested and documented

---

## Success Criteria (Overall)

**Functional:**
- [ ] Multi-user authentication and isolation working
- [ ] All old functionality available in new architecture
- [ ] Semantic search returning relevant results
- [ ] Workspace caching improving performance

**Performance:**
- [ ] Workspace creation <300ms
- [ ] Vector search p95 <500ms
- [ ] System supports 100 concurrent users

**Quality:**
- [ ] Test coverage >80%
- [ ] No critical security vulnerabilities
- [ ] No data leakage between users
- [ ] System stable for 7 days continuous operation

---

## Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| NovelAgentOrchestrator thread safety | High | Add locks, test with 100 concurrent sessions |
| Workspace init time >500ms | Medium | Profile and optimize, add prewarming |
| Qdrant collection explosion | High | Use userId not projectId for collections |
| Data migration failure | Critical | Full backup, tested rollback, staged migration |
| Vector search low accuracy | Medium | Tune embedding model, adjust chunk size |

---

## Next Steps

1. **Review this roadmap** - Confirm Phase breakdown and task lists
2. **Expand Phase 1** - Use superpowers:writing-plans to create detailed Phase 1 plan
3. **Execute Phase 1** - Use superpowers:subagent-driven-development
4. **Repeat for remaining Phases**

---

**Created:** 2026-06-09  
**Design Doc:** `docs/superpowers/specs/2026-06-09-novel-agent-multi-user-architecture.md`
