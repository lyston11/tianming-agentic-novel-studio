import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const apiSource = readFileSync(join(__dirname, '../src/api/index.ts'), 'utf8');
const appendTurnHelper = apiSource.match(
  /export const appendNovelAgentTurn[\s\S]*?export const listAccessibleNovelAgentProjects/,
)?.[0];

assert.ok(appendTurnHelper, 'appendNovelAgentTurn helper should remain part of the API module');
assert.match(
  appendTurnHelper,
  /appendNovelAgentTurn = \(\s*sessionId: string,\s*request: AppendNovelAgentTurnRequest/,
  'conversation turns should accept only server-resolved session scope',
);
assert.match(
  appendTurnHelper,
  /`\/novel-agent\/conversations\/\$\{encodeURIComponent\(sessionId\)\}\/turns`/,
  'conversation turns should use the session-scoped endpoint',
);
assert.doesNotMatch(
  appendTurnHelper,
  /projectId/,
  'conversation turns must not expose or append a caller-supplied projectId',
);
