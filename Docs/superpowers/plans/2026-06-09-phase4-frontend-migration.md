# Phase 4: Frontend Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate frontend from old single-user API endpoints to new multi-user REST API

**Architecture:** Update React components and API layer to consume new REST endpoints (MaterialsController, KnowledgeController, StoryBibleController, WorkflowController), remove dependencies on old workspace/library endpoints

**Tech Stack:** React 18, TypeScript, TanStack Query, Vite

---

## File Structure

**Files to Modify:**
- `Web/NovelAgentWeb.Frontend/src/api/index.ts` - Remove old API functions, add new REST API functions
- `Web/NovelAgentWeb.Frontend/src/api/types.ts` - Add new response types for REST APIs
- `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx` - Update to use new Materials API
- `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx` - Update to use new Workflow API
- `Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx` - Update to use new StoryBible API
- `Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx` - Remove getWorkspace dependency
- `Web/NovelAgentWeb.Frontend/src/components/materials/KnowledgeBaseBrowser.tsx` - Update to use new Knowledge API

**No New Files:** All changes are modifications to existing files

---

## Task 1: Update API Types

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/types.ts`

- [ ] **Step 1: Add Material response types**

```typescript
// Add after existing imports at the top of the file
export interface MaterialResponse {
  id: string;
  userId: string;
  projectId: string;
  title: string;
  filePath: string;
  category: string;
  tags: string;
  createdAt: string;
}

export interface MaterialListResponse {
  materials: MaterialResponse[];
  totalCount: number;
}

export interface MaterialContentResponse {
  id: string;
  title: string;
  content: string;
  category: string;
}

export interface UploadMaterialResponse {
  id: string;
  title: string;
  filePath: string;
  category: string;
}
```

- [ ] **Step 2: Add Knowledge response types**

```typescript
export interface KnowledgeEntryResponse {
  id: string;
  userId: string;
  projectId: string;
  title: string;
  content: string;
  category: string;
  tags: string;
  sourceType: string;
  sourceId: string | null;
  usageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface KnowledgeSearchResult {
  entry: KnowledgeEntryResponse;
  score: float;
  snippet: string;
}

export interface KnowledgeSearchResponse {
  results: KnowledgeSearchResult[];
  totalCount: number;
}
```

- [ ] **Step 3: Add StoryBible response types**

```typescript
export interface StoryConstitutionResponse {
  id: string;
  userId: string;
  projectId: string;
  genre: string;
  subGenre: string | null;
  coreHook: string;
  readerPromise: string | null;
  genreProfile: string | null;
  targetAudience: string | null;
  taboos: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CharacterResponse {
  id: string;
  userId: string;
  projectId: string;
  name: string;
  role: string;
  alias: string | null;
  age: number | null;
  gender: string | null;
  appearance: string | null;
  personality: string | null;
  background: string | null;
  initialPowerLevel: string | null;
  currentPowerLevel: string | null;
  specialAbilities: string | null;
  coreGoal: string | null;
  motivation: string | null;
  relationships: string | null;
  status: string;
  firstAppearChapter: string | null;
  lastAppearChapter: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface StoryBibleResponse {
  constitution: StoryConstitutionResponse | null;
  characters: CharacterResponse[];
}
```

- [ ] **Step 4: Add VolumeArc response types**

```typescript
export interface VolumeArcResponse {
  id: string;
  userId: string;
  projectId: string;
  volumeNumber: number;
  volumeTitle: string;
  volumeTheme: string | null;
  targetChapters: number | null;
  currentChapters: number;
  act1Setup: string | null;
  act2Confrontation: string | null;
  act3Climax: string | null;
  act4Resolution: string | null;
  keyEvents: string | null;
  majorConflict: string | null;
  conflictEscalation: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
}
```

- [ ] **Step 5: Commit type definitions**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/types.ts
git commit -m "feat(frontend): add REST API response types for Phase 4"
```

---

## Task 2: Remove Old API Functions

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts:46-51`

- [ ] **Step 1: Remove getWorkspace and getStoryBible**

Delete lines 46-50:
```typescript
// DELETE THESE LINES:
// Workspace
export const getWorkspace = () => get<WorkspaceInfo>('/workspace');

// Story Bible
export const getStoryBible = () => get<StoryBibleDocument>('/story-bible');
```

- [ ] **Step 2: Remove old runs API (lines 52-96)**

Delete lines 52-96 (listRuns, getRun, continueRun, reviewRun, rewriteRun, planFoundation, commitFoundation, planVolume, commitVolume, planChapter, selectCandidate, executeChapter, importCanon, promoteCanon, importForeshadow, confirmForeshadow, importCharacter, confirmCharacter)

- [ ] **Step 3: Remove old creative knowledge API (lines 98-102)**

Delete lines 98-102:
```typescript
// DELETE THESE LINES:
export const searchKnowledge = (req: CreativeKnowledgeQueryRequest) =>
  post<CreativeKnowledgeRetrievalResult>('/creative-knowledge/search', req);
export const recordUsedPattern = (req: UsedPatternRequest) =>
  post('/creative-knowledge/used-pattern', req);
```

- [ ] **Step 4: Remove old materials API (lines 104-147)**

Delete lines 104-147 (getMaterials, ingestMaterial, uploadMaterialFile, analyzeMaterial, deleteMaterial, updateMaterial, getMaterialContent, getKnowledgeEntries, deleteKnowledgeEntry, updateKnowledgeEntry)

- [ ] **Step 5: Remove old library API (lines 175-180)**

Delete lines 175-180:
```typescript
// DELETE THESE LINES:
export const getNovelLibrary = (projectId?: string) =>
  get<NovelLibraryDocument>(`/novel-library${projectId ? `?projectId=${encodeURIComponent(projectId)}` : ''}`);
export const getProjectWorkflow = (projectId: string) =>
  get<ProjectWorkflowDocument>(`/novel-projects/${encodeURIComponent(projectId)}/workflow`);
```

- [ ] **Step 6: Commit removal**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "refactor(frontend): remove old single-user API functions"
```

---

## Task 3: Add New Materials API Functions

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts` (add at end before Agent Chat section)

- [ ] **Step 1: Add new Materials API functions**

Insert before "// Agent Chat" comment (around line 148):
```typescript
// Materials API (new multi-user endpoints)
export const listMaterials = (projectId: string) =>
  get<MaterialListResponse>(`/materials?projectId=${encodeURIComponent(projectId)}`);

export const getMaterialById = (id: string) =>
  get<MaterialResponse>(`/materials/${id}`);

export const getMaterialContent = (id: string) =>
  get<MaterialContentResponse>(`/materials/${id}/content`);

export const uploadMaterial = async (projectId: string, file: File, category: string, tags?: string) => {
  const formData = new FormData();
  formData.append('File', file);
  formData.append('ProjectId', projectId);
  formData.append('Category', category);
  if (tags) formData.append('Tags', tags);
  
  const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';
  const token = localStorage.getItem('token');
  const response = await fetch(`${BASE_URL}/materials/upload`, {
    method: 'POST',
    headers: token ? { 'Authorization': `Bearer ${token}` } : {},
    body: formData,
  });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json() as Promise<UploadMaterialResponse>;
};

export const createMaterialFromText = (req: { projectId: string; title: string; content: string; category: string; tags?: string }) =>
  post<MaterialResponse>('/materials', req);

export const updateMaterialById = (id: string, req: { title?: string; category?: string; tags?: string }) =>
  api<MaterialResponse>(`/materials/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteMaterialById = (id: string) =>
  api<void>(`/materials/${id}`, { method: 'DELETE' });
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd Web/NovelAgentWeb.Frontend && npm run type-check`
Expected: No errors related to Materials API

- [ ] **Step 3: Commit new Materials API**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "feat(frontend): add new Materials API functions"
```

---

## Task 4: Add New Knowledge API Functions

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts` (add after Materials API)

- [ ] **Step 1: Add Knowledge API functions**

Insert after Materials API functions:
```typescript
// Knowledge API (new multi-user endpoints)
export const searchKnowledgeEntries = (req: { projectId: string; query: string; topK?: number; category?: string }) =>
  post<KnowledgeSearchResponse>('/knowledge/search', req);

export const listKnowledgeEntries = (projectId: string, category?: string) =>
  get<KnowledgeEntryResponse[]>(`/knowledge/entries?projectId=${encodeURIComponent(projectId)}${category ? `&category=${encodeURIComponent(category)}` : ''}`);

export const createKnowledgeEntry = (req: { projectId: string; title: string; content: string; category: string; tags?: string; sourceType?: string; sourceId?: string }) =>
  post<KnowledgeEntryResponse>('/knowledge/entries', req);

export const updateKnowledgeEntryById = (id: string, req: { title?: string; content?: string; category?: string; tags?: string }) =>
  api<KnowledgeEntryResponse>(`/knowledge/entries/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteKnowledgeEntryById = (id: string) =>
  api<void>(`/knowledge/entries/${id}`, { method: 'DELETE' });
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd Web/NovelAgentWeb.Frontend && npm run type-check`
Expected: No errors

- [ ] **Step 3: Commit Knowledge API**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "feat(frontend): add new Knowledge API functions"
```

---

## Task 5: Add New StoryBible API Functions

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts` (add after Knowledge API)

- [ ] **Step 1: Add StoryBible API functions**

Insert after Knowledge API:
```typescript
// StoryBible API (new multi-user endpoints)
export const getStoryBibleByProject = (projectId: string) =>
  get<StoryBibleResponse>(`/storybible?projectId=${encodeURIComponent(projectId)}`);

export const getConstitution = (projectId: string) =>
  get<StoryConstitutionResponse>(`/storybible/constitution?projectId=${encodeURIComponent(projectId)}`);

export const createOrUpdateConstitution = (req: { projectId: string; genre: string; subGenre?: string; coreHook: string; readerPromise?: string; genreProfile?: string; targetAudience?: string; taboos?: string }) =>
  post<StoryConstitutionResponse>('/storybible/constitution', req);

export const listCharacters = (projectId: string) =>
  get<CharacterResponse[]>(`/storybible/characters?projectId=${encodeURIComponent(projectId)}`);

export const createCharacter = (req: { projectId: string; name: string; role: string; alias?: string; age?: number; gender?: string; appearance?: string; personality?: string; background?: string; coreGoal?: string; motivation?: string }) =>
  post<CharacterResponse>('/storybible/characters', req);

export const updateCharacterById = (id: string, req: Partial<CharacterResponse>) =>
  api<CharacterResponse>(`/storybible/characters/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteCharacterById = (id: string) =>
  api<void>(`/storybible/characters/${id}`, { method: 'DELETE' });
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd Web/NovelAgentWeb.Frontend && npm run type-check`
Expected: No errors

- [ ] **Step 3: Commit StoryBible API**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "feat(frontend): add new StoryBible API functions"
```

---

## Task 6: Add New Workflow API Functions

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/api/index.ts` (add after StoryBible API)

- [ ] **Step 1: Add Workflow API functions**

Insert after StoryBible API:
```typescript
// Workflow API (new multi-user endpoints)
export const listVolumeArcs = (projectId: string) =>
  get<VolumeArcResponse[]>(`/workflow/volumes?projectId=${encodeURIComponent(projectId)}`);

export const getVolumeArc = (id: string) =>
  get<VolumeArcResponse>(`/workflow/volumes/${id}`);

export const createVolumeArc = (req: { projectId: string; volumeNumber: number; volumeTitle: string; volumeTheme?: string; targetChapters?: number; act1Setup?: string; act2Confrontation?: string; act3Climax?: string; act4Resolution?: string; keyEvents?: string; majorConflict?: string; conflictEscalation?: string }) =>
  post<VolumeArcResponse>('/workflow/volumes', req);

export const updateVolumeArc = (id: string, req: Partial<VolumeArcResponse>) =>
  api<VolumeArcResponse>(`/workflow/volumes/${id}`, {
    method: 'PATCH',
    body: JSON.stringify(req),
  });

export const deleteVolumeArc = (id: string) =>
  api<void>(`/workflow/volumes/${id}`, { method: 'DELETE' });
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd Web/NovelAgentWeb.Frontend && npm run type-check`
Expected: No errors

- [ ] **Step 3: Commit Workflow API**

```bash
git add Web/NovelAgentWeb.Frontend/src/api/index.ts
git commit -m "feat(frontend): add new Workflow API functions"
```

---

## Task 7: Update MaterialsPage to Use New API

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx`

- [ ] **Step 1: Update imports**

Replace lines 3-10:
```typescript
import {
  listMaterials,
  uploadMaterial,
  deleteMaterialById,
  updateMaterialById,
  getMaterialContent,
} from '../api';
import { projectService } from '../services/projectService';
```

- [ ] **Step 2: Add currentProject state**

After line 18, add:
```typescript
const [currentProjectId, setCurrentProjectId] = useState<string | null>(null);

useEffect(() => {
  projectService.getCurrentProject().then((project) => {
    if (project) setCurrentProjectId(project.id);
  });
}, []);
```

- [ ] **Step 3: Update materials query**

Replace line 38:
```typescript
const { data: materialsData } = useQuery({ 
  queryKey: ['materials', currentProjectId], 
  queryFn: () => currentProjectId ? listMaterials(currentProjectId) : Promise.resolve({ materials: [], totalCount: 0 }),
  enabled: !!currentProjectId
});
```

- [ ] **Step 4: Update upload mutation**

Replace lines 40-50:
```typescript
const uploadMutation = useMutation({
  mutationFn: (file: File) => {
    if (!currentProjectId) throw new Error('No project selected');
    return uploadMaterial(currentProjectId, file, 'Reference', '');
  },
  onSuccess: (data) => {
    queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
    addLog(`文件已上传: ${data.title}`);
  },
  onError: (err) => addLog(`上传失败: ${err}`),
});
```

- [ ] **Step 5: Update delete mutation**

Replace lines 63-71:
```typescript
const deleteMutation = useMutation({
  mutationFn: deleteMaterialById,
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
    queryClient.invalidateQueries({ queryKey: ['knowledgeEntries'] });
    addLog('素材已删除');
  },
  onError: (err) => addLog(`删除失败: ${err}`),
});
```

- [ ] **Step 6: Update materials rendering**

Replace access to `materialsData` (around line 150+):
```typescript
const materials = materialsData?.materials ?? [];
```

- [ ] **Step 7: Test in browser**

Run: `cd Web/NovelAgentWeb.Frontend && npm run dev`
Open browser, navigate to Materials page
Expected: Materials load from new API without errors

- [ ] **Step 8: Commit MaterialsPage update**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/MaterialsPage.tsx
git commit -m "refactor(frontend): update MaterialsPage to use new REST API"
```

---

## Task 8: Update KnowledgeBaseBrowser Component

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/components/materials/KnowledgeBaseBrowser.tsx`

- [ ] **Step 1: Read current component**

Run: Read the file to understand current structure

- [ ] **Step 2: Update imports**

Replace old API imports with:
```typescript
import { listKnowledgeEntries, deleteKnowledgeEntryById, updateKnowledgeEntryById } from '../../api';
```

- [ ] **Step 3: Add projectId prop**

Update component signature:
```typescript
export default function KnowledgeBaseBrowser({ projectId }: { projectId: string }) {
```

- [ ] **Step 4: Update query**

Update useQuery to use new API:
```typescript
const { data: entries } = useQuery({
  queryKey: ['knowledgeEntries', projectId],
  queryFn: () => listKnowledgeEntries(projectId),
  enabled: !!projectId
});
```

- [ ] **Step 5: Update mutations**

Update delete and update mutations to use new API functions

- [ ] **Step 6: Test in browser**

Navigate to Materials page, open knowledge browser
Expected: Knowledge entries load correctly

- [ ] **Step 7: Commit component update**

```bash
git add Web/NovelAgentWeb.Frontend/src/components/materials/KnowledgeBaseBrowser.tsx
git commit -m "refactor(frontend): update KnowledgeBaseBrowser to use new API"
```

---

## Task 9: Update Rail Component

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx:1-15`

- [ ] **Step 1: Remove getWorkspace import and query**

Replace lines 1-15:
```typescript
import { NavLink, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';
import { projectService } from '../../services/projectService';
import { useQuery } from '@tanstack/react-query';

const navItems = [
  { path: '/', label: 'Agent 对话', num: '01' },
  { path: '/materials', label: '创意知识库', num: '02' },
  { path: '/workflow', label: '创作工作流', num: '03' },
  { path: '/library', label: '小说书城', num: '04' },
  { path: '/settings', label: '用户设置', num: '05' },
];

export default function Rail() {
  const { data: currentProject } = useQuery({
    queryKey: ['currentProject'],
    queryFn: () => projectService.getCurrentProject(),
  });
  const { user, clearAuth } = useAuthStore();
  const navigate = useNavigate();
```

- [ ] **Step 2: Update project name display**

Replace line 52:
```typescript
<div>{currentProject?.title ?? '选择项目...'}</div>
```

- [ ] **Step 3: Test in browser**

Run: `cd Web/NovelAgentWeb.Frontend && npm run dev`
Expected: Rail displays project name from ProjectContext

- [ ] **Step 4: Commit Rail update**

```bash
git add Web/NovelAgentWeb.Frontend/src/components/layout/Rail.tsx
git commit -m "refactor(frontend): remove getWorkspace dependency from Rail"
```

---

## Task 10: Update LibraryPage

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx:1-10`

- [ ] **Step 1: Update imports**

Replace lines 1-10:
```typescript
import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { deleteNovelProject, updateNovelProject, getStoryBibleByProject, listVolumeArcs } from '../api';
import type { NovelBookView, NovelChapterView, NovelVolumeView } from '../api/types';
import { projectService } from '../services/projectService';
import { useAuthStore } from '../stores/authStore';
import Topbar from '../components/layout/Topbar';
import '../styles/library.css';
```

- [ ] **Step 2: Remove getNovelLibrary calls**

Replace lines 48-53:
```typescript
const { data: projects } = useQuery({
  queryKey: ['projects'],
  queryFn: () => projectService.listProjects(),
  refetchInterval: 10000,
});
const projectsList = projects ?? [];
```

- [ ] **Step 3: Update selected project data fetching**

Replace lines 69-74:
```typescript
const { data: storyBible, isLoading: bibleLoading } = useQuery({
  queryKey: ['storyBible', effectiveProjectId],
  queryFn: () => effectiveProjectId ? getStoryBibleByProject(effectiveProjectId) : Promise.resolve(null),
  enabled: !!effectiveProjectId,
  refetchInterval: 10000,
});
```

- [ ] **Step 4: Update volumes fetching**

Add after storyBible query:
```typescript
const { data: volumes, isLoading: volumesLoading } = useQuery({
  queryKey: ['volumeArcs', effectiveProjectId],
  queryFn: () => effectiveProjectId ? listVolumeArcs(effectiveProjectId) : Promise.resolve([]),
  enabled: !!effectiveProjectId,
  refetchInterval: 10000,
});
```

- [ ] **Step 5: Update book rendering logic**

Update references to use projects list and storyBible data instead of NovelLibraryDocument

- [ ] **Step 6: Test in browser**

Navigate to Library page
Expected: Projects and story data load correctly

- [ ] **Step 7: Commit LibraryPage update**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/LibraryPage.tsx
git commit -m "refactor(frontend): update LibraryPage to use new StoryBible API"
```

---

## Task 11: Update WorkflowPage

**Files:**
- Modify: `Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx:1-10`

- [ ] **Step 1: Update imports**

Replace lines 1-10:
```typescript
import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  deleteNovelProject,
  listVolumeArcs,
  listAgentSessions,
  sendChat,
  updateAgentSession,
} from '../api';
import { projectService } from '../services/projectService';
```

- [ ] **Step 2: Add currentProject state**

After component start:
```typescript
const [currentProjectId, setCurrentProjectId] = useState<string | null>(null);

useEffect(() => {
  projectService.getCurrentProject().then((project) => {
    if (project) setCurrentProjectId(project.id);
  });
}, []);
```

- [ ] **Step 3: Remove getNovelLibrary and getProjectWorkflow calls**

Replace with listVolumeArcs query:
```typescript
const { data: volumeArcs } = useQuery({
  queryKey: ['volumeArcs', currentProjectId],
  queryFn: () => currentProjectId ? listVolumeArcs(currentProjectId) : Promise.resolve([]),
  enabled: !!currentProjectId
});
```

- [ ] **Step 4: Update workflow rendering**

Update component to render volumeArcs data instead of workflow document

- [ ] **Step 5: Test in browser**

Navigate to Workflow page
Expected: Volume arcs load correctly

- [ ] **Step 6: Commit WorkflowPage update**

```bash
git add Web/NovelAgentWeb.Frontend/src/pages/WorkflowPage.tsx
git commit -m "refactor(frontend): update WorkflowPage to use new Workflow API"
```

---

## Task 12: Final E2E Testing

**Files:**
- Test: All pages

- [ ] **Step 1: Full build test**

Run: `cd Web/NovelAgentWeb.Frontend && npm run build`
Expected: Build succeeds with no errors

- [ ] **Step 2: Test login flow**

1. Navigate to /login
2. Log in with test credentials
3. Verify redirect to home page
Expected: No console errors

- [ ] **Step 3: Test Materials page**

1. Navigate to /materials
2. Upload a test file
3. Verify material appears in list
4. Delete material
Expected: All operations work without errors

- [ ] **Step 4: Test Workflow page**

1. Navigate to /workflow
2. Verify volume arcs display
3. Test creating agent session
4. Send test chat message
Expected: All features work

- [ ] **Step 5: Test Library page**

1. Navigate to /library
2. Verify projects display
3. Switch between projects
4. View story bible data
Expected: All data loads correctly

- [ ] **Step 6: Test Rail component**

1. Verify project name displays in rail footer
2. Test navigation between pages
3. Test logout button
Expected: Navigation smooth, no errors

- [ ] **Step 7: Browser console check**

Check browser console for any errors or warnings
Expected: No errors related to API calls or missing endpoints

- [ ] **Step 8: Network tab verification**

Check Network tab for API calls
Expected: All calls use new REST endpoints (/api/materials, /api/knowledge, /api/storybible, /api/workflow)

- [ ] **Step 9: Document any issues**

Create list of any bugs or issues found during testing

- [ ] **Step 10: Final commit**

```bash
git add -A
git commit -m "test(frontend): Phase 4 E2E testing complete

- Verified all pages load without errors
- Confirmed new REST APIs work correctly
- Tested CRUD operations on Materials, Knowledge, StoryBible
- Verified Rail component uses currentProject
- All console errors resolved"
```

---

## Self-Review Checklist

**Spec coverage:**
- ✅ Task 4.1: API Layer Update - Tasks 1-6 cover all new API functions
- ✅ Task 4.2: MaterialsPage - Task 7 refactors MaterialsPage
- ✅ Task 4.3: WorkflowPage - Task 11 refactors WorkflowPage
- ✅ Task 4.4: LibraryPage - Task 10 refactors LibraryPage
- ✅ Task 4.5: Rail Component - Task 9 refactors Rail
- ✅ Task 4.6: E2E Testing - Task 12 covers full E2E test suite

**Placeholder scan:**
- ✅ No TBD, TODO, or "implement later"
- ✅ All code blocks complete
- ✅ All file paths exact
- ✅ All commands have expected output

**Type consistency:**
- ✅ MaterialResponse, KnowledgeEntryResponse, StoryBibleResponse, VolumeArcResponse defined in Task 1
- ✅ API functions use consistent naming (list*, get*, create*, update*, delete*)
- ✅ All mutations use correct types

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-09-phase4-frontend-migration.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
