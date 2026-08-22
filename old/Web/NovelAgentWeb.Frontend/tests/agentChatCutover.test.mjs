import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const agentPageSource = readFileSync(join(__dirname, '../src/pages/AgentPage.tsx'), 'utf8');

assert.match(
  agentPageSource,
  /await appendNovelAgentTurn\(sessionId, \{\s*idempotencyKey: userMessageId,\s*content: msg,?\s*\}\)/,
  'active chat submit must persist turns through the Application Conversation API',
);
assert.doesNotMatch(
  agentPageSource,
  /sendChat\(/,
  'AgentPage must no longer call the legacy /agent/chat entry for chat turns',
);
assert.match(
  agentPageSource,
  /res\.decision\.message/,
  'agent reply must come from the durable Conversation decision',
);
