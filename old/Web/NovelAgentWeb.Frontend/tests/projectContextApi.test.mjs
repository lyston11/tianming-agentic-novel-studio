import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const apiSource = readFileSync(join(__dirname, '../src/api/index.ts'), 'utf8');
const projectContextHelpers = apiSource.match(
  /export const listAccessibleNovelAgentProjects[\s\S]*?export const confirmNovelAgentProposal/,
)?.[0];

assert.ok(projectContextHelpers, 'project context helpers should remain part of the API module');
assert.match(
  projectContextHelpers,
  /listAccessibleNovelAgentProjects = \(\) =>[\s\S]*get<AccessibleProjectCatalogItem\[\]>\('\/novel-agent\/projects\/accessible'\)/,
  'project discovery should use the authenticated minimal metadata endpoint',
);
assert.match(
  projectContextHelpers,
  /activateNovelAgentProjectContext = \([\s\S]*`\/novel-agent\/conversations\/\$\{encodeURIComponent\(sessionId\)\}\/project-context\/activate`/,
  'activation should remain session-scoped',
);
assert.match(
  projectContextHelpers,
  /'Idempotency-Key': request\.idempotencyKey/,
  'activation should carry the caller idempotency key as a header',
);
assert.match(
  projectContextHelpers,
  /expectedBindingVersion: request\.expectedBindingVersion/,
  'activation should carry the server binding version observed by the caller',
);
assert.match(
  projectContextHelpers,
  /sourceUserMessageId: request\.sourceUserMessageId[\s\S]*confirmationActionId: request\.confirmationActionId/,
  'activation should preserve an auditable confirmation source',
);
assert.doesNotMatch(
  projectContextHelpers,
  /projectId: request\.projectId[\s\S]*idempotencyKey: request\.idempotencyKey/,
  'idempotency key must stay in the request header rather than the API body',
);
