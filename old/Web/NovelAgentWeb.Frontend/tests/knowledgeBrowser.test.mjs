import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const browser = readFileSync(join(__dirname, '../src/components/materials/KnowledgeBaseBrowser.tsx'), 'utf8');
const api = readFileSync(join(__dirname, '../src/api/index.ts'), 'utf8');
const page = readFileSync(join(__dirname, '../src/pages/MaterialsPage.tsx'), 'utf8');

assert.match(
  browser,
  /useQuery<KnowledgeResponse\[\], Error>\(\{[\s\S]*?queryFn:\s*\(\)\s*=>\s*listKnowledgeEntries\(projectId\),\s*\}\);/,
  'knowledge browser must load user-level entries even when no project is selected',
);

assert.match(
  api,
  /listKnowledgeEntries\s*=\s*\(projectId\?:\s*string\)/,
  'knowledge list API should accept an optional project id for user-level browsing',
);

assert.match(
  api,
  /projectId\?\.trim\(\)\s*\?\s*get<KnowledgeResponse\[\]>\(`\/knowledge\?projectId=/,
  'knowledge list API should only append projectId when a project is actually selected',
);

assert.match(
  page,
  /projectId=\{currentProjectId \|\| undefined\}/,
  'materials page should pass undefined instead of an empty project id to the knowledge browser',
);

assert.match(
  browser,
  /['"]?hard_fact['"]?\s*:\s*['"]HardFact['"]/,
  'knowledge browser should normalize legacy snake_case categories before grouping directories',
);

assert.match(
  browser,
  /isLoading:\s*entriesLoading/,
  'knowledge browser must keep entry loading state instead of rendering an empty list while entries are still loading',
);

assert.match(
  browser,
  /正在加载知识条目/,
  'knowledge browser should show a loading state before declaring the current directory empty',
);

assert.match(
  browser,
  /totalEntryCount/,
  'knowledge browser should derive the headline entry total from directory stats while entries are loading',
);

assert.match(
  browser,
  /refetch:\s*refetchEntries/,
  'knowledge browser should keep a handle for recovering an inconsistent empty entry response',
);

assert.match(
  browser,
  /hasDirectoryEntriesButNoLoadedEntries\s*=[\s\S]*rawEntries\.length\s*===\s*0[\s\S]*directoryEntryTotal\s*>\s*0/,
  'knowledge browser should detect when directory counts prove entries exist but the entry list is empty',
);

assert.match(
  browser,
  /hasDirectoryEntriesButNoLoadedEntries[\s\S]*void\s+refetchEntries\(\)/,
  'knowledge browser should refetch once when directory counts prove entries exist but the entry list is empty',
);

assert.match(
  browser,
  /searchKnowledgeEntries\(\{[\s\S]*projectId:[\s\S]*query:[\s\S]*topK:/,
  'knowledge browser should call the semantic search API for project-scoped queries',
);

assert.match(
  browser,
  /queryFn:\s*\(\{\s*signal\s*\}\)[\s\S]*searchKnowledgeEntries\([\s\S]*signal\)/,
  'semantic search should forward the React Query cancellation signal',
);

assert.match(
  browser,
  /semanticSearchApplied[\s\S]*semanticScores/,
  'knowledge browser should render ranked semantic search results and scores',
);

assert.doesNotMatch(
  browser,
  /条目会移动到[“"]未分类/,
  'directory deletion must not promise an automatic entry migration that the backend does not perform',
);

assert.match(
  browser,
  /请先将条目移动到其他目录，再删除空目录/,
  'non-empty directory deletion should guide the user to move entries first',
);

assert.match(
  page,
  /getKnowledgeTask/,
  'materials page should poll the knowledge processing task endpoint after upload',
);

assert.match(
  page,
  /refetchInterval:[\s\S]*TERMINAL_KNOWLEDGE_TASK_STATUSES/,
  'knowledge task polling should stop after a terminal status',
);

assert.match(
  page,
  /queryKey:\s*\[['"]knowledgeTask['"],\s*lastKnowledgeTask\?\.projectId,\s*lastKnowledgeTask\?\.taskId\]/,
  'knowledge task polling must be scoped by both project and task',
);

assert.match(
  page,
  /hasActiveKnowledgeTask[\s\S]*uploadKnowledgeMutation\.mutate\(\{\s*projectId:\s*currentProjectId,\s*file\s*\}\)/,
  'materials page must not replace an in-flight task with a second upload from the same project',
);

assert.doesNotMatch(
  page,
  /可在 Agent 对话中发送：处理知识文件/,
  'uploaded knowledge files are processed automatically and must not require a manual Agent command',
);
