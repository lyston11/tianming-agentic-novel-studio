import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const workflowPage = readFileSync(join(__dirname, '../src/pages/WorkflowPage.tsx'), 'utf8');
const workflowCss = readFileSync(join(__dirname, '../src/styles/workflow.css'), 'utf8');

assert.match(
  workflowPage,
  /className="workflow-production-dashboard"/,
  'workflow step tab must group creative input, production chains, versions, evidence, and audits into one production dashboard',
);

assert.match(
  workflowPage,
  /className="workflow-production-dashboard-grid"/,
  'workflow production dashboard must use a structured grid instead of horizontal stacked rows',
);

assert.match(
  workflowPage,
  /className="workflow-production-dashboard-audit"/,
  'low-signal production audit details must live in a secondary collapsed audit area',
);

assert.doesNotMatch(
  workflowPage,
  /<details className="workflow-production-chains workflow-production-chain-audit"/,
  'canonical production chains must render as the primary workflow content, not as a collapsed audit details block',
);

assert.doesNotMatch(
  workflowPage,
  /生产链路审计/,
  'canonical production chains should not be labelled as audit/debug content in the main workflow tab',
);

assert.match(
  workflowPage,
  /天命生产链/,
  'workflow step tab must present the canonical Tianming production chain as a first-class product surface',
);

assert.match(
  workflowCss,
  /\.workflow-production-chain-primary\b/,
  'primary production chain layout styles must exist separately from low-priority audit details',
);

assert.match(
  workflowCss,
  /\.workflow-production-dashboard\s*\{/,
  'workflow production dashboard must have first-class layout styles',
);

assert.match(
  workflowPage,
  /<details className="workflow-production-evidence-board workflow-production-evidence-audit"/,
  'production evidence must be a collapsed audit details block, not a default-open content section',
);

assert.doesNotMatch(
  workflowPage,
  /<section className="workflow-production-evidence-board"/,
  'production evidence must not render as a default-open section in the main workflow tab',
);

assert.doesNotMatch(
  workflowPage,
  /\[\s*['"]artifacts['"]\s*,\s*['"]产物日志['"]\s*\]/,
  'chapter detail tabs must not expose a separate product log tab; audit evidence belongs under the workflow step details',
);

assert.doesNotMatch(
  workflowPage,
  /chapterDetailTab\s*===\s*['"]artifacts['"]/,
  'product log must not render as a primary chapter detail panel',
);

assert.doesNotMatch(
  workflowPage,
  /<section className="workflow-tab-panel">\s*\{renderChapterCanonicalSummary\(selectedChapter\)\}\s*\{renderCreativeInbox\(selectedCreativeIntents\)\}\s*\{renderProductionChains\(selectedProductionChains, selectedChapter\)\}/,
  'workflow step tab must not render production context as loose sibling blocks before the chapter flow',
);
