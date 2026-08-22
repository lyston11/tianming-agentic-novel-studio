import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const apiSource = readFileSync(join(__dirname, '../src/api/index.ts'), 'utf8');
const createSessionHelper = apiSource.match(
  /export const createAgentSession[\s\S]*?export const getAgentSession/,
)?.[0];

assert.ok(createSessionHelper, 'createAgentSession helper should remain part of the API module');
assert.match(
  createSessionHelper,
  /export const createAgentSession = \(\) =>[\s\S]*api<AgentSessionInfo>\('\/agent\/session'/,
  'new sessions must use the unbound POST /agent/session contract',
);
assert.doesNotMatch(
  createSessionHelper,
  /projectId/,
  'session creation must not expose or append an initial projectId',
);
