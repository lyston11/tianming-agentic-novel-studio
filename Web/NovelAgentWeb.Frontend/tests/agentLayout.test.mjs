import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));
const agentPage = readFileSync(join(__dirname, '../src/pages/AgentPage.tsx'), 'utf8');
const agentCss = readFileSync(join(__dirname, '../src/styles/agent.css'), 'utf8');
const chatStore = readFileSync(join(__dirname, '../src/stores/useChatStore.ts'), 'utf8');

assert.doesNotMatch(
  agentPage,
  /agent-task-strip/,
  'runtime/tool execution blocks must not render in a floating strip above the input box',
);

assert.doesNotMatch(
  agentPage,
  /taskStripRef/,
  'chat input spacing must not depend on a floating task strip',
);

assert.doesNotMatch(
  agentPage,
  /\|\s*\{\s*kind:\s*'execution'/,
  'runtime/tool execution blocks must not be ordinary conversation items',
);

assert.doesNotMatch(
  agentPage,
  /if \(item\.kind === 'execution'\) \{\s+return renderExecutionBlock\(item\.block\);/m,
  'runtime/tool execution blocks must not render inline in the chat thread',
);

assert.doesNotMatch(
  agentPage,
  /const executionKey = executionBlocks/,
  'background task updates must not force the chat thread to auto-scroll',
);

assert.match(
  agentPage,
  /anchorMessageId/,
  'background task blocks must remember the message/turn that started them',
);

assert.match(
  agentPage,
  /runAnchorMessageIdsRef/,
  'runtime run ids must map back to the originating message so later events do not move to a newer turn',
);

assert.doesNotMatch(
  agentPage,
  /const resolved = mapped \|\| anchorMessageId \|\| latestTurnAnchorMessageIdRef\.current/,
  'events for an existing runtime run must not silently fall back to the newest user message',
);

assert.doesNotMatch(
  agentPage,
  /\|\| latestUserMessageId;/,
  'unanchored execution blocks must not be rendered under the newest user message because old runs can replay after a newer turn',
);

assert.match(
  agentPage,
  /sourceMessageId/,
  'runtime runs and SSE events must carry sourceMessageId so frontend can attach events to the correct user turn',
);

assert.match(
  agentPage,
  /agent-turn-executions/,
  'background task blocks must render under their owning chat turn, not as a global dock near the input',
);

assert.match(
  agentPage,
  /messageExecutionBlocks/,
  'chat rendering must place task blocks by message id',
);

assert.doesNotMatch(
  chatStore,
  /id:\s*`agent-\$\{Date\.now\(\)\}`/,
  'agent messages must not use Date.now() alone as the React key source because fast background replies can collide',
);

assert.match(
  chatStore,
  /createLocalMessageId/,
  'local chat messages need a monotonic id factory so rapid SSE replies still render with unique React keys',
);

assert.doesNotMatch(
  agentPage,
  /const\s+userMessageId\s*=\s*`user-\$\{Date\.now\(\)\}`/,
  'submitted user messages must use the same monotonic local id factory as the chat store, not Date.now() alone',
);

assert.match(
  agentPage,
  /const\s+userMessageId\s*=\s*addUserMessage\(sessionId,\s*msg\)/,
  'the clientMessageId sent to the backend should be the actual id returned by addUserMessage',
);

assert.match(
  agentPage,
  /chatItemRenderKey/,
  'chat item rendering needs a defensive key helper so previously duplicated message ids do not break React reconciliation',
);

assert.match(
  agentPage,
  /function latestUserMessageIdFromTurns[\s\S]*turns\[index\]\.turnId\?\.trim\(\)/,
  'execution anchors rebuilt from chat history must use persisted turnId instead of role-createdAt-index fallback keys',
);

assert.doesNotMatch(
  agentPage,
  /message-agent-\$\{Date\.now\(\)\}/,
  'chat render keys must not use the old message-agent-Date.now shape that produced duplicate React keys in fast SSE replies',
);

assert.doesNotMatch(
  agentPage,
  /key=\{item\.id\} className=\{`agent-turn/,
  'chat turns must not use the raw message id as the only React key because old in-memory duplicate ids can still exist after HMR',
);

assert.doesNotMatch(
  agentPage,
  /className=\{`agent-task-dock/,
  'background task blocks must not use a single global task dock that can steal completed work from previous turns',
);

assert.match(
  agentPage,
  /executionBlockOutputLabel/,
  'task cards must explain whether they reply in chat or update another workspace surface',
);

assert.match(
  agentPage,
  /compactExecutionEvents/,
  'expanded task cards must compact repeated heartbeat/stage events instead of rendering every duplicate production update',
);

assert.match(
  agentPage,
  /productionStageTrackItems/,
  'ProduceChapter progress must render as one ordered closed-loop stage track instead of loose event cards',
);

assert.match(
  agentPage,
  /interface ProductionStageTrackGroup/,
  'ProduceChapter progress must preserve separate chapter/production chains inside one runtime turn',
);

assert.match(
  agentPage,
  /function productionStageTrackGroups/,
  'production progress grouping must be keyed by productionRunId/chapterId/packageId instead of one global track',
);

assert.match(
  agentPage,
  /productionKey\?: string/,
  'runtime production events must retain productionKey metadata so multiple chapter chains do not get merged',
);

assert.match(
  agentPage,
  /const productionKey = productionRunId \|\| chapterId \|\| packageId \|\| '';/,
  'production progress grouping must prefer the stable production run or chapter id before package id so rebuild/commit package changes do not split one chapter chain',
);

assert.match(
  agentPage,
  /productionStageTrackGroups\(block\.events\)/,
  'rendered execution blocks must display grouped production tracks, not a single mixed global track',
);

assert.doesNotMatch(
  agentPage,
  /const productionTrack = productionStageTrackItems\(block\.events\);/,
  'execution blocks must not collapse every chapter production event into one shared track',
);

assert.match(
  agentPage,
  /agent-production-stage-track/,
  'chapter production progress needs a dedicated stage-track UI inside the owning execution block',
);

assert.match(
  agentPage,
  /executionVisibleEvents\(block\)/,
  'expanded execution blocks must use the shared visible-event filter',
);

assert.match(
  agentPage,
  /event\.type === 'production_progress'\) return false/,
  'raw production_progress heartbeat events must be removed from the expanded generic event list',
);

assert.match(
  agentPage,
  /executionBlockTitleFromEvents/,
  'execution block titles must be derived from the whole closed-loop run, not the latest production event only',
);

assert.match(
  agentPage,
  /mergeExecutionBlockEvents/,
  'execution blocks must preserve the whole ProduceChapter stage history instead of truncating the production run to a few tail events',
);

assert.doesNotMatch(
  agentPage,
  /\[\.\.\.current\.events,\s*event\]\.slice\(-8\)/,
  'chapter production blocks must not keep only the last 8 raw events because the closed loop has more stages than that',
);

assert.match(
  agentPage,
  /isProductionClosureComplete/,
  'a ProduceChapter block may say the whole closed loop is complete only after the final post-commit stages are recorded',
);

assert.match(
  agentPage,
  /resolveExecutionBlockStatus/,
  'terminal run_update or agent_reply events must not mark a ProduceChapter block done until the production closure is complete',
);

assert.match(
  agentPage,
  /terminalState\?: 'completed' \| 'failed' \| 'cancelled'/,
  'runtime events must preserve terminal run state so cancelled production runs can be shown as stopped instead of still running',
);

assert.match(
  agentPage,
  /function hasCancelledRuntimeUpdate/,
  'execution block status resolution needs an explicit cancelled-run detector',
);

assert.match(
  agentPage,
  /function isTerminalRuntimeMessage/,
  'SSE run_update events sometimes replay without structured data, so terminal Chinese runtime messages must still close the task block',
);

assert.match(
  agentPage,
  /isTerminalRuntimeMessage\(message\)/,
  'terminal runtime detection must inspect the run_update message when event data/status is missing',
);

assert.match(
  agentPage,
  /if \(hasCancelledRuntimeUpdate\(events\)\) return 'done';/,
  'cancelled runtime runs must close their execution block even when an older production stage heartbeat is still running',
);

assert.match(
  agentPage,
  /if \(hasCancelledRuntimeUpdate\(events\)\) return '后台执行已取消';/,
  'cancelled runtime runs must render a clear cancelled title instead of a generic running production title',
);

assert.match(
  agentPage,
  /block\.status = resolveExecutionBlockStatus\(block\.events,\s*status,\s*block\.status\)/,
  'runtime-event replay must use the same production closure status resolver as live SSE events',
);

assert.match(
  chatStore,
  /turn\.turnId/,
  'server chat turns must use the persisted turnId as the primary frontend message id',
);

assert.match(
  chatStore,
  /turn\.turnIndex/,
  'server chat turns must retain turnIndex as a stable fallback when older API responses do not include turnId',
);

assert.match(
  agentPage,
  /const nextStatus = resolveExecutionBlockStatus\(events,\s*status,\s*current\.status\)/,
  'execution block status must be derived from the full event set so a completed runtime run cannot close an unfinished production track',
);

{
  const outputLabelStart = agentPage.indexOf('function executionBlockOutputLabel');
  const productionLabelIndex = agentPage.indexOf('block.events.some(isProductionProgressEvent)', outputLabelStart);
  const chatReplyLabelIndex = agentPage.indexOf("block.events.some((event) => event.type === 'agent_reply')", outputLabelStart);
  assert.ok(
    outputLabelStart >= 0 && productionLabelIndex >= 0 && chatReplyLabelIndex >= 0 && productionLabelIndex < chatReplyLabelIndex,
    'execution block output labels must prioritize production/library destinations over generic chat replies',
  );
}

assert.doesNotMatch(
  agentPage,
  /const status = failed \? 'failed' : 'done';\s*\n\s*return \{\s*\n\s*\.\.\.block,\s*\n\s*status,/,
  'markExecutionBlockDone must keep unfinished production closures running instead of forcing done',
);

assert.doesNotMatch(
  agentPage,
  /if \(status === 'done'\) return '章节生产闭环已完成';/,
  'a terminal runtime update alone must not relabel a committed chapter as a fully completed production closed loop',
);

assert.doesNotMatch(
  agentPage,
  /event\.stage === 'chaptercommitted'/,
  'execution destinations must normalize production stage names before deciding that a chapter has entered the bookstore',
);

assert.match(
  agentPage,
  /normalizeProductionStageKey\(event\.stage\) === 'chaptercommitted'/,
  'chapter commit destination detection must work for both ChapterCommitted and chaptercommitted stage spellings',
);

assert.match(
  agentPage,
  /executionVisibleEvents/,
  'expanded production blocks should hide generic runtime updates and show the dedicated stage track instead',
);

assert.match(
  agentPage,
  /isProductionToolProgressEvent/,
  'expanded production blocks must hide generic ProduceChapter tool progress cards once the ordered stage track is available',
);

assert.doesNotMatch(
  agentPage,
  /const visibleEvents = block\.expanded \? block\.events : \[\];/,
  'execution details must not dump every raw event in one long vertical list',
);

assert.match(
  agentPage,
  /后台推进|会回复你|需要确认/,
  'task cards must distinguish background work from user-facing replies',
);

assert.doesNotMatch(
  agentCss,
  /\.agent-task-strip\b/,
  'floating task strip styles should be removed so execution UI follows the message flow',
);

assert.match(
  agentCss,
  /\.agent-turn-executions\b/,
  'anchored background task block styles must exist',
);

assert.doesNotMatch(
  agentPage,
  /label:\s*`读 \$\{memoryScopeLabel\(read\.memoryScope\)\}`/,
  'memory audit chips must not look like clickable tool-call actions such as “读执行”',
);

assert.doesNotMatch(
  agentPage,
  /agent-memory-audit-line|memoryAuditChips|本轮记忆审计/,
  'memory audit/debug chips must not render as ordinary chat content',
);

assert.doesNotMatch(
  agentPage,
  /agent-memory-line|shouldShowMemoryLine/,
  'internal memory and mission state labels must not render under ordinary agent replies',
);

const expectedProductionStages = [
  ['packagebuilt', '构建章节生产包'],
  ['draftgenerated', '生成章节正文'],
  ['changesextracted', '抽取章节 CHANGES'],
  ['gatevalidated', '校验章节草稿'],
  ['draftrewritten', '修复章节草稿'],
  ['reviewcompleted', 'Agent 质量评审'],
  ['chaptercommitted', '提交书城'],
  ['factspersisted', '沉淀连续性事实'],
  ['indexupdated', '刷新生产索引'],
];

for (const [stage, label] of expectedProductionStages) {
  assert.match(
    agentPage,
    new RegExp(`${stage}:\\s*'${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}'`),
    `production progress stage ${stage} must render as a readable chat label`,
  );
}
