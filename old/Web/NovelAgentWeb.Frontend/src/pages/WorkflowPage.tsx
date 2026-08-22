import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import {
  compareChapterVersions,
  deleteNovelProject,
  getChapterVersions,
  getProjectWorkflow,
  getWorkspace,
  listAgentSessions,
  sendChat,
  updateNovelProject,
} from '../api';
import type {
  AgentMissionPlan,
  AgentScheduledTask,
  AgentSessionSummary,
  ChapterVersionDiffBlock,
  ChapterVersionProductionAlignment,
  ChapterVersionResponse,
  NovelBookView,
  NovelChapterView,
  NovelProjectInfo,
  NovelVolumeView,
  WorkflowArtifactTimelineItem,
  WorkflowAgentReviewCheckEvidence,
  WorkflowAgentReviewSummaryEvidence,
  WorkflowCreativeIntentEvidence,
  WorkflowFactSnapshotEvidence,
  WorkflowGateEvidence,
  WorkflowGoalTaskState,
  WorkflowKnowledgeBindingEvidence,
  WorkflowKnowledgeBindingSummaryEvidence,
  WorkflowKnowledgeConstraintEvidence,
  WorkflowMemoryPromotionEvidence,
  WorkflowMemoryReadEvidence,
  WorkflowProductionEventSummary,
  WorkflowProductionChain,
  WorkflowProductionStage,
  WorkflowToolExecutionSummary,
  WorkflowRollbackEvidence,
  WorkflowRevisionPlanEvidence,
} from '../api/types';
import Topbar from '../components/layout/Topbar';
import { useProjectStore } from '../stores/useProjectStore';
import ProjectGoalWorkbench from './workflow/ProjectGoalWorkbench';
import '../styles/workflow.css';

type ChapterDetailTab = 'manuscript' | 'workflow' | 'runtime' | 'issues';
type ChapterDetailTabOption = readonly [ChapterDetailTab, string];
type ChapterProgressStatus = 'done' | 'running' | 'blocked' | 'drafting' | 'unstarted';

interface WorkflowContextMenuState {
  book: NovelBookView;
  x: number;
  y: number;
}

interface WorkflowDialogState {
  mode: 'rename';
  book: NovelBookView;
}

interface ChapterProgressStep {
  key: string;
  label: string;
  status: ChapterProgressStatus;
  caption: string;
  artifactId?: string;
  summary: string;
}

function progressPercent(book: NovelBookView) {
  if (book.plannedChapterCount <= 0) return 0;
  return Math.min(100, Math.round((book.generatedChapterCount / book.plannedChapterCount) * 100));
}

function missionChapters(plan?: AgentMissionPlan | null) {
  return plan?.bookTaskTree?.volumes.flatMap((volume) => volume.chapters) ?? [];
}

function missionStageLabel(plan?: AgentMissionPlan | null) {
  const value = plan?.status || plan?.stage || 'active';
  const labels: Record<string, string> = {
    foundation: '故事地基',
    volume_planning: '卷规划',
    chapter_work: '章节推进',
    awaiting_confirmation: '待确认',
    blocked: '阻塞',
    completed: '完成',
    active: '推进中',
    idle: '待启动',
  };
  return labels[value] ?? value;
}

function taskStatusLabel(status: string) {
  const labels: Record<string, string> = {
    queued: '排队',
    running: '运行中',
    blocked: '阻塞',
    waiting_confirmation: '待确认',
    paused: '暂停',
    done: '完成',
  };
  return labels[status] ?? status;
}

function taskStatusGroup(status?: string | null) {
  const normalized = (status || 'queued').toLowerCase();
  if (normalized === 'running') return 'running';
  if (normalized === 'blocked') return 'blocked';
  if (normalized === 'waiting_confirmation') return 'waiting';
  if (normalized === 'done' || normalized === 'completed') return 'done';
  return 'queued';
}

function taskActionLabel(task: AgentScheduledTask) {
  const action = task.nextAction || task.taskType || '';
  const labels: Record<string, string> = {
    SelectChapterCandidate: '选择章节方案',
    ProduceChapter: '生成正文',
    ValidateChapterDraft: '结构门禁校验',
    ReviewChapter: '总编质量评审',
    CommitValidatedChapter: '入库发布',
    BuildChapterPackage: '整理连续性资料',
    RepairChapterDraft: '修订草稿',
  };
  return labels[action] ?? labels[task.taskType] ?? (action || '等待调度');
}

function taskRiskLabel(risk?: string | null) {
  const labels: Record<string, string> = {
    low: '低风险',
    medium: '中风险',
    high: '高风险',
    critical: '严重风险',
  };
  return risk ? labels[risk.toLowerCase()] ?? risk : '';
}

function taskChapterLabel(chapterId?: string | null) {
  if (!chapterId) return '项目任务';
  const match = chapterId.match(/chapter[-_ ]?(\d+)/i) ?? chapterId.match(/第\s*(\d+)\s*章/);
  if (!match?.[1]) return '章节任务';
  const number = Number.parseInt(match[1], 10);
  return Number.isFinite(number) && number > 0 ? `第 ${number} 章` : '章节任务';
}

function taskFocusLabel(task?: AgentScheduledTask | null) {
  if (!task) return '暂无运行任务';
  const status = taskStatusGroup(task.status);
  if (status === 'running') return `正在${taskActionLabel(task)}`;
  if (status === 'blocked') return '有任务被阻塞';
  if (status === 'waiting') return '等待确认';
  return '等待 Agent 调度';
}

function scheduleHeadline(tasks: AgentScheduledTask[], counts: Record<string, number>) {
  if (tasks.length === 0) return '暂无调度任务';
  const running = counts.running ?? 0;
  const blocked = counts.blocked ?? 0;
  const waiting = counts.waiting ?? 0;
  const queued = counts.queued ?? 0;
  if (running > 0) return `正在执行 ${running} 个生产动作`;
  if (blocked > 0) return `${blocked} 个任务需要处理`;
  if (waiting > 0) return `${waiting} 个任务等待确认`;
  return `${queued || tasks.length} 个任务等待 Agent 推进`;
}

function compactWorkflowBrief(value?: string | null, fallback = '查看真实成稿、门禁、质量评审与过程产物。') {
  const normalized = (value ?? '')
    .replace(/\s+/g, ' ')
    .replace(/^围绕[“"]?/, '')
    .trim();
  if (!normalized) return fallback;
  const firstClause = normalized
    .split(/，按|。|；|;/)
    .map((item) => item.trim())
    .find(Boolean) ?? normalized;
  return firstClause.length > 88 ? `${firstClause.slice(0, 88)}...` : firstClause;
}

function sameLooseText(left?: string | null, right?: string | null) {
  return normalizeLabel(left) === normalizeLabel(right);
}

function chapterHeaderSummary(
  chapter?: NovelChapterView | null,
  artifact?: WorkflowArtifactTimelineItem | null,
) {
  const candidates = [
    chapter?.summary,
    chapter?.goal,
    artifact?.summary,
  ].filter((value): value is string => !!value?.trim());

  const summary = candidates.find((value) => !sameLooseText(value, chapter?.title));
  return summary || '这一章还没有可展示的正文或工作流摘要。';
}

function normalizeLabel(value?: string | null) {
  return (value ?? '').replace(/[^\p{L}\p{N}]/gu, '').toLowerCase();
}

function isGenericTitle(value: string) {
  return !value || value.includes('未命名') || value.includes('当前小说');
}

function bookActivityScore(book: NovelBookView, sessions: AgentSessionSummary[]) {
  const relatedSessions = sessions.filter((session) => session.activeProjectId === book.projectId);
  const messageCount = relatedSessions.reduce((sum, session) => sum + session.messageCount, 0);
  const emptyUntitledPenalty = isGenericTitle(normalizeLabel(book.title)) && relatedSessions.length === 0 && book.plannedChapterCount === 0 && book.generatedChapterCount === 0
    ? 500
    : 0;
  return relatedSessions.length * 120
    + messageCount * 8
    + book.plannedChapterCount * 35
    + book.generatedChapterCount * 25
    - emptyUntitledPenalty;
}

function isArchivedProject(book: NovelBookView) {
  return (book.status || '').toLowerCase() === 'archived';
}

function projectShortId(projectId?: string | null) {
  if (!projectId) return '';
  return projectId === 'default' ? 'default' : projectId.slice(0, 8);
}

function bookToProjectInfo(book: NovelBookView): NovelProjectInfo {
  return {
    id: book.projectId,
    title: book.title,
    genre: book.genre,
    subGenre: book.subGenre,
    coreHook: book.coreHook,
    readerPromise: book.readerPromise,
    status: book.status,
    coverImageUrl: book.coverImageUrl,
    createdAt: book.updatedAt,
    updatedAt: book.updatedAt,
  };
}

function artifactClass(artifact?: WorkflowArtifactTimelineItem | null) {
  const status = (artifact?.status ?? '').toLowerCase();
  if (artifact?.isFinal) return 'done';
  if (status.includes('fail') || status.includes('blocked') || status.includes('rewrite')) return 'blocked';
  if (status.includes('running') || status.includes('executing')) return 'running';
  if (artifact) return 'drafting';
  return 'unstarted';
}

function productionStageClass(stage?: WorkflowProductionStage | null) {
  const status = (stage?.status ?? '').toLowerCase();
  if (status.includes('blocked') || status.includes('fail')) return 'blocked';
  if (status.includes('ready') || status.includes('completed')) return 'done';
  if (status.includes('running') || status.includes('progress') || status.includes('awaiting')) return 'running';
  if (stage && stage.productionEvents.length > 0) return 'drafting';
  return 'unstarted';
}

function productionChainClass(chain?: WorkflowProductionChain | null) {
  const status = (chain?.status ?? '').toLowerCase();
  if (status.includes('blocked') || status.includes('fail')) return 'blocked';
  if (status.includes('completed') || status.includes('done')) return 'done';
  if (status.includes('running') || status.includes('progress')) return 'running';
  if (chain && chain.steps.length > 0) return 'drafting';
  return 'unstarted';
}

function productionStepClass(status: string) {
  const value = status.toLowerCase();
  if (value.includes('fail') || value.includes('blocked') || value.includes('invalid')) return 'blocked';
  if (value.includes('completed') || value.includes('executed') || value.includes('committed')) return 'done';
  if (value.includes('running') || value.includes('queued') || value.includes('pending')) return 'running';
  return 'drafting';
}

function productionChainStatusLabel(status: string) {
  const labels: Record<string, string> = {
    blocked: '需处理',
    running: '执行中',
    completed: '已完成',
    in_progress: '推进中',
    pending: '待处理',
  };
  return labels[status] ?? (status || '未开始');
}

function extractChapterNumber(value?: string | number | null) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : 0;
  if (!value) return 0;
  const text = String(value);
  const explicit = text.match(/chapter[-_ ]?(\d+)/i) ?? text.match(/第\s*(\d+)\s*章/);
  if (explicit?.[1]) return Number.parseInt(explicit[1], 10) || 0;
  const trailing = text.match(/(\d+)(?!.*\d)/);
  return trailing?.[1] ? Number.parseInt(trailing[1], 10) || 0 : 0;
}

function productionChainMatchesChapter(chain: WorkflowProductionChain, chapter?: NovelChapterView | null) {
  if (!chapter) return true;
  if (chapter.productionSummary?.chainId && chain.id === chapter.productionSummary.chainId) return true;
  if (chain.chapterId === chapter.chapterId || chain.chapterLogicalId === chapter.chapterId) return true;

  const chapterNumber = chapter.beatIndex || extractChapterNumber(chapter.chapterId);
  if (chapterNumber <= 0) return false;
  return extractChapterNumber(chain.chapterId) === chapterNumber
    || extractChapterNumber(chain.chapterLogicalId) === chapterNumber;
}

function productionChainEvidenceChips(chain: WorkflowProductionChain) {
  const evidence = chain.evidence;
  if (!evidence) return [];

  return [
    evidence.gate?.status ? `门禁 ${evidence.gate.status}` : '',
    evidence.agentReview?.overallResult ? `验收 ${evidence.agentReview.overallResult}` : '',
    evidence.agentReview?.recommendedAction ? `动作 ${evidence.agentReview.recommendedAction}` : '',
    evidence.factSnapshot?.endingState ? `结尾 ${evidence.factSnapshot.endingState}` : '',
    evidence.chapterChangeCount > 0 ? `CHANGES ${evidence.chapterChangeCount}` : '',
  ].filter(Boolean);
}

function chapterProductionSummaryChips(chapter?: NovelChapterView | null) {
  const summary = chapter?.productionSummary;
  if (!summary?.hasCanonicalEvidence) return [];
  const creativeIntents = summary.creativeIntents ?? [];
  const acceptedCreativeCount = creativeIntents.filter((intent) =>
    ['accepted', 'adopted', 'executed', '已采纳', '已执行'].includes((intent.status || '').toLowerCase()),
  ).length;
  const executedCreativeCount = creativeIntents.filter((intent) =>
    ['executed', 'done', '已执行'].includes((intent.status || '').toLowerCase()),
  ).length;

  return [
    summary.chapterVersionId ? `章节 v${summary.chapterVersionNumber || '?'}` : '',
    summary.factSnapshotId ? `事实 v${summary.factSnapshotVersion || '?'}` : '',
    summary.gateStatus ? `门禁 ${summary.gateStatus}` : '',
    summary.agentReviewResult ? `验收 ${summary.agentReviewResult}` : '',
    summary.chapterChangeCount > 0 ? `CHANGES ${summary.chapterChangeCount}` : '',
    summary.revisionPlanCount > 0 ? `修订 ${summary.revisionPlanCount}` : '',
    summary.rebuildCount > 0 ? `重建 ${summary.rebuildCount}` : '',
    creativeIntents.length > 0 ? `创意 ${creativeIntents.length}` : '',
    acceptedCreativeCount > 0 ? `采纳 ${acceptedCreativeCount}` : '',
    executedCreativeCount > 0 ? `执行 ${executedCreativeCount}` : '',
  ].filter(Boolean);
}

function productionStageCaption(stage: WorkflowProductionStage) {
  if (stage.status === 'empty') return stage.emptyReason || stage.nextIntentHint || '未开始';
  const status = stage.status.toLowerCase();
  const isCurrentBlocked = status.includes('blocked') || status.includes('fail');
  if (!isCurrentBlocked && stage.summary) return stage.summary;
  if (stage.productionEvents.length > 0) {
    const latest = isCurrentBlocked
      ? stage.productionEvents[0]
      : stage.productionEvents.find((event) => !isBlockedProductionEvent(event));
    if (!latest) return stage.summary || stage.nextIntentHint || '等待产物';
    return `${latest.stage || latest.eventType} · ${latest.status}`;
  }
  return stage.summary || stage.nextIntentHint || '等待产物';
}

function compactEventData(dataJson: string) {
  if (!dataJson?.trim()) return '';
  try {
    const parsed = JSON.parse(dataJson) as Record<string, unknown>;
    return Object.entries(parsed)
      .filter(([, value]) => value !== null && value !== undefined && value !== '' && !(Array.isArray(value) && value.length === 0))
      .slice(0, 4)
      .map(([key, value]) => {
        if (Array.isArray(value)) return `${key}: ${value.slice(0, 2).join(' / ')}`;
        if (typeof value === 'object') return `${key}: 已记录`;
        return `${key}: ${String(value)}`;
      })
      .join(' · ');
  } catch {
    return dataJson.length > 160 ? `${dataJson.slice(0, 160)}...` : dataJson;
  }
}

function stageEventsForChapter(stage: WorkflowProductionStage, chapterId?: string | null) {
  const events = stage.productionEvents ?? [];
  const scopedEvents = chapterId
    ? events.filter((event) => event.chapterId === chapterId)
    : events;
  const matched = scopedEvents.length > 0 ? scopedEvents : events;
  const stageStatus = stage.status.toLowerCase();
  if (!stageStatus.includes('blocked') && !stageStatus.includes('fail')) {
    return matched.filter((event) => !isBlockedProductionEvent(event)).slice(0, 3);
  }
  return matched.slice(0, 3);
}

function isBlockedProductionEvent(event: WorkflowProductionEventSummary) {
  const status = event.status.toLowerCase();
  return status.includes('fail') || status.includes('blocked') || status.includes('invalid');
}

function toolExecutionStateKey(tool: WorkflowToolExecutionSummary) {
  const outputKey = tool.semanticContract?.outputKind
    || tool.semanticContract?.outputArtifacts?.join('/')
    || '';
  return outputKey || tool.toolName || tool.id;
}

function toolActivityTime(tool: WorkflowToolExecutionSummary) {
  return Math.max(parseTime(tool.completedAt), parseTime(tool.startedAt));
}

function collapseCurrentToolExecutions(tools: WorkflowToolExecutionSummary[]) {
  const byKey = new Map<string, WorkflowToolExecutionSummary>();
  tools.forEach((tool) => {
    const key = toolExecutionStateKey(tool);
    const existing = byKey.get(key);
    if (!existing || toolActivityTime(tool) >= toolActivityTime(existing)) {
      byKey.set(key, tool);
    }
  });
  return Array.from(byKey.values()).sort((a, b) => toolActivityTime(b) - toolActivityTime(a));
}

function parseTime(value: string) {
  const time = Date.parse(value);
  return Number.isFinite(time) ? time : 0;
}

function toolExecutionsForStage(stage: WorkflowProductionStage) {
  return collapseCurrentToolExecutions(stage.toolExecutions ?? []).slice(0, 3);
}

function toolExecutionDisplayName(tool: WorkflowToolExecutionSummary) {
  return tool.semanticContract?.displayName || tool.toolName || '未命名工具';
}

function toolExecutionStatusLabel(status: string) {
  const value = status.toLowerCase();
  if (value === 'awaiting_user') return '待用户验收';
  if (value === 'awaiting_decision') return '待处理';
  if (value.includes('fail')) return '失败';
  if (value.includes('cancel')) return '已取消';
  if (value.includes('running') || value.includes('execut')) return '执行中';
  if (value.includes('complete') || value.includes('success') || value.includes('succeed')) return '已完成';
  if (value.includes('pending') || value.includes('queued')) return '排队中';
  return status || '未记录';
}

function toolExecutionClass(tool: WorkflowToolExecutionSummary) {
  return executionStatusClass(tool.status);
}

function executionStatusClass(status: string) {
  const value = status.toLowerCase();
  if (value.includes('fail') || value.includes('cancel')) return 'blocked';
  if (value.includes('running') || value.includes('execut')) return 'running';
  if (value.includes('complete') || value.includes('success') || value.includes('succeed')) return 'done';
  return 'drafting';
}

function toolExecutionChips(tool: WorkflowToolExecutionSummary, kind: 'input' | 'output') {
  const values = kind === 'input'
    ? tool.semanticContract?.inputArtifacts
    : tool.semanticContract?.outputArtifacts;
  return (values ?? []).filter(Boolean).slice(0, 4);
}

function renderToolExecutions(tools: WorkflowToolExecutionSummary[]) {
  if (tools.length === 0) return null;

  return (
    <div className="workflow-tool-execution-list" aria-label="阶段工具契约">
      {tools.map((tool) => {
        const inputArtifacts = toolExecutionChips(tool, 'input');
        const outputArtifacts = toolExecutionChips(tool, 'output');
        const blockedInputArtifacts = (tool.failure?.inputArtifacts ?? [])
          .filter((artifact) => artifact.blocksExecution);
        const contract = tool.semanticContract;
        const result = tool.failure?.reason || tool.errorMessage || tool.resultMessage || contract?.resultSemantics || contract?.userVisibleWhere || '';
        return (
          <div key={tool.id} className={`workflow-tool-execution ${toolExecutionClass(tool)}`}>
            <div className="workflow-tool-execution-head">
              <span>{tool.phase || contract?.domainSurface || '工具执行'}</span>
              <strong title={tool.toolName}>{toolExecutionDisplayName(tool)}</strong>
              <em>{toolExecutionStatusLabel(tool.status)}</em>
            </div>
            <div className="workflow-tool-execution-meta">
              {tool.risk && <i>风险 {tool.risk}</i>}
              {contract?.outputKind && <i>{contract.outputKind}</i>}
              {tool.startedAt && <i>{formatTime(tool.startedAt)}</i>}
            </div>
            {(inputArtifacts.length > 0 || outputArtifacts.length > 0) && (
              <div className="workflow-tool-artifacts">
                {inputArtifacts.length > 0 && (
                  <p>
                    <span>输入</span>
                    {inputArtifacts.map((artifact) => <i key={`in-${tool.id}-${artifact}`}>{artifact}</i>)}
                  </p>
                )}
                {outputArtifacts.length > 0 && (
                  <p>
                    <span>输出</span>
                    {outputArtifacts.map((artifact) => <i key={`out-${tool.id}-${artifact}`}>{artifact}</i>)}
                  </p>
                )}
              </div>
            )}
            {blockedInputArtifacts.length > 0 && (
              <div className="workflow-tool-blockers" aria-label="阻断输入产物">
                {blockedInputArtifacts.map((artifact) => (
                  <p key={`${tool.id}-${artifact.artifactName}-${artifact.artifactId}`}>
                    <span>阻断输入</span>
                    <strong title={artifact.message || artifact.artifactName}>
                      {artifact.artifactName || '未命名产物'}
                    </strong>
                    <i>{artifact.status || 'unknown'}</i>
                    {artifact.artifactId && <em title={artifact.artifactId}>{artifact.artifactId}</em>}
                    {artifact.recommendedActions?.length > 0 && (
                      <small>{artifact.recommendedActions.slice(0, 3).join(' / ')}</small>
                    )}
                  </p>
                ))}
              </div>
            )}
            {(contract?.idempotencyPolicy || contract?.rollbackPolicy) && (
              <div className="workflow-tool-policy">
                {contract.idempotencyPolicy && <span title={contract.idempotencyPolicy}>幂等：{contract.idempotencyPolicy}</span>}
                {contract.rollbackPolicy && <span title={contract.rollbackPolicy}>回滚：{contract.rollbackPolicy}</span>}
              </div>
            )}
            {result && <small title={result}>{result}</small>}
          </div>
        );
      })}
    </div>
  );
}

function renderTaskExecutions(tasks: WorkflowGoalTaskState[]) {
  if (tasks.length === 0) return null;

  return (
    <div className="workflow-tool-execution-list" aria-label="Kernel 任务执行">
      {tasks.map((task) => {
        const artifacts = [...task.inputArtifactIds, ...task.outputArtifactIds];
        const result = task.lastError || (task.completedAt ? '任务已完成并记录持久状态。' : '等待统一任务调度器推进。');
        return (
          <div key={task.taskId} className={`workflow-tool-execution ${executionStatusClass(task.status)}`}>
            <div className="workflow-tool-execution-head">
              <span>{task.kernelName || 'Kernel'}</span>
              <strong title={task.taskId}>{task.taskType}</strong>
              <em>{toolExecutionStatusLabel(task.status)}</em>
            </div>
            <div className="workflow-tool-execution-meta">
              <i>尝试 {task.attempt}/{task.maxAttempts}</i>
              {task.failureKind && <i>{task.failureKind}</i>}
              <i>{formatTime(task.updatedAt)}</i>
            </div>
            {artifacts.length > 0 && (
              <div className="workflow-tool-artifacts">
                {task.inputArtifactIds.length > 0 && (
                  <p>
                    <span>输入</span>
                    {task.inputArtifactIds.slice(0, 4).map((artifact) => <i key={`task-in-${task.taskId}-${artifact}`}>{artifact}</i>)}
                  </p>
                )}
                {task.outputArtifactIds.length > 0 && (
                  <p>
                    <span>输出</span>
                    {task.outputArtifactIds.slice(0, 4).map((artifact) => <i key={`task-out-${task.taskId}-${artifact}`}>{artifact}</i>)}
                  </p>
                )}
              </div>
            )}
            {result && <small title={result}>{result}</small>}
          </div>
        );
      })}
    </div>
  );
}

function productionEvidenceChips(event: ReturnType<typeof stageEventsForChapter>[number]) {
  const evidence = event.evidence;
  if (!evidence) return [];
  const knowledgeSummary = evidence.knowledgeBindingSummary;
  return [
    evidence.packageKind ? `包 ${evidence.packageKind}` : '',
    knowledgeSummary && knowledgeSummary.bindingCount > 0
      ? `知识入口 G${knowledgeSummary.shouldEnterGateCount}/蓝${knowledgeSummary.shouldEnterBlueprintCount}/F${knowledgeSummary.shouldEnterFactSnapshotCount}`
      : '',
    !knowledgeSummary && evidence.knowledgeBindingCount > 0 ? `知识 ${evidence.knowledgeBindingCount}` : '',
    evidence.knowledgeConstraintEvidence?.length > 0 ? `约束 ${evidence.knowledgeConstraintEvidence.length}` : '',
    evidence.acceptedCreativeIntentCount > 0 ? `创意 ${evidence.acceptedCreativeIntentCount}` : '',
    evidence.sourceRevisionPlans?.length > 0 ? `修订 ${evidence.sourceRevisionPlans.length}` : '',
    evidence.rebuiltFromPackageIds?.length > 0 ? `替代包 ${evidence.rebuiltFromPackageIds.length}` : '',
    evidence.rollback ? `回滚 v${evidence.rollback.targetVersionNumber || ''}`.trim() : '',
    evidence.memoryReads?.length > 0 ? `读记忆 ${evidence.memoryReads.length}` : '',
    evidence.memoryPromotions?.length > 0 ? `记忆提升 ${evidence.memoryPromotions.length}` : '',
    evidence.knowledgeFactCount > 0 ? `硬事实 ${evidence.knowledgeFactCount}` : '',
    evidence.ragQueryCount > 0 ? `召回 ${evidence.ragQueryCount}` : '',
    evidence.factSnapshotVersion > 0 ? `事实 v${evidence.factSnapshotVersion}` : '',
    evidence.chapterVersionNumber > 0 ? `章节 v${evidence.chapterVersionNumber}` : '',
    evidence.chapterVersionWordCount > 0 ? `${evidence.chapterVersionWordCount} 字` : '',
  ].filter(Boolean);
}

function knowledgeConstraintStatusLabel(status?: string | null) {
  const normalized = (status ?? '').toLowerCase();
  if (normalized === 'satisfied') return '已满足';
  if (normalized === 'violated') return '已违反';
  if (normalized === 'unknown') return '未知';
  return status || '未记录';
}

function knowledgeConstraintLabel(evidence: WorkflowKnowledgeConstraintEvidence) {
  const routes = [
    evidence.shouldEnterGate ? 'Gate' : '',
    evidence.shouldEnterBlueprint ? '蓝图' : '',
    evidence.shouldEnterFactSnapshot ? 'FactSnapshot' : '',
  ].filter(Boolean);
  return [
    evidence.constraintLevel || evidence.entryType || '知识约束',
    evidence.packagePolicy || '',
    routes.length > 0 ? `进入 ${routes.join('/')}` : '',
    evidence.classificationId ? '已分类' : '',
    evidence.gateStatus ? `Gate ${evidence.gateStatus}` : '',
    evidence.factSnapshotVersion > 0 ? `事实 v${evidence.factSnapshotVersion}` : '',
  ].filter(Boolean).join(' · ');
}

function renderKnowledgeConstraintEvidence(items?: WorkflowKnowledgeConstraintEvidence[] | null) {
  const visible = (items ?? [])
    .filter((item) => item.title?.trim() || item.knowledgeId?.trim())
    .slice(0, 4);
  if (visible.length === 0) return null;

  return (
    <div className="workflow-production-knowledge workflow-production-constraints" aria-label="知识约束证据">
      <span>知识约束证据</span>
      <div>
        {visible.map((item) => {
          const status = knowledgeConstraintStatusLabel(item.evidenceStatus);
          const title = item.title || item.knowledgeId;
          const titleText = [
            title,
            item.subject ? `对象：${item.subject}` : '',
            item.classificationRule ? `规则：${item.classificationRule}` : '',
            item.allowedTerms?.length > 0 ? `允许：${item.allowedTerms.slice(0, 3).join(' / ')}` : '',
            item.forbiddenTerms?.length > 0 ? `禁止：${item.forbiddenTerms.slice(0, 3).join(' / ')}` : '',
            item.violations?.length > 0 ? `违规：${item.violations.slice(0, 2).join(' / ')}` : '',
          ].filter(Boolean).join(' · ');
          return (
            <i
              key={`${item.factSnapshotId}-${item.knowledgeId}-${title}`}
              className={`constraint-${(item.evidenceStatus || 'unknown').toLowerCase()}`}
              title={`${titleText} · ${knowledgeConstraintLabel(item)}`}
            >
              <strong>{title}</strong>
              <em>{status}</em>
            </i>
          );
        })}
      </div>
    </div>
  );
}

function knowledgeBindingLabel(binding: WorkflowKnowledgeBindingEvidence) {
  const routes = [
    binding.shouldEnterGate ? 'Gate' : '',
    binding.shouldEnterBlueprint ? '蓝图' : '',
    binding.shouldEnterFactSnapshot ? 'FactSnapshot' : '',
  ].filter(Boolean);
  return [
    binding.constraintLevel || binding.role || binding.entryType || '知识',
    routes.length > 0 ? routes.join('/') : '',
    binding.classificationId ? '已分类' : '待分类',
    binding.projectUsageStatus || '',
  ].filter(Boolean).join(' · ');
}

function renderKnowledgeBindings(bindings?: WorkflowKnowledgeBindingEvidence[] | null) {
  const visible = (bindings ?? []).filter((binding) => binding.title?.trim()).slice(0, 4);
  if (visible.length === 0) return null;

  return (
    <div className="workflow-production-knowledge" aria-label="本章采用知识">
      <span>本章采用知识</span>
      <div>
        {visible.map((binding) => (
          <i
            key={`${binding.knowledgeId}-${binding.title}`}
            title={[
              binding.title,
              binding.classificationRule ? `规则：${binding.classificationRule}` : '',
              binding.packagePolicy ? `策略：${binding.packagePolicy}` : '',
              knowledgeBindingLabel(binding),
            ].filter(Boolean).join(' · ')}
          >
            <strong>{binding.title}</strong>
            <em>{knowledgeBindingLabel(binding)}</em>
          </i>
        ))}
      </div>
    </div>
  );
}

function renderKnowledgeBindingSummary(summary?: WorkflowKnowledgeBindingSummaryEvidence | null) {
  if (!summary || summary.bindingCount <= 0) return null;

  const items = [
    ['绑定', summary.bindingCount],
    ['Gate', summary.shouldEnterGateCount],
    ['蓝图', summary.shouldEnterBlueprintCount],
    ['FactSnapshot', summary.shouldEnterFactSnapshotCount],
    ['硬约束', summary.hardConstraintCount],
    ['待分类', summary.pendingClassificationCount],
  ].filter(([, value]) => Number(value) > 0);

  return (
    <div className="workflow-production-knowledge workflow-production-knowledge-summary" aria-label="知识入口摘要">
      <span>知识入口摘要</span>
      <div>
        {items.map(([label, value]) => (
          <i key={label}>
            <strong>{label}</strong>
            <em>{value}</em>
          </i>
        ))}
      </div>
    </div>
  );
}

function creativeIntentLabel(intent: WorkflowCreativeIntentEvidence) {
  return [
    intent.targetScope || '创意',
    intent.impactLevel || '',
    intent.status || '',
  ].filter(Boolean).join(' · ');
}

function renderCreativeIntents(intents?: WorkflowCreativeIntentEvidence[] | null) {
  const visible = (intents ?? [])
    .filter((intent) => intent.normalizedIntent?.trim())
    .slice(0, 4);
  if (visible.length === 0) return null;

  return (
    <div className="workflow-production-knowledge workflow-production-creative" aria-label="本章采用创意">
      <span>本章采用创意</span>
      <div>
        {visible.map((intent) => (
          <i key={`${intent.intentId}-${intent.normalizedIntent}`} title={`${intent.normalizedIntent} · ${creativeIntentLabel(intent)}`}>
            <strong>{intent.normalizedIntent}</strong>
            <em>{creativeIntentLabel(intent)}</em>
          </i>
        ))}
      </div>
    </div>
  );
}

function revisionPlanLabel(plan: WorkflowRevisionPlanEvidence) {
  return [
    plan.targetChapterDisplayName || plan.targetChapterLogicalId || '',
    plan.status || 'unknown',
    plan.planType || plan.targetScope || 'revision',
    plan.riskLevel ? `风险 ${plan.riskLevel}` : '',
    plan.affectedChapterIds?.length > 0 ? `影响 ${plan.affectedChapterIds.length} 章` : '',
    plan.invalidatedPackageIds?.length > 0 ? `失效包 ${plan.invalidatedPackageIds.length}` : '',
  ].filter(Boolean).join(' · ');
}

function revisionPlanTitle(plan: WorkflowRevisionPlanEvidence) {
  return [
    plan.targetChapterDisplayName || plan.targetChapterLogicalId || plan.targetChapterId || '',
    plan.recommendation || '已记录修订来源',
    revisionPlanLabel(plan),
    plan.revisionPlanId ? `计划：${plan.revisionPlanId}` : '',
    plan.targetChapterId ? `目标章节ID：${plan.targetChapterId}` : '',
    plan.affectedChapterIds?.length > 0 ? `影响章节：${plan.affectedChapterIds.slice(0, 4).join(' / ')}` : '',
    plan.invalidatedPackageIds?.length > 0 ? `失效包：${plan.invalidatedPackageIds.slice(0, 4).join(' / ')}` : '',
  ].filter(Boolean).join(' · ');
}

function renderRevisionPlanEvidence(plans?: WorkflowRevisionPlanEvidence[] | null) {
  const visible = (plans ?? [])
    .filter((plan) => plan.revisionPlanId?.trim() || plan.recommendation?.trim())
    .slice(0, 4);
  if (visible.length === 0) return null;

  return (
    <div className="workflow-production-knowledge workflow-production-revisions" aria-label="来源修订计划">
      <span>来源修订计划</span>
      <div>
        {visible.map((plan, index) => {
          const title = plan.targetChapterDisplayName || plan.targetChapterLogicalId || plan.revisionPlanId || `revision-plan-${index + 1}`;
          return (
            <i
              key={`${title}-${plan.targetChapterId}-${index}`}
              title={revisionPlanTitle(plan)}
            >
              <strong>{title}</strong>
              <em>{revisionPlanLabel(plan)}</em>
            </i>
          );
        })}
      </div>
    </div>
  );
}

function rollbackEvidenceTitle(rollback: WorkflowRollbackEvidence) {
  return [
    rollback.reason || '已记录版本回滚',
    rollback.targetVersionId ? `目标版本：${rollback.targetVersionId}` : '',
    rollback.currentDocumentId ? `当前正文：${rollback.currentDocumentId}` : '',
    rollback.invalidatedPackageIds?.length > 0 ? `失效包：${rollback.invalidatedPackageIds.slice(0, 6).join(' / ')}` : '',
  ].filter(Boolean).join(' · ');
}

function renderRollbackEvidence(rollback?: WorkflowRollbackEvidence | null) {
  if (!rollback) return null;

  return (
    <div className="workflow-production-knowledge workflow-production-rollback" aria-label="版本回滚">
      <span>版本回滚</span>
      <div>
        <i title={rollbackEvidenceTitle(rollback)}>
          <strong>v{rollback.targetVersionNumber || '?'}</strong>
          <em>{rollback.invalidatedPackageIds?.length > 0 ? `失效包 ${rollback.invalidatedPackageIds.length}` : '无失效包'}</em>
        </i>
      </div>
    </div>
  );
}

function renderCreativeInbox(
  intents: WorkflowCreativeIntentEvidence[],
  openRecoveryGoal: (intentId: string) => void,
) {
  const visible = intents
    .filter((intent) => intent.normalizedIntent?.trim())
    .slice(0, 6);

  return (
    <section className="workflow-creative-inbox" aria-label="创意收件箱">
      <div className="workflow-creative-inbox-head">
        <div>
          <span>创意收件箱</span>
          <strong>{visible.length} 条</strong>
        </div>
        <em>Agent 决策输入</em>
      </div>
      {visible.length === 0 ? (
        <div className="empty compact">当前章节还没有创意要求。</div>
      ) : (
        <div className="workflow-creative-inbox-list">
          {visible.map((intent) => (
            <article key={intent.intentId} className={`workflow-creative-inbox-row ${intent.status}`}>
              <span>{creativeIntentLabel(intent)}</span>
              <strong title={intent.normalizedIntent}>{intent.normalizedIntent}</strong>
              {intent.source === 'legacy_recovery' && intent.status === 'candidate' ? (
                <button
                  type="button"
                  className="ghost-button compact"
                  onClick={() => openRecoveryGoal(intent.intentId)}
                >
                  进入会话确认
                </button>
              ) : <em>{intent.status}</em>}
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

function renderChapterCanonicalSummary(chapter?: NovelChapterView | null) {
  const summary = chapter?.productionSummary;
  if (!summary?.hasCanonicalEvidence) return null;

  const revisionPlans = (summary.revisionPlans ?? []).slice(0, 3);
  const creativeIntents = (summary.creativeIntents ?? [])
    .filter((intent) => intent.normalizedIntent?.trim())
    .slice(0, 4);
  const traceItems = (summary.traceItems ?? []).slice(0, 8);
  if (revisionPlans.length === 0 && creativeIntents.length === 0 && traceItems.length === 0) return null;

  return (
    <section className="workflow-chapter-canonical-summary" aria-label="章节生产决策">
      <div className="workflow-chapter-canonical-head">
        <div>
          <span>章节生产链</span>
          <strong>{traceItems.length} 节点 / {revisionPlans.length} 修订 / {creativeIntents.length} 创意</strong>
        </div>
        <em title={summary.endingState || summary.summary}>{summary.status || '已记录'}</em>
      </div>

      {traceItems.length > 0 && (
        <details className="workflow-canonical-details" open>
          <summary>生产链节点</summary>
          <div className="workflow-canonical-list">
            {traceItems.map((item) => (
              <article key={item.key} className="workflow-canonical-row trace">
                <span>{item.label || item.artifactType || '节点'}</span>
                <strong title={[item.description, item.artifactId, ...(item.relatedArtifactIds ?? [])].filter(Boolean).join(' / ')}>
                  {item.description || item.artifactId || item.key}
                </strong>
                <em>{item.status || 'recorded'}</em>
                {item.relatedArtifactIds?.length > 0 && (
                  <small title={item.relatedArtifactIds.join(' / ')}>关联 {item.relatedArtifactIds.length}</small>
                )}
              </article>
            ))}
          </div>
        </details>
      )}

      {revisionPlans.length > 0 && (
        <details className="workflow-canonical-details">
          <summary>修订计划</summary>
          <div className="workflow-canonical-list">
            {revisionPlans.map((plan) => (
              <article key={plan.revisionPlanId} className="workflow-canonical-row">
                <span>{plan.planType || 'revision'}</span>
                <strong title={plan.recommendation || plan.revisionPlanId}>
                  {plan.recommendation || plan.revisionPlanId}
                </strong>
                <em>{plan.status || 'draft'} · {plan.riskLevel || 'risk?'}</em>
                {plan.affectedChapterIds.length > 0 && (
                  <small title={plan.affectedChapterIds.join(' / ')}>影响 {plan.affectedChapterIds.length} 章</small>
                )}
              </article>
            ))}
          </div>
        </details>
      )}

      {creativeIntents.length > 0 && (
        <details className="workflow-canonical-details">
          <summary>创意意图</summary>
          <div className="workflow-canonical-list">
            {creativeIntents.map((intent) => (
              <article key={intent.intentId} className="workflow-canonical-row creative">
                <span>{intent.targetScope || 'scope'}</span>
                <strong title={intent.normalizedIntent}>{intent.normalizedIntent}</strong>
                <em>{intent.status || 'candidate'}</em>
              </article>
            ))}
          </div>
        </details>
      )}
    </section>
  );
}

function reviewCheckLabel(check: WorkflowAgentReviewCheckEvidence) {
  return [
    check.status || '',
    check.evidence?.[0] || '',
  ].filter(Boolean).join(' · ');
}

function renderAgentReviewChecks(checks?: WorkflowAgentReviewCheckEvidence[] | null) {
  const visible = (checks ?? [])
    .filter((check) => check.name?.trim() || check.message?.trim())
    .slice(0, 4);
  if (visible.length === 0) return null;

  return (
    <div className="workflow-production-knowledge workflow-production-review" aria-label="Agent 审稿验收">
      <span>Agent 审稿验收</span>
      <div>
        {visible.map((check) => (
          <i key={`${check.key}-${check.name}`} title={`${check.message} · ${reviewCheckLabel(check)}`}>
            <strong>{check.name || check.key}</strong>
            <em>{reviewCheckLabel(check)}</em>
          </i>
        ))}
      </div>
    </div>
  );
}

type ProductionEvidenceSummary = {
  gate: WorkflowGateEvidence | null;
  factSnapshot: WorkflowFactSnapshotEvidence | null;
  agentReview: WorkflowAgentReviewSummaryEvidence | null;
  knowledgeBindingSummary: WorkflowKnowledgeBindingSummaryEvidence | null;
  agentReviewChecks: WorkflowAgentReviewCheckEvidence[];
  knowledgeConstraints: WorkflowKnowledgeConstraintEvidence[];
  memoryReads: WorkflowMemoryReadEvidence[];
  memoryPromotions: WorkflowMemoryPromotionEvidence[];
};

function emptyProductionEvidenceSummary(): ProductionEvidenceSummary {
  return {
    gate: null,
    factSnapshot: null,
    agentReview: null,
    knowledgeBindingSummary: null,
    agentReviewChecks: [],
    knowledgeConstraints: [],
    memoryReads: [],
    memoryPromotions: [],
  };
}

function hasProductionEvidence(summary: ProductionEvidenceSummary) {
  return !!summary.factSnapshot ||
    !!summary.gate ||
    !!summary.agentReview ||
    !!summary.knowledgeBindingSummary ||
    summary.agentReviewChecks.length > 0 ||
    summary.knowledgeConstraints.length > 0 ||
    summary.memoryReads.length > 0 ||
    summary.memoryPromotions.length > 0;
}

function collectProductionEvidence(
  events: WorkflowProductionEventSummary[],
  chains: WorkflowProductionChain[] = [],
) {
  const summary = emptyProductionEvidenceSummary();

  chains.forEach((chain) => {
    const evidence = chain.evidence;
    if (!evidence) return;

    if (!summary.gate && evidence.gate) summary.gate = evidence.gate;
    if (!summary.factSnapshot && evidence.factSnapshot) summary.factSnapshot = evidence.factSnapshot;
    if (!summary.agentReview && evidence.agentReview) summary.agentReview = evidence.agentReview;
  });

  return events.reduce<ProductionEvidenceSummary>((summary, event) => {
    const evidence = event.evidence;
    if (!evidence) return summary;

    if (!summary.gate && evidence.gate) summary.gate = evidence.gate;
    if (!summary.factSnapshot && evidence.factSnapshot) summary.factSnapshot = evidence.factSnapshot;
    if (!summary.agentReview && evidence.agentReview) summary.agentReview = evidence.agentReview;
    if (!summary.knowledgeBindingSummary && evidence.knowledgeBindingSummary) {
      summary.knowledgeBindingSummary = evidence.knowledgeBindingSummary;
    }
    if (evidence.agentReviewChecks?.length) {
      summary.agentReviewChecks.push(...evidence.agentReviewChecks);
    }
    if (evidence.knowledgeConstraintEvidence?.length) {
      summary.knowledgeConstraints.push(...evidence.knowledgeConstraintEvidence);
    }
    if (evidence.memoryReads?.length) {
      summary.memoryReads.push(...evidence.memoryReads);
    }
    if (evidence.memoryPromotions?.length) {
      summary.memoryPromotions.push(...evidence.memoryPromotions);
    }
    return summary;
  }, summary);
}

function gateStatusLabel(status?: string | null) {
  const normalized = (status ?? '').toLowerCase();
  if (normalized.includes('failed')) return '未通过';
  if (normalized.includes('pass') || normalized.includes('valid')) return '已通过';
  if (normalized.includes('repair')) return '修复中';
  return status || '未记录';
}

function renderFactSnapshotCard(fact?: WorkflowFactSnapshotEvidence | null) {
  if (!fact) return null;

  const rows = [
    ['主角', fact.protagonistName],
    ['身份', fact.protagonistIdentity],
    ['状态', fact.protagonistStatus],
    ['地点', fact.currentLocation],
    ['系统', fact.systemState],
    ['装备', fact.equipmentState],
    ['结尾', fact.endingState],
  ].filter(([, value]) => value?.trim());

  return (
    <article className="workflow-evidence-card fact">
      <div className="workflow-evidence-card-head">
        <span>连续性事实</span>
        <strong>{fact.protagonistName || '事实快照'}</strong>
      </div>
      <div className="workflow-evidence-fact-grid">
        {rows.map(([label, value]) => (
          <p key={label} title={value}>
            <span>{label}</span>
            <strong>{value}</strong>
          </p>
        ))}
      </div>
      {fact.keyEvents?.length > 0 && (
        <div className="workflow-evidence-list">
          <span>已发生</span>
          {fact.keyEvents.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      )}
      {fact.nextChapterMustCarry?.length > 0 && (
        <div className="workflow-evidence-list carry">
          <span>下一章必须承接</span>
          {fact.nextChapterMustCarry.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      )}
    </article>
  );
}

function renderGateEvidenceCard(gate?: WorkflowGateEvidence | null) {
  if (!gate) return null;

  const checks: Array<[string, boolean]> = [
    ['协议', gate.protocolPassed],
    ['事实', gate.factSnapshotPassed],
    ['蓝图', gate.blueprintPassed],
    ['知识', gate.ragPassed],
    ['CHANGES', gate.changesDetected],
  ];

  return (
    <article className={`workflow-evidence-card gate ${gate.status?.toLowerCase() || 'unknown'}`}>
      <div className="workflow-evidence-card-head">
        <span>内核门禁</span>
        <strong>{gateStatusLabel(gate.status)}</strong>
      </div>
      <div className="workflow-evidence-checks">
        {checks.map(([label, passed]) => (
          <i key={label} className={passed ? 'pass' : 'fail'}>{label}</i>
        ))}
      </div>
      {gate.issues?.length > 0 && (
        <div className="workflow-evidence-list problem">
          <span>阻塞项</span>
          {gate.issues.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      )}
      {gate.repairHints?.length > 0 && (
        <div className="workflow-evidence-list">
          <span>修复建议</span>
          {gate.repairHints.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      )}
    </article>
  );
}

function renderAgentReviewSummaryCard(
  review?: WorkflowAgentReviewSummaryEvidence | null,
  checks?: WorkflowAgentReviewCheckEvidence[] | null,
) {
  const visibleChecks = (checks ?? []).filter((check) => check.name || check.message).slice(0, 3);
  const reviewSignals = [
    review?.meetsAcceptedCreativeIntents === true ? '创意已落实' : '',
    review?.meetsAcceptedCreativeIntents === false ? '创意未落实' : '',
    review?.continuityRisk ? `连续性 ${review.continuityRisk}` : '',
    review?.chapterPacing ? `节奏 ${review.chapterPacing}` : '',
    review?.recommendedAction ? `动作 ${review.recommendedAction}` : '',
  ].filter(Boolean);
  if (!review && visibleChecks.length === 0) return null;

  return (
    <article className="workflow-evidence-card review">
      <div className="workflow-evidence-card-head">
        <span>Agent 总编验收</span>
        <strong>{review?.decision || review?.overallResult || '已记录'}</strong>
      </div>
      {reviewSignals.length ? (
        <div className="workflow-evidence-list">
          <span>判断</span>
          {reviewSignals.slice(0, 4).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      ) : null}
      {review?.problems?.length ? (
        <div className="workflow-evidence-list problem">
          <span>问题</span>
          {review.problems.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      ) : null}
      {review?.suggestions?.length ? (
        <div className="workflow-evidence-list">
          <span>建议</span>
          {review.suggestions.slice(0, 3).map((item) => <em key={item} title={item}>{item}</em>)}
        </div>
      ) : null}
      {visibleChecks.length > 0 && (
        <div className="workflow-evidence-list">
          <span>检查</span>
          {visibleChecks.map((check) => (
            <em key={`${check.key}-${check.name}`} title={check.message}>
              {check.name || check.key} · {check.status}
            </em>
          ))}
        </div>
      )}
    </article>
  );
}

function memoryScopeLabel(scope?: string | null) {
  const labels: Record<string, string> = {
    chat: '聊天',
    session: '会话',
    project: '项目',
    author: '作者',
    user: '用户',
    execution: '执行',
    tianming_state: '天命状态',
  };
  return labels[(scope ?? '').toLowerCase()] ?? scope ?? '未记录';
}

function renderMemoryEvidenceCard(
  reads?: WorkflowMemoryReadEvidence[] | null,
  promotions?: WorkflowMemoryPromotionEvidence[] | null,
) {
  const visibleReads = (reads ?? [])
    .filter((read, index, list) => read.id && list.findIndex((item) => item.id === read.id) === index)
    .slice(0, 4);
  const visiblePromotions = (promotions ?? [])
    .filter((promotion, index, list) => promotion.id && list.findIndex((item) => item.id === promotion.id) === index)
    .slice(0, 4);
  if (visibleReads.length === 0 && visiblePromotions.length === 0) return null;

  return (
    <article className="workflow-evidence-card memory">
      <div className="workflow-evidence-card-head">
        <span>记忆审计</span>
        <strong>{visibleReads.length} 读 / {visiblePromotions.length} 提升</strong>
      </div>
      {visibleReads.length > 0 && (
        <div className="workflow-evidence-list">
          <span>读取</span>
          {visibleReads.map((read) => (
            <em key={read.id} title={`${read.consumer || read.sourceType} · ${read.memoryKeys.join(' / ')}`}>
              {memoryScopeLabel(read.memoryScope)} · {read.memoryKeys.slice(0, 2).join(' / ') || 'memory'}
            </em>
          ))}
        </div>
      )}
      {visiblePromotions.length > 0 && (
        <div className="workflow-evidence-list carry">
          <span>提升</span>
          {visiblePromotions.map((promotion) => (
            <em key={promotion.id} title={promotion.promotionReason || `${promotion.sourceMemoryKey} -> ${promotion.targetMemoryKey}`}>
              {memoryScopeLabel(promotion.sourceScope)} {'->'} {memoryScopeLabel(promotion.targetScope)} · {promotion.targetMemoryKey || 'memory'}
            </em>
          ))}
        </div>
      )}
    </article>
  );
}

function renderProductionFailure(event: ReturnType<typeof stageEventsForChapter>[number]) {
  const failure = event.failure;
  if (!failure) return null;

  const chips = [
    failure.recoverable ? '可恢复' : '不可自动恢复',
    failure.requiresUserDecision ? '需要用户决定' : '无需用户决定',
    failure.artifactIds?.length > 0 ? `产物 ${failure.artifactIds.slice(0, 2).join(' / ')}` : '',
  ].filter(Boolean);

  return (
    <div className="workflow-production-failure" aria-label="生产失败契约">
      <div>
        <span>{failure.code || 'PRODUCTION_FAILED'}</span>
        <strong>{failure.message || event.message}</strong>
      </div>
      {failure.recommendedAction && <em>{failure.recommendedAction}</em>}
      {chips.length > 0 && (
        <p>
          {chips.map((chip) => <i key={chip}>{chip}</i>)}
        </p>
      )}
    </div>
  );
}

function renderProductionChains(chains: WorkflowProductionChain[], selectedChapter?: NovelChapterView | null) {
  if (chains.length === 0) {
    return (
      <section className="workflow-production-chains workflow-production-chain-primary" aria-label="天命生产链">
        <div className="workflow-production-events-head">
          <div>
            <span>天命生产链</span>
            <strong>0 条</strong>
          </div>
          <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
        </div>
        <div className="empty compact">还没有可聚合的生产链路。</div>
      </section>
    );
  }

  return (
    <section className="workflow-production-chains workflow-production-chain-primary" aria-label="天命生产链">
      <div className="workflow-production-events-head">
        <div>
          <span>天命生产链</span>
          <strong>{chains.length} 条</strong>
        </div>
        <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
      </div>
      <div className="workflow-production-chain-list">
        {chains.slice(0, 4).map((chain) => {
          const chainTitle = chain.chapterDisplayName || chain.chapterLogicalId || selectedChapter?.title || '生产链路';
          const chainSubTitle = chain.packageId || chain.runtimeRunId || chain.chapterId;
          const evidenceChips = productionChainEvidenceChips(chain);
          return (
            <article key={chain.id} className={`workflow-production-chain ${productionChainClass(chain)}`}>
              <div className="workflow-production-chain-head">
                <div>
                  <span title={chainSubTitle}>{chainTitle}</span>
                  <strong title={chain.summary}>{chain.summary || '生产链路已记录'}</strong>
                </div>
                <em>{productionChainStatusLabel(chain.status)}</em>
              </div>
              {evidenceChips.length > 0 && (
                <div className="workflow-production-chain-evidence" aria-label="生产链证据摘要">
                  {evidenceChips.map((chip) => <span key={chip} title={chip}>{chip}</span>)}
                </div>
              )}
              <div className="workflow-production-chain-track">
                {chain.steps.map((step, index) => (
                  <span
                    key={`${chain.id}-${step.eventId}-${step.key}-${index}`}
                    className={`workflow-production-chain-step ${productionStepClass(step.status)}`}
                    title={[step.message, step.eventType, step.artifactId, formatTime(step.createdAt)].filter(Boolean).join(' · ')}
                  >
                    <i>{index + 1}</i>
                    <strong>{step.label}</strong>
                    <small>{step.status}</small>
                  </span>
                ))}
              </div>
              <div className="workflow-production-chain-meta">
                {chain.chapterVersionId && <span>章节 v{chain.chapterVersionNumber || '?'} · {projectShortId(chain.chapterVersionId)}</span>}
                {chain.factSnapshotId && <span>事实 v{chain.factSnapshotVersion || '?'} · {projectShortId(chain.factSnapshotId)}</span>}
                {chain.revisionPlanIds.length > 0 && <span>修订 {chain.revisionPlanIds.length}</span>}
                {chain.rebuildLinks.length > 0 && <span>重建 {chain.rebuildLinks.length}</span>}
                {chainSubTitle && <span>{projectShortId(chainSubTitle)}</span>}
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}

function chapterVersionLabel(version?: ChapterVersionResponse | null) {
  if (!version) return '未选择版本';
  return `v${version.versionNumber}${version.isCurrent ? ' 当前' : ''} · ${version.wordCount} 字`;
}

function chapterVersionOptionLabel(version: ChapterVersionResponse) {
  return `v${version.versionNumber}${version.isCurrent ? ' 当前' : ''} · ${formatTime(version.createdAt)}`;
}

function diffKindLabel(kind: ChapterVersionDiffBlock['kind']) {
  const labels: Record<string, string> = {
    unchanged: '未变',
    changed: '修改',
    added: '新增',
    removed: '删除',
  };
  return labels[kind] ?? kind;
}

function diffBlockTitle(block: ChapterVersionDiffBlock) {
  return [block.leftText, block.rightText].filter(Boolean).join('\n---\n');
}

function workflowAlignmentChips(alignment?: ChapterVersionProductionAlignment | null) {
  if (!alignment) return [];
  return [
    alignment.acceptedCreativeIntents.length > 0
      ? {
          key: 'creative',
          label: `创意 ${alignment.acceptedCreativeIntents.length}`,
          value: alignment.acceptedCreativeIntents[0].normalizedIntent,
          tone: 'creative',
        }
      : null,
    alignment.sourceRevisionPlans.length > 0
      ? {
          key: 'revision',
          label: `修订 ${alignment.sourceRevisionPlans.length}`,
          value: alignment.sourceRevisionPlans[0].recommendation || alignment.sourceRevisionPlans[0].status,
          tone: 'revision',
        }
      : null,
    alignment.agentReviewDecision || alignment.agentReviewChecks.length > 0
      ? {
          key: 'review',
          label: '审稿',
          value: alignment.agentReviewDecision || `${alignment.agentReviewChecks.length} 项检查`,
          tone: 'review',
        }
      : null,
    alignment.rebuiltFromPackageIds.length > 0
      ? {
          key: 'package',
          label: `替代包 ${alignment.rebuiltFromPackageIds.length}`,
          value: alignment.rebuiltFromPackageIds.slice(0, 2).join(' / '),
          tone: 'package',
        }
      : null,
  ].filter(Boolean) as Array<{ key: string; label: string; value: string; tone: string }>;
}

function isWeakArtifactTitle(title?: string | null) {
  const value = (title ?? '').trim();
  return !value
    || /^chapter-\d+/i.test(value)
    || value === '目标推进'
    || value === '章节推进';
}

function artifactDisplayTitle(artifact: WorkflowArtifactTimelineItem, chapterTitleById: Map<string, string>) {
  const chapterTitle = artifact.chapterId ? chapterTitleById.get(artifact.chapterId) : '';
  if (chapterTitle && isWeakArtifactTitle(artifact.title)) return chapterTitle;
  return artifact.title || chapterTitle || artifact.label;
}

function artifactSummaryText(artifact: WorkflowArtifactTimelineItem, chapterTitleById: Map<string, string>) {
  const title = artifactDisplayTitle(artifact, chapterTitleById);
  const summary = (artifact.summary || '').trim();
  if (!summary || normalizeLabel(summary) === normalizeLabel(title)) {
    if (artifact.isFinal) return '已进入小说书城，可在正文成稿中查看。';
    return artifact.status || '过程产物';
  }
  return summary;
}

function artifactRenderKey(artifact: WorkflowArtifactTimelineItem, index: number) {
  return [
    artifact.id || artifact.kind || 'artifact',
    artifact.updatedAt || 'no-time',
    artifact.runId || 'no-run',
    artifact.status || 'no-status',
    index,
  ].join(':');
}

function chapterStatusClass(chapter?: NovelChapterView | null) {
  const status = chapter?.artifactStatus || chapter?.writingStatus || chapter?.status || 'unstarted';
  if (status === 'committed' || status === 'quality_passed') return 'done';
  if (status === 'validated') return 'validated';
  if (status === 'gate_failed' || status === 'repairing' || status === 'quality_failed') return 'blocked';
  if (status === 'draft_generated') return 'drafting';
  if (status === 'context_ready') return 'context';
  if (status === 'candidate_selected') return 'selected';
  if (status === 'candidates_ready') return 'candidate';
  if (status === 'planned') return 'planned';
  return 'unstarted';
}

function chapterStatusLabel(chapter: NovelChapterView) {
  if (chapter.userVisibleStatus) return chapter.userVisibleStatus;
  if (chapter.status === '卷草案') return '卷草案';
  const labels: Record<string, string> = {
    committed: '已入库',
    validated: '已校验',
    repairing: '修复中',
    gate_failed: '门禁失败',
    draft_generated: '草稿',
    context_ready: '上下文',
    candidate_selected: '已选',
    candidates_ready: '候选',
    planned: '已规划',
    unstarted: '待规划',
  };
  return labels[chapter.writingStatus] ?? chapter.status ?? '待规划';
}

function formatTime(value?: string | null) {
  if (!value) return '暂无时间';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function visiblePreview(value: string) {
  const text = value.trim();
  if (!text) return '';
  return text.length <= 1200 ? text : `${text.slice(0, 1200)}...`;
}

function manuscriptPreview(value: string) {
  const text = value.trim();
  if (!text) return [];
  const paragraphs = text
    .split(/\n+/)
    .map((paragraph) => paragraph.trim())
    .filter(Boolean);
  if (paragraphs.length > 1 || text.length < 900) return paragraphs.slice(0, 12);

  const chunks: string[] = [];
  let current = '';
  for (const sentence of text.match(/[^。！？!?]+[。！？!?]?/g) ?? [text]) {
    if (current.length > 520) {
      chunks.push(current);
      current = sentence;
    } else {
      current += sentence;
    }
  }
  if (current) chunks.push(current);
  return chunks.slice(0, 12);
}

function isCompletionOnlyArtifact(artifact: WorkflowArtifactTimelineItem) {
  const surface = normalizeLabel(artifact.surface);
  const label = normalizeLabel(artifact.label);
  const summary = normalizeLabel(artifact.summary);
  const source = normalizeLabel(artifact.source);
  const status = normalizeLabel(artifact.status);
  const kind = normalizeLabel(artifact.kind);

  return artifact.isFinal ||
    kind === 'librarychapter' ||
    surface.includes('小说书城') ||
    surface.includes('书城') ||
    label.includes('书城入库') ||
    label.includes('已入库章节') ||
    summary.includes('已进入小说书城') ||
    summary.includes('正文已经进入小说书城') ||
    source.includes('novellibrarychapters') ||
    status === 'committed';
}

function buildMissionOnlyCards() {
  return [] as { session: AgentSessionSummary; plan: AgentMissionPlan; projectId: string }[];
}

function buildAgentContextMessage(args: {
  projectId: string;
  sessionId: string;
  selectedArtifact: WorkflowArtifactTimelineItem | null;
  selectedChapter: NovelChapterView | null;
  userFeedback: string;
}) {
  const lines = [
    '用户在创作工作流页面补充了要求。',
    `projectId: ${args.projectId}`,
    `sessionId: ${args.sessionId}`,
    `selectedArtifactId: ${args.selectedArtifact?.id || '无'}`,
    `artifactKind: ${args.selectedArtifact?.kind || '无'}`,
    `chapterId: ${args.selectedArtifact?.chapterId || args.selectedChapter?.chapterId || '无'}`,
    `runId: ${args.selectedArtifact?.runId || args.selectedChapter?.runId || '无'}`,
    `当前产物状态: ${args.selectedArtifact?.status || (args.selectedChapter ? chapterStatusLabel(args.selectedChapter) : '未知')}`,
    '',
    args.userFeedback || '请结合当前工作流真实产物，判断下一步应该推进、解释、修订还是等待用户确认。',
  ];
  return lines.join('\n');
}

function firstRealChapter(volumes: NovelVolumeView[]) {
  return volumes
    .flatMap((volume) => volume.chapters)
    .find((chapter) => isLibraryChapter(chapter) || chapter.visibleInWorkflow || chapter.hasGeneratedContent || chapter.runId || chapter.summary);
}

function isLibraryChapter(chapter: NovelChapterView) {
  return chapter.visibleInLibrary
    || chapter.artifactStatus === 'committed'
    || chapter.writingStatus === 'committed'
    || ['committed', 'published', 'completed'].includes((chapter.status || '').toLowerCase());
}

function hasReadableManuscript(chapter?: NovelChapterView | null) {
  return !!chapter && isLibraryChapter(chapter) && chapter.content.trim().length > 0;
}

function chapterQualityWarnings(chapter?: NovelChapterView | null) {
  if (!chapter) return [] as string[];
  const warnings: string[] = [];
  if (hasReadableManuscript(chapter) && chapter.wordCount > 0 && chapter.wordCount < 3000) {
    warnings.push(`正文 ${chapter.wordCount} 字，低于当前章节成稿目标 3000 字，建议进入扩写/修订。`);
  }
  if (!hasReadableManuscript(chapter)) {
    warnings.push(isLibraryChapter(chapter) ? '这一章已标记入库，但没有可展示正文，需要检查提交内容。' : '这一章还没有进入书城成稿，当前只能查看工作流产物。');
  }
  if (chapter.needsRewrite) warnings.push('质量评审标记为需要重写。');
  if (chapter.gateIssues.length > 0) warnings.push(`结构门禁还有 ${chapter.gateIssues.length} 个问题。`);
  if (!chapter.changesProtocolPassed && chapter.hasGeneratedContent) warnings.push('修订记录协议未通过，需重新生成或修复 CHANGES。');
  return warnings;
}

function artifactPriority(artifact: WorkflowArtifactTimelineItem) {
  const order: Record<string, number> = {
    quality_review: 80,
    gate_report: 70,
    draft_artifact: 60,
    chapter_artifact_summary: 55,
    library_chapter: 50,
    context_package: 40,
    chapter_brief: 30,
  };
  return order[artifact.kind] ?? 10;
}

function isCompletedProductionChain(chain: WorkflowProductionChain) {
  const status = chain.status.toLowerCase();
  return status === 'completed' || status === 'committed' || status === 'done';
}

function progressStatusFromArtifact(
  artifact: WorkflowArtifactTimelineItem | undefined,
  fallbackDone = false,
): ChapterProgressStatus {
  if (!artifact) return fallbackDone ? 'done' : 'unstarted';
  const status = artifactClass(artifact);
  if (status === 'blocked' || status === 'running') return status;
  if (artifact.isFinal || status === 'done') return 'done';
  return 'done';
}

function progressCaption(status: ChapterProgressStatus) {
  const labels: Record<ChapterProgressStatus, string> = {
    done: '已完成',
    running: '执行中',
    blocked: '需处理',
    drafting: '生成中',
    unstarted: '待生成',
  };
  return labels[status];
}

function buildChapterProgressSteps(
  chapter: NovelChapterView | null,
  artifacts: WorkflowArtifactTimelineItem[],
): ChapterProgressStep[] {
  const findArtifact = (predicate: (artifact: WorkflowArtifactTimelineItem) => boolean) => artifacts.find(predicate);
  const step = (
    key: string,
    label: string,
    artifact: WorkflowArtifactTimelineItem | undefined,
    fallbackDone = false,
    emptySummary = '暂无对应产物。',
  ): ChapterProgressStep => {
    const status = progressStatusFromArtifact(artifact, fallbackDone);
    return {
      key,
      label,
      status,
      caption: progressCaption(status),
      artifactId: artifact?.id,
      summary: artifact?.summary || emptySummary,
    };
  };

  const planArtifact = findArtifact((artifact) => artifact.kind === 'chapter_brief');
  const contextArtifact = findArtifact((artifact) => artifact.kind === 'context_package');
  const draftArtifact = findArtifact((artifact) =>
    artifact.kind === 'draft_artifact' ||
    artifact.kind === 'chapter_artifact_summary' && !!artifact.preview?.trim());
  const gateArtifact = findArtifact((artifact) => artifact.kind === 'gate_report');
  const qualityArtifact = findArtifact((artifact) => artifact.kind === 'quality_review');
  const libraryArtifact = findArtifact((artifact) => artifact.kind === 'library_chapter' || artifact.isFinal && artifact.surface === '小说书城');

  return [
    step('chapter_plan', '章节规划', planArtifact, !!chapter && ['planned', 'candidates_ready', 'candidate_selected', 'context_ready', 'draft_generated', 'validated', 'committed'].includes(chapter.writingStatus)),
    step('context', '上下文包', contextArtifact, !!chapter && ['context_ready', 'draft_generated', 'validated', 'committed'].includes(chapter.writingStatus)),
    step('draft', '正文草稿', draftArtifact, !!chapter?.hasGeneratedContent),
    step('gate', '结构门禁', gateArtifact, !!chapter && ['validated', 'committed'].includes(chapter.writingStatus)),
    step('quality', '质量评审', qualityArtifact, !!chapter && (chapter.artifactStatus === 'quality_passed' || chapter.writingStatus === 'committed')),
    step('library', '书城入库', libraryArtifact, hasReadableManuscript(chapter), hasReadableManuscript(chapter) ? '已进入小说书城。' : '暂无书城正文。'),
  ];
}

export default function WorkflowPage() {
  const navigate = useNavigate();
  const { projectId: routeProjectId } = useParams();
  const queryClient = useQueryClient();
  const storeProjectId = useProjectStore((s) => s.currentProjectId);
  const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
  const setCurrentProjectId = useProjectStore((s) => s.setCurrentProjectId);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string | null>(null);
  const [selectedChapterId, setSelectedChapterId] = useState<string | null>(null);
  const [selectedProgressStepKey, setSelectedProgressStepKey] = useState<string | null>(null);
  const [chapterDetailTab, setChapterDetailTab] = useState<ChapterDetailTab>('manuscript');
  const [agentNote, setAgentNote] = useState('');
  const [workbenchNotice, setWorkbenchNotice] = useState('');
  const [agentLastReply, setAgentLastReply] = useState('');
  const [showArchivedWorkflows, setShowArchivedWorkflows] = useState(false);
  const [workflowMenu, setWorkflowMenu] = useState<WorkflowContextMenuState | null>(null);
  const [workflowDialog, setWorkflowDialog] = useState<WorkflowDialogState | null>(null);
  const [workflowTitleDraft, setWorkflowTitleDraft] = useState('');
  const [workflowLeftVersionId, setWorkflowLeftVersionId] = useState('');
  const [workflowRightVersionId, setWorkflowRightVersionId] = useState('');
  const currentProjectId = routeProjectId ?? storeProjectId;
  const isDetailView = !!routeProjectId;

  const { data: agentSessions } = useQuery({
    queryKey: ['agentSessions'],
    queryFn: listAgentSessions,
    refetchInterval: 8000,
  });

  const { data: workspaceData, isLoading: workspaceLoading, isError } = useQuery({
    queryKey: ['workspace'],
    queryFn: getWorkspace,
    staleTime: 30_000,
    retry: 3,
  });

  const books = useMemo(() => workspaceData?.projects ?? [], [workspaceData?.projects]);
  const archivedBooks = useMemo(() => books.filter(isArchivedProject), [books]);
  const workflowBooks = useMemo(
    () => showArchivedWorkflows ? books : books.filter((book) => !isArchivedProject(book)),
    [books, showArchivedWorkflows],
  );
  const activeBook = useMemo(() => books.find((book) => book.isActive) ?? books[0] ?? null, [books]);
  const missionOnlyCards = useMemo(() => buildMissionOnlyCards(), []);

  const fallbackProjectId = useMemo(() => {
    const sessions = agentSessions ?? [];
    const scoredBooks = workflowBooks
      .map((book) => ({ projectId: book.projectId, score: bookActivityScore(book, sessions) }))
      .sort((a, b) => b.score - a.score);
    const activeMissionCard = missionOnlyCards
      .map((card) => ({
        projectId: card.projectId,
        score: missionChapters(card.plan).length * 35 + (card.plan.schedulerState?.tasks.length ?? 0) * 120,
      }))
      .sort((a, b) => b.score - a.score);
    return scoredBooks.find((item) => item.score > 0)?.projectId
      ?? activeMissionCard.find((item) => item.score > 0)?.projectId
      ?? activeBook?.projectId
      ?? workflowBooks[0]?.projectId
      ?? missionOnlyCards[0]?.projectId
      ?? null;
  }, [activeBook?.projectId, agentSessions, missionOnlyCards, workflowBooks]);

  const { data: workflow, isLoading: workflowLoading } = useQuery({
    queryKey: ['projectWorkflow', currentProjectId],
    queryFn: () => getProjectWorkflow(currentProjectId!),
    enabled: isDetailView && !!currentProjectId,
    refetchInterval: 5000,
  });

  const selectedBook = workflow?.project
    ?? books.find((book) => book.projectId === currentProjectId)
    ?? (isDetailView ? null : activeBook);
  const volumes = useMemo(() => workflow?.library?.volumes ?? [], [workflow?.library?.volumes]);
  const chapters = useMemo(() => volumes.flatMap((volume) => volume.chapters), [volumes]);
  const selectedPlan = workflow?.goalState
    ? null
    : workflow?.missionPlans?.[0] ?? workflow?.sessions?.[0]?.missionPlan ?? null;
  const timeline = useMemo(() => workflow?.artifactTimeline ?? [], [workflow?.artifactTimeline]);
  const visibleTimeline = useMemo(() => timeline.filter((artifact) => artifact.isUserVisible), [timeline]);
  const selectedChapter = chapters.find((chapter) => chapter.chapterId === selectedChapterId)
    ?? chapters.find(isLibraryChapter)
    ?? firstRealChapter(volumes)
    ?? chapters[0]
    ?? null;
  const selectedChapterVersionQuery = useQuery({
    queryKey: ['workflowChapterVersions', selectedChapter?.chapterId],
    queryFn: () => getChapterVersions(selectedChapter!.chapterId),
    enabled: isDetailView && !!selectedChapter?.chapterId,
    staleTime: 15_000,
    retry: 1,
  });
  const chapterVersions = useMemo(
    () => [...(selectedChapterVersionQuery.data ?? [])].sort((a, b) => b.versionNumber - a.versionNumber),
    [selectedChapterVersionQuery.data],
  );
  const currentChapterVersion = chapterVersions.find((version) => version.isCurrent) ?? chapterVersions[0] ?? null;
  const previousChapterVersion = chapterVersions.find((version) => version.id !== currentChapterVersion?.id) ?? null;
  const leftChapterVersion = chapterVersions.find((version) => version.id === workflowLeftVersionId) ?? previousChapterVersion ?? currentChapterVersion;
  const rightChapterVersion = chapterVersions.find((version) => version.id === workflowRightVersionId) ?? currentChapterVersion ?? previousChapterVersion;
  const canCompareWorkflowVersions = !!selectedChapter?.chapterId
    && !!leftChapterVersion?.id
    && !!rightChapterVersion?.id
    && leftChapterVersion.id !== rightChapterVersion.id;
  const workflowVersionDiffQuery = useQuery({
    queryKey: ['workflowChapterVersionDiff', selectedChapter?.chapterId, leftChapterVersion?.id, rightChapterVersion?.id],
    queryFn: () => compareChapterVersions(selectedChapter!.chapterId, leftChapterVersion!.id, rightChapterVersion!.id),
    enabled: isDetailView && canCompareWorkflowVersions,
    staleTime: 15_000,
    retry: 1,
  });
  const chapterTitleById = useMemo(
    () => new Map(chapters.map((chapter) => [chapter.chapterId, chapter.title])),
    [chapters],
  );
  const selectedChapterArtifacts = selectedChapter
    ? visibleTimeline
      .filter((artifact) => artifact.chapterId === selectedChapter.chapterId)
      .sort((a, b) => artifactPriority(b) - artifactPriority(a) || Date.parse(b.updatedAt || '0') - Date.parse(a.updatedAt || '0'))
    : [];
  const selectedChapterAuditArtifacts = selectedChapterArtifacts.filter((artifact) => !isCompletionOnlyArtifact(artifact));
  const chapterDetailTabs: ChapterDetailTabOption[] = [
    ['manuscript', '正文成稿'],
    ['workflow', '天命生产链'],
    ['runtime', 'Agent 运行日志'],
    ['issues', '问题/门禁'],
  ];
  const selectedProductionStages = (workflow?.productionStages ?? []).filter((stage) => {
    if (!selectedChapter) return stage.productionEvents.length > 0 || stage.status !== 'empty';
    if (stage.productionEvents.length === 0) return stage.status !== 'empty';
    return stage.productionEvents.some((event) => event.chapterId === selectedChapter.chapterId)
      || stage.productionEvents.some((event) => !event.chapterId);
  });
  const selectedRuntimeStages = selectedProductionStages.filter((stage) =>
    (stage.taskExecutions?.length ?? 0) > 0 || toolExecutionsForStage(stage).length > 0);
  const selectedProductionChains = (workflow?.productionChains ?? []).filter((chain) => {
    if (productionChainMatchesChapter(chain, selectedChapter)) return true;
    return selectedChapter
      ? chain.steps.some((step) => step.artifactId === selectedChapter.chapterId)
      : true;
  });
  const completedProductionChapterIds = new Set((workflow?.productionChains ?? [])
    .filter(isCompletedProductionChain)
    .map((chain) => chain.chapterId)
    .filter(Boolean));
  const committedChapterUpdatedAtById = new Map(chapters
    .filter(isLibraryChapter)
    .map((chapter) => [chapter.chapterId, Date.parse(chapter.updatedAt || '0')]));
  const selectedProductionEvents = selectedProductionStages
    .flatMap((stage) => stageEventsForChapter(stage, selectedChapter?.chapterId));
  const selectedProductionEvidence = collectProductionEvidence(selectedProductionEvents, selectedProductionChains);
  const hasSelectedProductionEvidence = hasProductionEvidence(selectedProductionEvidence);
  const selectedCreativeIntents = (workflow?.creativeIntents ?? []).filter((intent) => {
    if (!selectedChapter) return true;
    return !intent.targetChapterId || intent.targetChapterId === selectedChapter.chapterId;
  });
  const chapterProgressSteps = buildChapterProgressSteps(selectedChapter, selectedChapterArtifacts);
  const selectedAuditArtifact = selectedChapterAuditArtifacts.find((artifact) => artifact.id === selectedArtifactId)
    ?? selectedChapterAuditArtifacts[0]
    ?? null;
  const selectedArtifact = visibleTimeline.find((artifact) => artifact.id === selectedArtifactId)
    ?? selectedChapterAuditArtifacts[0]
    ?? selectedChapterArtifacts[0]
    ?? visibleTimeline[0]
    ?? null;
  const selectedProgressStep = chapterProgressSteps.find((step) => step.key === selectedProgressStepKey)
    ?? chapterProgressSteps.find((step) => step.artifactId && step.artifactId === selectedArtifact?.id)
    ?? chapterProgressSteps.find((step) => step.status === 'blocked' || step.status === 'running' || step.status === 'drafting')
    ?? [...chapterProgressSteps].reverse().find((step) => step.status === 'done')
    ?? chapterProgressSteps[0]
    ?? null;
  const selectedProgressArtifact = selectedProgressStep?.artifactId
    ? selectedChapterArtifacts.find((artifact) => artifact.id === selectedProgressStep.artifactId) ?? null
    : null;
  const selectedVolume = volumes.find((volume) => volume.volumeId === selectedChapter?.volumeId) ?? volumes[0] ?? null;
  const selectedChapterWarnings = chapterQualityWarnings(selectedChapter);
  const selectedChapterProductionChips = chapterProductionSummaryChips(selectedChapter);
  const selectedProductionEvidenceCount = [
    selectedProductionEvidence.factSnapshot,
    selectedProductionEvidence.gate,
    selectedProductionEvidence.agentReview,
    selectedProductionEvidence.knowledgeBindingSummary,
    ...selectedProductionEvidence.agentReviewChecks,
    ...selectedProductionEvidence.knowledgeConstraints,
    ...selectedProductionEvidence.memoryReads,
    ...selectedProductionEvidence.memoryPromotions,
  ].filter(Boolean).length;
  const productionDashboardStats = [
    { key: 'creative', label: '创意', value: selectedCreativeIntents.length, caption: '已进入决策输入' },
    { key: 'chain', label: '链路', value: selectedProductionChains.length, caption: '可聚合生产链' },
    { key: 'version', label: '版本', value: chapterVersions.length, caption: selectedChapterVersionQuery.isLoading ? '读取中' : '章节历史' },
    { key: 'evidence', label: '证据', value: selectedProductionEvidenceCount, caption: '事实/门禁/审稿' },
    { key: 'event', label: '审计', value: selectedProductionStages.length, caption: '生产阶段' },
  ];
  const generatedCount = selectedBook?.generatedChapterCount ?? 0;
  const plannedCount = selectedBook?.plannedChapterCount ?? 0;
  const committedCount = chapters.filter(isLibraryChapter).length;
  const activeWorkflowSessionId = workflow?.activeSessionId
    || workflow?.sessions?.[0]?.sessionId
    || agentSessions?.find((session) => session.activeProjectId === currentProjectId)?.sessionId
    || '';
  const selectedProjectTaskQueue: AgentScheduledTask[] = workflow?.goalState
    ? []
    : workflow?.schedulerTasks ?? selectedPlan?.schedulerState?.tasks ?? [];
  const primaryScheduledTask = selectedProjectTaskQueue.find((task) => task.status === 'running')
    ?? selectedProjectTaskQueue.find((task) => task.status === 'blocked' || task.status === 'waiting_confirmation')
    ?? null;
  const visibleScheduledTasks = selectedProjectTaskQueue
    .filter((task) => task.taskId !== primaryScheduledTask?.taskId)
    .slice(0, 3);
  const queuedScheduledTasks = selectedProjectTaskQueue.filter((task) => taskStatusGroup(task.status) === 'queued');
  const projectBriefSource = selectedBook?.readerPromise || selectedBook?.coreHook;
  const projectBrief = compactWorkflowBrief(projectBriefSource);
  const scheduleCounts = selectedProjectTaskQueue.reduce<Record<string, number>>((acc, task) => {
    const key = taskStatusGroup(task.status);
    acc[key] = (acc[key] ?? 0) + 1;
    return acc;
  }, {});
  const scheduleText = scheduleHeadline(selectedProjectTaskQueue, scheduleCounts);
  const blockedArtifacts = timeline
    .filter((artifact) => artifact.isUserVisible && artifactClass(artifact) === 'blocked')
    .filter((artifact) => {
      if (!artifact.chapterId) return true;
      if (completedProductionChapterIds.has(artifact.chapterId)) return false;
      const committedAt = committedChapterUpdatedAtById.get(artifact.chapterId) ?? 0;
      if (committedAt <= 0) return true;
      const artifactAt = Date.parse(artifact.updatedAt || '0');
      return artifactAt > committedAt;
    })
    .slice(0, 5);
  const hasWorkflowSession = !!activeWorkflowSessionId;
  const diagnosticReasons: string[] = [
    ...(workflow?.suspectReasons ?? []),
    ...(workflow?.staleMissionWarnings ?? []),
  ];
  const diagnosticTaskCount = workflow?.diagnosticTasks?.length ?? 0;

  useEffect(() => {
    if (!routeProjectId && !currentProjectId && fallbackProjectId) {
      const book = books.find((item) => item.projectId === fallbackProjectId);
      if (book) {
        setCurrentProject(bookToProjectInfo(book));
      } else {
        setCurrentProjectId(fallbackProjectId);
      }
    }
  }, [books, fallbackProjectId, currentProjectId, routeProjectId, setCurrentProject, setCurrentProjectId]);

  useEffect(() => {
    if (!routeProjectId) return;
    const book = books.find((item) => item.projectId === routeProjectId);
    if (book) {
      setCurrentProject(bookToProjectInfo(book));
    } else {
      setCurrentProjectId(routeProjectId);
    }
  }, [books, routeProjectId, setCurrentProject, setCurrentProjectId]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSelectedArtifactId(null);
      setSelectedChapterId(null);
      setSelectedProgressStepKey(null);
      setChapterDetailTab('manuscript');
      setWorkflowLeftVersionId('');
      setWorkflowRightVersionId('');
    }, 0);
    return () => window.clearTimeout(timer);
  }, [currentProjectId]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setWorkflowLeftVersionId('');
      setWorkflowRightVersionId('');
      setSelectedProgressStepKey(null);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [selectedChapter?.chapterId]);

  useEffect(() => {
    if (chapterVersions.length === 0) return;
    const current = chapterVersions.find((version) => version.isCurrent) ?? chapterVersions[0];
    const previous = chapterVersions.find((version) => version.id !== current.id);
    const timer = window.setTimeout(() => {
      setWorkflowRightVersionId((value) => (value && chapterVersions.some((version) => version.id === value) ? value : current.id));
      setWorkflowLeftVersionId((value) => {
        if (value && chapterVersions.some((version) => version.id === value)) return value;
        return previous?.id ?? current.id;
      });
    }, 0);
    return () => window.clearTimeout(timer);
  }, [chapterVersions]);

  useEffect(() => {
    if (!workflowMenu) return;
    const closeMenu = () => setWorkflowMenu(null);
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeMenu();
    };
    window.addEventListener('click', closeMenu);
    window.addEventListener('contextmenu', closeMenu);
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      window.removeEventListener('click', closeMenu);
      window.removeEventListener('contextmenu', closeMenu);
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [workflowMenu]);

  const selectProject = (projectId: string) => {
    const book = books.find((item) => item.projectId === projectId);
    if (book) {
      setCurrentProject(bookToProjectInfo(book));
    } else {
      setCurrentProjectId(projectId);
    }
    setSelectedArtifactId(null);
    setSelectedChapterId(null);
    navigate(`/workflow/${encodeURIComponent(projectId)}`);
  };

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
    queryClient.invalidateQueries({ queryKey: ['workspace'] });
    queryClient.invalidateQueries({ queryKey: ['projectWorkflow', currentProjectId] });
  };

  const refreshAsync = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['agentSessions'] }),
      queryClient.invalidateQueries({ queryKey: ['workspace'] }),
      queryClient.invalidateQueries({ queryKey: ['projectWorkflow', currentProjectId] }),
    ]);
  };

  const agentActionMutation = useMutation({
    mutationFn: (message: string) => sendChat({ sessionId: activeWorkflowSessionId, message }),
    onSuccess: async (response) => {
      setAgentNote('');
      setWorkbenchNotice(response.reply ? 'Agent 已回复。' : '已交给 Agent。');
      setAgentLastReply(response.reply || '');
      await refreshAsync();
    },
    onError: (error) => {
      setWorkbenchNotice(error instanceof Error ? error.message : 'Agent 操作没有完成，请稍后重试。');
    },
  });

  const workflowArchiveMutation = useMutation({
    mutationFn: ({ projectId, archived }: { projectId: string; archived: boolean }) =>
      updateNovelProject(projectId, { status: archived ? 'archived' : 'drafting' }),
    onSuccess: async () => {
      await refreshAsync();
    },
  });

  const workflowRenameMutation = useMutation({
    mutationFn: ({ projectId, title }: { projectId: string; title: string }) =>
      updateNovelProject(projectId, { title }),
    onSuccess: async (_result, variables) => {
      if (currentProjectId === variables.projectId && selectedBook) {
        setCurrentProject({ ...bookToProjectInfo(selectedBook), title: variables.title });
      }
      await refreshAsync();
    },
  });

  const workflowDeleteMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async (_result, projectId) => {
      if (currentProjectId === projectId) {
        setCurrentProject(null);
        setCurrentProjectId(null);
        navigate('/workflow');
      }
      await refreshAsync();
    },
  });

  const openWorkflowRenameDialog = (book: NovelBookView) => {
    setWorkflowMenu(null);
    setWorkflowDialog({ mode: 'rename', book });
    setWorkflowTitleDraft(book.title);
  };

  const closeWorkflowDialog = () => {
    if (workflowRenameMutation.isPending) return;
    setWorkflowDialog(null);
    setWorkflowTitleDraft('');
  };

  const submitWorkflowDialog = async () => {
    if (!workflowDialog) return;
    const title = workflowTitleDraft.trim();
    if (!title || title === workflowDialog.book.title) {
      closeWorkflowDialog();
      return;
    }

    await workflowRenameMutation.mutateAsync({
      projectId: workflowDialog.book.projectId,
      title,
    });
    closeWorkflowDialog();
  };

  const toggleWorkflowArchive = async (book: NovelBookView, returnToOverview = false) => {
    setWorkflowMenu(null);
    const archived = !isArchivedProject(book);
    const message = archived
      ? `归档「${book.title}」？归档后默认不会出现在工作流总览，但可以通过“显示归档”查看和恢复。`
      : `恢复「${book.title}」到工作流总览？`;
    if (!window.confirm(message)) return;
    await workflowArchiveMutation.mutateAsync({ projectId: book.projectId, archived });
    if (returnToOverview) {
      setCurrentProjectId(null);
      navigate('/workflow');
    }
  };

  const deleteWorkflowProject = async (book: NovelBookView) => {
    setWorkflowMenu(null);
    const hasOutput = book.generatedChapterCount > 0 || book.plannedChapterCount > 0 || book.volumeCount > 0;
    const warning = hasOutput
      ? `「${book.title}」已有卷、章节位或过程产物。删除会移除项目、章节和相关数据，且不可恢复。确定删除吗？`
      : `删除空工作流「${book.title}」？该操作不可恢复。`;
    if (!window.confirm(warning)) return;
    await workflowDeleteMutation.mutateAsync(book.projectId);
  };

  const submitAgentNote = async () => {
    if (!currentProjectId || !activeWorkflowSessionId) {
      setWorkbenchNotice('未绑定可操作会话，无法发送给 Agent。');
      return;
    }
    if (!agentNote.trim()) {
      setWorkbenchNotice('请先写下要补充、调整或询问的内容。');
      return;
    }
    setWorkbenchNotice('正在发送给 Agent...');
    setAgentLastReply('');
    const message = buildAgentContextMessage({
      projectId: currentProjectId,
      sessionId: activeWorkflowSessionId,
      selectedArtifact,
      selectedChapter,
      userFeedback: agentNote.trim(),
    });
    await agentActionMutation.mutateAsync(message);
  };

  if (!isDetailView) {
    const activeProjectCount = workflowBooks.filter((book) => book.isActive).length;
    const projectWithOutputCount = workflowBooks.filter((book) => book.generatedChapterCount > 0 || book.plannedChapterCount > 0).length;
    const totalPlannedCount = workflowBooks.reduce((sum, book) => sum + book.plannedChapterCount, 0);
    const totalGeneratedCount = workflowBooks.reduce((sum, book) => sum + book.generatedChapterCount, 0);
    const workflowEntryCount = workflowBooks.length + missionOnlyCards.length;

    return (
      <>
        <Topbar
          title="创作工作流"
          actions={
            <button className="ghost-button" onClick={refresh}>
              刷新
            </button>
          }
        />

        <div className="ops-workspace workflow-overview">
          <section className="workflow-overview-board">
            <div className="workflow-overview-toolbar">
              <div>
                <span className="ops-kicker">Workflows</span>
                <h3>项目工作流</h3>
              </div>
              <div className="workflow-overview-summary">
                <div className="ops-status-grid">
                  <strong>{workflowBooks.length}<small>项目</small></strong>
                  <strong>{projectWithOutputCount}<small>有产物</small></strong>
                  <strong>{totalPlannedCount}<small>章节位</small></strong>
                  <strong>{totalGeneratedCount}<small>过程章</small></strong>
                </div>
                <div className="ops-live-card">
                  <span>当前工作台</span>
                  <strong>{activeProjectCount > 0 ? `${activeProjectCount} 个 active 项目` : '等待选择项目'}</strong>
                  <small>{agentSessions?.length ?? 0} 个 Agent 会话</small>
                </div>
                <button
                  type="button"
                  className={`workflow-archive-toggle ${showArchivedWorkflows ? 'active' : ''}`}
                  onClick={() => setShowArchivedWorkflows((value) => !value)}
                  disabled={archivedBooks.length === 0}
                >
                  {showArchivedWorkflows ? '隐藏归档' : `归档 ${archivedBooks.length}`}
                </button>
                <small>{workspaceLoading ? '正在加载项目...' : `共 ${workflowEntryCount} 个工作流入口`}</small>
              </div>
            </div>

            {workspaceLoading && <div className="workflow-loading">加载项目中...</div>}
            {isError && <div className="workflow-error">加载失败，请刷新重试</div>}
            {!workspaceLoading && !isError && workflowBooks.length === 0 && missionOnlyCards.length === 0 && (
              <div className="workflow-empty">暂无活跃项目</div>
            )}

            <div className="workflow-overview-grid">
              {workflowBooks.map((book) => {
                const relatedSessions = (agentSessions ?? []).filter((session) => session.activeProjectId === book.projectId);
                const score = bookActivityScore(book, agentSessions ?? []);
                return (
                  <article
                    key={book.projectId}
                    className={`workflow-overview-card ${book.isActive ? 'active' : ''} ${book.projectId === storeProjectId ? 'selected' : ''} ${isArchivedProject(book) ? 'archived' : ''}`}
                    role="button"
                    tabIndex={0}
                    onClick={() => selectProject(book.projectId)}
                    onContextMenu={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      setWorkflowMenu({ book, x: event.clientX, y: event.clientY });
                    }}
                    onKeyDown={(event) => {
                      if (event.key !== 'Enter' && event.key !== ' ') return;
                      event.preventDefault();
                      selectProject(book.projectId);
                    }}
                  >
                    <div className="workflow-card-head">
                      <span>{book.status || 'Drafting'} · {projectShortId(book.projectId)}</span>
                      <em>{isArchivedProject(book) ? 'Archived' : book.isActive ? 'Active' : score > 0 ? '有活动' : '空项目'}</em>
                    </div>
                    <strong>{book.title}</strong>
                    <p>{book.coreHook || book.readerPromise || book.selectedChapter?.summary || '等待 Agent 补齐故事地基和章节计划。'}</p>
                    <i><b style={{ width: `${progressPercent(book)}%` }} /></i>
                    <div className="workflow-card-metrics">
                      <span><b>{book.volumeCount}</b><small>卷</small></span>
                      <span><b>{book.plannedChapterCount}</b><small>章节位</small></span>
                      <span><b>{book.generatedChapterCount}</b><small>过程章</small></span>
                      <span><b>{relatedSessions.length}</b><small>会话</small></span>
                    </div>
                    <small className="workflow-card-time">更新 {formatTime(book.updatedAt)}</small>
                  </article>
                );
              })}

              {missionOnlyCards.map(({ session, plan, projectId }) => (
                <button
                  key={session.sessionId}
                  type="button"
                  className={`workflow-overview-card mission ${projectId === storeProjectId ? 'selected' : ''}`}
                  onClick={() => selectProject(projectId)}
                >
                  <div className="workflow-card-head">
                    <span>{missionStageLabel(plan)}</span>
                    <em>任务会话</em>
                  </div>
                  <strong>{plan.projectTitle || session.title || 'Agent 小说任务'}</strong>
                  <p>{plan.currentNovelGoal || plan.overallGoal || session.phase}</p>
                  <div className="workflow-card-metrics">
                    <span><b>{missionChapters(plan).length}</b><small>章任务</small></span>
                    <span><b>{plan.schedulerState?.tasks.length ?? 0}</b><small>调度</small></span>
                  </div>
                  <small className="workflow-card-time">更新 {formatTime(session.updatedAt)}</small>
                </button>
              ))}
            </div>
          </section>
        </div>

        {workflowMenu && (
          <div
            className="workflow-context-menu"
            style={{ left: workflowMenu.x, top: workflowMenu.y }}
            onClick={(event) => event.stopPropagation()}
            onContextMenu={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
          >
            <button
              type="button"
              onClick={() => openWorkflowRenameDialog(workflowMenu.book)}
              disabled={workflowRenameMutation.isPending}
            >
              改名
            </button>
            <button
              type="button"
              onClick={() => void toggleWorkflowArchive(workflowMenu.book)}
              disabled={workflowArchiveMutation.isPending}
            >
              {isArchivedProject(workflowMenu.book) ? '恢复' : '归档'}
            </button>
            <button
              type="button"
              className="danger"
              onClick={() => void deleteWorkflowProject(workflowMenu.book)}
              disabled={workflowDeleteMutation.isPending}
            >
              删除
            </button>
          </div>
        )}

        {workflowDialog && (
          <div
            className="workflow-dialog-backdrop"
            role="presentation"
            onMouseDown={(event) => {
              if (event.target === event.currentTarget) closeWorkflowDialog();
            }}
          >
            <section
              className="workflow-action-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="workflow-action-dialog-title"
              onMouseDown={(event) => event.stopPropagation()}
            >
              <div className="workflow-action-dialog-head">
                <span>Rename Workflow</span>
                <h3 id="workflow-action-dialog-title">重命名工作流</h3>
              </div>

              <label className="workflow-action-field">
                <span>工作流名称</span>
                <input
                  autoFocus
                  value={workflowTitleDraft}
                  placeholder="输入新的工作流名称"
                  onChange={(event) => setWorkflowTitleDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void submitWorkflowDialog();
                    if (event.key === 'Escape') closeWorkflowDialog();
                  }}
                />
              </label>

              <div className="workflow-action-dialog-actions">
                <button
                  className="ghost-button compact"
                  type="button"
                  onClick={closeWorkflowDialog}
                  disabled={workflowRenameMutation.isPending}
                >
                  取消
                </button>
                <button
                  className="ink-button"
                  type="button"
                  onClick={() => void submitWorkflowDialog()}
                  disabled={workflowRenameMutation.isPending || !workflowTitleDraft.trim()}
                >
                  {workflowRenameMutation.isPending ? '保存中...' : '确认'}
                </button>
              </div>
            </section>
          </div>
        )}
      </>
    );
  }

  return (
    <>
      <Topbar
        title="创作工作流"
        actions={
          <>
            <button className="ghost-button" onClick={() => navigate('/workflow')}>
              返回总览
            </button>
            <button className="ghost-button" onClick={refresh}>
              刷新
            </button>
          </>
        }
      />

      <div className="ops-workspace workflow-detail chapter-detail-page">
        <section className="workflow-project-strip">
          <div className="workflow-project-title">
            <span className="ops-kicker">Chapter Workflow</span>
            <h2>{selectedBook?.title || '未选择小说任务'}</h2>
            <p title={projectBriefSource || projectBrief}>{projectBrief}</p>
          </div>
          <div className="workflow-project-metrics">
            <strong>{committedCount}<small>入库章</small></strong>
            <strong>{plannedCount}<small>章节位</small></strong>
            <strong>{selectedBook?.generatedChapterCount ?? generatedCount}<small>过程章</small></strong>
            <strong>{selectedBook?.status || 'Drafting'}<small>状态</small></strong>
          </div>
          {selectedBook && (
            <div className="workflow-project-actions">
              <button
                className="ghost-button compact"
                type="button"
                onClick={() => void toggleWorkflowArchive(selectedBook, true)}
                disabled={workflowArchiveMutation.isPending}
              >
                {isArchivedProject(selectedBook) ? '恢复工作流' : '归档工作流'}
              </button>
              <button
                className="danger-button compact"
                type="button"
                onClick={() => void deleteWorkflowProject(selectedBook)}
                disabled={workflowDeleteMutation.isPending}
              >
                删除工作流
              </button>
            </div>
          )}
        </section>

        {workflow?.latestGoalId ? (
          <>
            {workflow.goalState && (
              <section className="project-goal-workbench">
                <div>
                  <span>Goal State Machine</span>
                  <h3>批次 {workflow.goalState.currentBatchNumber} · {workflow.goalState.productionStatus}</h3>
                  <p>
                    {workflow.goalState.executionStrategy === 'full_auto' ? '全自动推进' : '分批交互推进'}
                    {' · '}下一章 {workflow.goalState.nextChapterNumber || '—'}
                    {' · '}任务图 v{workflow.goalState.activeTaskGraphVersion || '—'}
                  </p>
                </div>
                <div className="workflow-project-metrics">
                  <strong>{workflow.goalState.tasks.filter((task) => task.status === 'completed' || task.status === 'reused').length}<small>已完成任务</small></strong>
                  <strong>{workflow.goalState.tasks.filter((task) => task.status === 'ready' || task.status === 'running').length}<small>执行中</small></strong>
                  <strong>{workflow.goalState.candidates.length}<small>候选版本</small></strong>
                  <strong>{workflow.goalState.artifacts.length}<small>权威产物</small></strong>
                </div>
              </section>
            )}
            <ProjectGoalWorkbench goalId={workflow.latestGoalId} />
          </>
        ) : (
          <section className="project-goal-workbench empty-state">
            <div>
              <span>Book Production</span>
              <h3>等待创作合同</h3>
              <p>在 Agent 会话中确认整书目标后，批次计划、候选正文和验收操作会出现在这里。</p>
            </div>
            <button type="button" className="ink-button" onClick={() => navigate('/')}>
              前往 Agent 会话
            </button>
          </section>
        )}

        <section className="workflow-chapter-board">
          <aside className="workflow-chapter-index">
            <div className="ops-panel-head">
              <span>章节目录</span>
              <strong>{chapters.length}</strong>
            </div>
            {workflowLoading ? (
              <div className="empty compact">正在载入项目工作流...</div>
            ) : volumes.length === 0 ? (
              <div className="empty compact">这个项目还没有真实卷或章节。</div>
            ) : volumes.map((volume) => (
              <section key={volume.volumeId} className="ops-volume-group">
                <div className="ops-volume-title">
                  <strong>{volume.title}</strong>
                  <span>{volume.chapters.length}/{volume.expectedChapterCount || volume.chapters.length} 章</span>
                </div>
                {volume.chapters.length === 0 ? (
                  <div className="ops-chapter-empty">还没有章节实体</div>
                ) : volume.chapters.map((chapter) => {
                  const warnings = chapterQualityWarnings(chapter);
                  return (
                    <button
                      key={chapter.chapterId}
                      className={`ops-chapter-row ${selectedChapter?.chapterId === chapter.chapterId ? 'active' : ''}`}
                      onClick={() => {
                        setSelectedChapterId(chapter.chapterId);
                        setSelectedArtifactId(null);
                        setChapterDetailTab(hasReadableManuscript(chapter) ? 'manuscript' : 'workflow');
                      }}
                    >
                      <i className={chapterStatusClass(chapter)}>{chapter.beatIndex || '-'}</i>
                      <span>
                        <strong>{chapter.title}</strong>
                        <small>{chapterStatusLabel(chapter)} · {chapter.wordCount || 0} 字{warnings.length > 0 ? ' · 有问题' : ''}</small>
                      </span>
                    </button>
                  );
                })}
              </section>
            ))}
          </aside>

          <main className="workflow-chapter-main">
            <div className="workflow-chapter-head">
              <div>
                <span>{selectedVolume?.title || '章节'}</span>
                <h3>{selectedChapter?.title || '等待真实章节'}</h3>
                <p>{chapterHeaderSummary(selectedChapter, selectedArtifact)}</p>
                {selectedChapterProductionChips.length > 0 && (
                  <div className="workflow-chapter-production-chips" aria-label="章节生产摘要">
                    {selectedChapterProductionChips.map((chip) => (
                      <em key={chip} title={selectedChapter?.productionSummary?.endingState || chip}>{chip}</em>
                    ))}
                  </div>
                )}
              </div>
              <strong className={`ops-state ${selectedChapter ? chapterStatusClass(selectedChapter) : artifactClass(selectedArtifact)}`}>
                {selectedChapter ? chapterStatusLabel(selectedChapter) : '未开始'}
              </strong>
            </div>

            <div className="workflow-chapter-facts">
              <span><b>{selectedChapter?.wordCount || 0}</b><small>正文字数</small></span>
              <span><b>{selectedChapterAuditArtifacts.length}</b><small>过程审计</small></span>
              <span><b>{selectedChapter?.qualityScore || 0}</b><small>质量分</small></span>
              <span><b>{selectedChapter?.ragRecallCount || 0}</b><small>召回</small></span>
            </div>

            {selectedChapterWarnings.length > 0 && (
              <div className="workflow-warning-list">
                {selectedChapterWarnings.map((warning) => <p key={warning}>{warning}</p>)}
              </div>
            )}

            <div className="workflow-detail-tabs">
              {chapterDetailTabs.map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  className={chapterDetailTab === key ? 'selected' : ''}
                  onClick={() => setChapterDetailTab(key)}
                >
                  {label}
                </button>
              ))}
            </div>

            {chapterDetailTab === 'manuscript' && (
              <section className="workflow-manuscript-panel">
                {selectedChapter && hasReadableManuscript(selectedChapter) ? (
                  <>
                    <div className="ops-card-title">
                      <div>
                        <span>小说书城 · 最终正文</span>
                        <h4>{selectedChapter.title}</h4>
                      </div>
                      <em>{formatTime(selectedChapter.updatedAt)}</em>
                    </div>
                    <article className="workflow-manuscript-reader">
                      {manuscriptPreview(selectedChapter.content).map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                    </article>
                  </>
                ) : (
                  <div className="empty-panel">
                    <p>这一章还没有书城正文。请在“工作流步骤”里查看草稿、门禁或候选产物。</p>
                  </div>
                )}
              </section>
            )}

            {chapterDetailTab === 'workflow' && (
              <section className="workflow-tab-panel">
                <section className="workflow-production-dashboard" aria-label="章节生产依据">
                  <div className="workflow-production-dashboard-head">
                    <div>
                      <span>生产依据</span>
                      <strong>{selectedChapter ? selectedChapter.title : '项目全局'}</strong>
                    </div>
                    <em>创意、生产链、版本、证据与审计统一归档</em>
                  </div>

                  <div className="workflow-production-dashboard-summary">
                    {productionDashboardStats.map((stat) => (
                      <span key={stat.key}>
                        <b>{stat.value}</b>
                        <small>{stat.label}</small>
                        <em>{stat.caption}</em>
                      </span>
                    ))}
                  </div>

                  <div className="workflow-production-dashboard-grid">
                    {renderCreativeInbox(selectedCreativeIntents, (intentId) => {
                      navigate(`/agent?recoveryIntentId=${encodeURIComponent(intentId)}`);
                    })}
                    {renderChapterCanonicalSummary(selectedChapter) ?? (
                      <section className="workflow-chapter-canonical-summary workflow-dashboard-empty-card" aria-label="章节生产链">
                        <div className="workflow-chapter-canonical-head">
                          <div>
                            <span>章节生产链</span>
                            <strong>暂无决策链</strong>
                          </div>
                          <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
                        </div>
                        <p>这一章还没有可聚合的生产决策摘要。</p>
                      </section>
                    )}
                    {renderProductionChains(selectedProductionChains, selectedChapter)}

                    <section className="workflow-version-diff-panel workflow-production-dashboard-wide" aria-label="章节版本差异">
                      <div className="workflow-version-diff-head">
                        <div>
                          <span>章节版本差异</span>
                          <strong>
                            {selectedChapterVersionQuery.isLoading
                              ? '读取版本中'
                              : chapterVersions.length > 0
                                ? `${chapterVersions.length} 个版本`
                                : '暂无版本'}
                          </strong>
                        </div>
                        {selectedChapterVersionQuery.isError ? (
                          <em className="error">版本记录读取失败</em>
                        ) : workflowVersionDiffQuery.isError ? (
                          <em className="error">差异读取失败</em>
                        ) : workflowVersionDiffQuery.isFetching ? (
                          <em>正在对比...</em>
                        ) : workflowVersionDiffQuery.data ? (
                          <em>{workflowVersionDiffQuery.data.wordCountDelta >= 0 ? '+' : ''}{workflowVersionDiffQuery.data.wordCountDelta} 字</em>
                        ) : (
                          <em>{selectedChapter ? selectedChapter.title : '未选择章节'}</em>
                        )}
                      </div>

                      {selectedChapterVersionQuery.isLoading ? (
                        <div className="empty compact">正在读取这一章的版本历史...</div>
                      ) : selectedChapterVersionQuery.isError ? (
                        <div className="workflow-version-diff-empty error">后端版本接口没有返回有效数据，请确认运行中的后端已加载最新代码。</div>
                      ) : chapterVersions.length < 2 ? (
                        <div className="workflow-version-diff-empty">这一章还没有可对比的历史版本。</div>
                      ) : (
                        <>
                          <div className="workflow-version-pickers">
                            <label>
                              <span>基准</span>
                              <select value={leftChapterVersion?.id ?? ''} onChange={(event) => setWorkflowLeftVersionId(event.target.value)}>
                                {chapterVersions.map((version) => (
                                  <option key={version.id} value={version.id}>
                                    {chapterVersionOptionLabel(version)}
                                  </option>
                                ))}
                              </select>
                            </label>
                            <label>
                              <span>目标</span>
                              <select value={rightChapterVersion?.id ?? ''} onChange={(event) => setWorkflowRightVersionId(event.target.value)}>
                                {chapterVersions.map((version) => (
                                  <option key={version.id} value={version.id}>
                                    {chapterVersionOptionLabel(version)}
                                  </option>
                                ))}
                              </select>
                            </label>
                          </div>

                          <div className="workflow-version-summary">
                            <span title={leftChapterVersion?.contentDocumentId || ''}>{chapterVersionLabel(leftChapterVersion)}</span>
                            <i />
                            <span title={rightChapterVersion?.contentDocumentId || ''}>{chapterVersionLabel(rightChapterVersion)}</span>
                          </div>

                          {!canCompareWorkflowVersions ? (
                            <div className="workflow-version-diff-empty">请选择两个不同版本进行对比。</div>
                          ) : workflowVersionDiffQuery.data ? (
                            <>
                              <p className="workflow-version-summary-text">{workflowVersionDiffQuery.data.summary || '已完成版本差异对比。'}</p>
                              {workflowAlignmentChips(workflowVersionDiffQuery.data.productionAlignment).length > 0 && (
                                <div className="workflow-version-alignment-strip" aria-label="版本生产对照">
                                  {workflowAlignmentChips(workflowVersionDiffQuery.data.productionAlignment).map((chip) => (
                                    <span key={chip.key} className={chip.tone} title={chip.value}>
                                      <strong>{chip.label}</strong>
                                      <em>{chip.value}</em>
                                    </span>
                                  ))}
                                </div>
                              )}
                              <div className="workflow-version-diff-list">
                                {workflowVersionDiffQuery.data.diffBlocks
                                  .filter((block) => block.kind !== 'unchanged')
                                  .slice(0, 6)
                                  .map((block, index) => (
                                    <article
                                      key={`${block.kind}-${index}-${block.leftText.slice(0, 16)}-${block.rightText.slice(0, 16)}`}
                                      className={`workflow-version-diff-row ${block.kind}`}
                                      title={diffBlockTitle(block)}
                                    >
                                      <span>{diffKindLabel(block.kind)}</span>
                                      <div>
                                        {block.leftText && <p>{block.leftText}</p>}
                                        {block.rightText && <p>{block.rightText}</p>}
                                      </div>
                                    </article>
                                  ))}
                                {workflowVersionDiffQuery.data.diffBlocks.filter((block) => block.kind !== 'unchanged').length === 0 && (
                                  <div className="workflow-version-diff-empty">两个版本正文没有段落级差异。</div>
                                )}
                              </div>
                            </>
                          ) : (
                            <div className="workflow-version-diff-empty">等待差异结果...</div>
                          )}
                        </>
                      )}
                    </section>

                    {hasSelectedProductionEvidence && (
                      <details className="workflow-production-evidence-board workflow-production-evidence-audit" aria-label="生产证据审计">
                        <summary className="workflow-production-events-head">
                          <div>
                            <span>生产证据审计</span>
                            <strong>事实 / 门禁 / 总编</strong>
                          </div>
                          <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
                        </summary>
                        <div className="workflow-evidence-card-grid">
                          {renderKnowledgeBindingSummary(selectedProductionEvidence.knowledgeBindingSummary)}
                          {renderFactSnapshotCard(selectedProductionEvidence.factSnapshot)}
                          {renderGateEvidenceCard(selectedProductionEvidence.gate)}
                          {renderAgentReviewSummaryCard(selectedProductionEvidence.agentReview, selectedProductionEvidence.agentReviewChecks)}
                          {renderKnowledgeConstraintEvidence(selectedProductionEvidence.knowledgeConstraints)}
                          {renderMemoryEvidenceCard(selectedProductionEvidence.memoryReads, selectedProductionEvidence.memoryPromotions)}
                        </div>
                      </details>
                    )}
                  </div>

                  <details className="workflow-production-dashboard-audit" aria-label="详细生产审计">
                    <summary className="workflow-production-events-head">
                      <div>
                        <span>详细审计</span>
                        <strong>{selectedProductionStages.length} 个阶段 / {selectedChapterAuditArtifacts.length} 条过程证据</strong>
                      </div>
                      <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
                    </summary>
                    <section className="workflow-production-events workflow-production-stage-details" aria-label="天命生产阶段">
                      <div className="workflow-production-events-head workflow-production-events-head-static">
                        <div>
                          <span>生产事件审计</span>
                          <strong>{selectedProductionStages.length} 个阶段</strong>
                        </div>
                        <em>{selectedChapter ? selectedChapter.title : '项目全局'}</em>
                      </div>
                      {selectedProductionStages.length === 0 ? (
                        <div className="empty compact">还没有生产内核事件。</div>
                      ) : (
                        <div className="workflow-production-stage-grid">
                          {selectedProductionStages.map((stage) => {
                            const events = stageEventsForChapter(stage, selectedChapter?.chapterId);
                            return (
                              <article key={stage.key} className={`workflow-production-stage ${productionStageClass(stage)}`}>
                                <div className="workflow-production-stage-title">
                                  <span>{stage.surface}</span>
                                  <strong>{stage.label}</strong>
                                  <em>{stage.status}</em>
                                </div>
                                <p>{productionStageCaption(stage)}</p>
                                {events.length > 0 && (
                                  <div className="workflow-production-event-list">
                                    {events.map((event) => {
                                      const chips = productionEvidenceChips(event);
                                      return (
                                        <div key={event.id} className={`workflow-production-event ${event.status}`}>
                                          <span>{formatTime(event.createdAt)}</span>
                                          <strong>{event.message || event.stage}</strong>
                                          <small>{compactEventData(event.dataJson) || event.eventType}</small>
                                          {chips.length > 0 && (
                                            <div className="workflow-production-evidence">
                                              {chips.map((chip) => <i key={chip}>{chip}</i>)}
                                            </div>
                                          )}
                                          {renderProductionFailure(event)}
                                          {renderKnowledgeBindingSummary(event.evidence?.knowledgeBindingSummary)}
                                          {renderKnowledgeBindings(event.evidence?.knowledgeBindings)}
                                          {renderKnowledgeConstraintEvidence(event.evidence?.knowledgeConstraintEvidence)}
                                          {renderCreativeIntents(event.evidence?.creativeIntents)}
                                          {renderRevisionPlanEvidence(event.evidence?.sourceRevisionPlans)}
                                          {renderRollbackEvidence(event.evidence?.rollback)}
                                          {renderAgentReviewChecks(event.evidence?.agentReviewChecks)}
                                          {renderMemoryEvidenceCard(event.evidence?.memoryReads, event.evidence?.memoryPromotions)}
                                        </div>
                                      );
                                    })}
                                  </div>
                                )}
                                {/* 工具执行日志已迁移到 Runtime Tab，此处只展示天命生产链阶段 */}
                              </article>
                            );
                          })}
                        </div>
                      )}
                    </section>
                  </details>
                </section>

                <section className="workflow-chapter-progress workflow-chapter-flow" aria-label="本章流程概览">
                  <div className="workflow-chapter-progress-head">
                    <div>
                      <span>本章流程</span>
                      <strong>{selectedChapter ? chapterStatusLabel(selectedChapter) : '未选择章节'}</strong>
                    </div>
                    <em>{selectedProgressStep?.label || '等待章节产物'}</em>
                  </div>
                  <div className="workflow-chapter-flow-body">
                    <div className="workflow-chapter-progress-track">
                      {chapterProgressSteps.map((step, index) => (
                        <button
                          key={step.key}
                          type="button"
                          className={`workflow-progress-step ${step.status} ${selectedProgressStep?.key === step.key ? 'selected' : ''}`}
                          title={step.summary}
                          onClick={() => {
                            setSelectedProgressStepKey(step.key);
                            if (step.artifactId) setSelectedArtifactId(step.artifactId);
                          }}
                        >
                          <i>{index + 1}</i>
                          <span>{step.label}</span>
                          <em>{step.caption}</em>
                        </button>
                      ))}
                    </div>
                    <section className={`workflow-progress-detail ${selectedProgressStep?.status ?? 'unstarted'}`}>
                      <div className="ops-card-title">
                        <div>
                          <span>当前步骤</span>
                          <h4>{selectedProgressStep?.label || '未选择步骤'}</h4>
                        </div>
                        <em>{selectedProgressStep?.caption || '暂无状态'}</em>
                      </div>
                      <p>{selectedProgressStep?.summary || '这个步骤还没有可展示的产物。'}</p>
                      {selectedProgressArtifact ? (
                        <>
                          <div className="workflow-progress-artifact">
                            <span>{selectedProgressArtifact.surface} · {selectedProgressArtifact.label}</span>
                            <strong title={artifactDisplayTitle(selectedProgressArtifact, chapterTitleById)}>
                              {artifactDisplayTitle(selectedProgressArtifact, chapterTitleById)}
                            </strong>
                            <em>{formatTime(selectedProgressArtifact.updatedAt)}</em>
                          </div>
                          {selectedProgressArtifact.preview && selectedProgressArtifact.kind !== 'library_chapter' && (
                            <div className="ops-draft-preview workflow-progress-preview">
                              {visiblePreview(selectedProgressArtifact.preview)
                                .split(/\n{2,}| \/ /)
                                .filter(Boolean)
                                .slice(0, 5)
                                .map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                            </div>
                          )}
                        </>
                      ) : (
                        <div className="workflow-progress-empty">
                          <span>未生成对应产物</span>
                          <p>{selectedProgressStep?.status === 'unstarted' ? '这一环节还没有开始，后续生成后会自动出现在这里。' : '当前步骤已有状态，但没有绑定可读产物，请展开下方产物记录或生产事件审计查看来源。'}</p>
                        </div>
                      )}
                    </section>
                  </div>
                </section>
                <details className="workflow-step-audit">
                  <summary className="workflow-artifact-toolbar">
                    <div>
                      <span>过程审计</span>
                      <strong>{selectedChapterAuditArtifacts.length} 条过程证据</strong>
                    </div>
                    <em>{selectedAuditArtifact ? artifactDisplayTitle(selectedAuditArtifact, chapterTitleById) : '无额外过程证据'}</em>
                  </summary>
                  <div className="workflow-step-audit-body">
                    <div className="workflow-step-grid" aria-label="本章工作流产物记录">
                      {selectedChapterAuditArtifacts.length === 0 ? (
                        <div className="empty compact">这一章没有需要单独展开的过程证据。</div>
                      ) : selectedChapterAuditArtifacts.map((artifact, index) => (
                        <button
                          key={artifactRenderKey(artifact, index)}
                          type="button"
                          className={`ops-artifact-card ${selectedAuditArtifact?.id === artifact.id ? 'selected' : ''} ${artifactClass(artifact)}`}
                          onClick={() => setSelectedArtifactId(artifact.id)}
                        >
                          <span>{artifact.surface} · {artifact.label}</span>
                          <strong>{artifactDisplayTitle(artifact, chapterTitleById)}</strong>
                          <small>{artifactSummaryText(artifact, chapterTitleById)}</small>
                          <em>{formatTime(artifact.updatedAt)}</em>
                        </button>
                      ))}
                    </div>
                    {selectedAuditArtifact && selectedAuditArtifact.chapterId === selectedChapter?.chapterId ? (
                      <section className="ops-artifact-detail">
                        <div className="ops-card-title">
                          <div>
                            <span>{selectedAuditArtifact.surface} · {selectedAuditArtifact.label}</span>
                            <h4>{artifactDisplayTitle(selectedAuditArtifact, chapterTitleById)}</h4>
                          </div>
                          <em>{formatTime(selectedAuditArtifact.updatedAt)}</em>
                        </div>
                        <p>{selectedAuditArtifact.summary || '暂无摘要。'}</p>
                        {selectedAuditArtifact.preview && selectedAuditArtifact.kind !== 'library_chapter' && (
                          <div className="ops-draft-preview">
                            {visiblePreview(selectedAuditArtifact.preview)
                              .split(/\n{2,}| \/ /)
                              .filter(Boolean)
                              .slice(0, 8)
                              .map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                          </div>
                        )}
                        <div className="ops-artifact-meta">
                          <span>{selectedAuditArtifact.kind}</span>
                          {selectedAuditArtifact.runId && <span>run {projectShortId(selectedAuditArtifact.runId)}</span>}
                          <span>过程证据</span>
                        </div>
                      </section>
                    ) : (
                      <section className="ops-artifact-detail empty-detail">
                        <div className="ops-card-title">
                          <div>
                            <span>过程审计</span>
                            <h4>无需展开额外日志</h4>
                          </div>
                        </div>
                        <p>最终正文、书城入库和章节状态不在这里重复展示；日常查看优先使用上方本章流程。</p>
                      </section>
                    )}
                  </div>
                </details>
              </section>
            )}

            {chapterDetailTab === 'runtime' && (
              <section className="workflow-tab-panel">
                <section className="workflow-runtime-log-panel" aria-label="Agent 运行日志">
                  <div className="workflow-runtime-log-head">
                    <div>
                      <span>{workflow?.projectionKind === 'goal' ? 'Goal 任务执行' : 'Agent 运行日志'}</span>
                      <strong>{selectedChapter ? selectedChapter.title : '项目全局'}</strong>
                    </div>
                    <em>{workflow?.projectionKind === 'goal'
                      ? '展示持久 KernelTask、输入输出产物和失败原因'
                      : '展示旧项目的只读 Agent Runtime 工具调用记录'}</em>
                  </div>
                  {selectedRuntimeStages.length === 0 ? (
                    <p className="workflow-runtime-log-empty">当前没有可展示的任务执行记录。</p>
                  ) : (
                    <div className="workflow-runtime-log-stages">
                      {selectedRuntimeStages.map((stage) => {
                        const tasks = stage.taskExecutions ?? [];
                        const tools = toolExecutionsForStage(stage);
                        return (
                          <article key={`runtime-${stage.key}`} className={`workflow-runtime-log-stage ${stage.status}`}>
                            <div className="workflow-runtime-log-stage-head">
                              <span>{stage.label}</span>
                              <em>{tasks.length > 0 ? `${tasks.length} 个任务` : `${tools.length} 次工具调用`}</em>
                            </div>
                            {tasks.length > 0 ? renderTaskExecutions(tasks) : renderToolExecutions(tools)}
                          </article>
                        );
                      })}
                    </div>
                  )}
                </section>
              </section>
            )}

            {chapterDetailTab === 'issues' && (
              <section className="workflow-tab-panel">
                <div className="workflow-issue-grid">
                  <article>
                    <span>结构门禁</span>
                    <strong>{selectedChapter?.gateStatus || '暂无'}</strong>
                    {(selectedChapter?.gateIssues.length ?? 0) === 0 ? (
                      <p>没有结构门禁阻塞。</p>
                    ) : selectedChapter?.gateIssues.map((issue) => <p key={issue}>{issue}</p>)}
                  </article>
                  <article>
                    <span>修复建议</span>
                    <strong>{selectedChapter?.repairHints.length ?? 0}</strong>
                    {(selectedChapter?.repairHints.length ?? 0) === 0 ? (
                      <p>暂无修复建议。</p>
                    ) : selectedChapter?.repairHints.map((hint) => <p key={hint}>{hint}</p>)}
                  </article>
                  <article>
                    <span>质量评审</span>
                    <strong>{selectedChapter?.qualityScore || 0}</strong>
                    {(selectedChapter?.reviewChecks.length ?? 0) === 0 && (selectedChapter?.nextSuggestions.length ?? 0) === 0 ? (
                      <p>暂无质量评审细项。</p>
                    ) : [...(selectedChapter?.reviewChecks ?? []), ...(selectedChapter?.nextSuggestions ?? [])].map((item) => <p key={item}>{item}</p>)}
                  </article>
                  <article>
                    <span>上下文/依赖</span>
                    <strong>{(selectedChapter?.contextWarnings.length ?? 0) + (selectedChapter?.dependencyWarnings.length ?? 0)}</strong>
                    {(selectedChapter?.contextWarnings.length ?? 0) + (selectedChapter?.dependencyWarnings.length ?? 0) === 0 ? (
                      <p>暂无上下文或依赖警告。</p>
                    ) : [...(selectedChapter?.contextWarnings ?? []), ...(selectedChapter?.dependencyWarnings ?? [])].map((item) => <p key={item}>{item}</p>)}
                  </article>
                </div>
              </section>
            )}

          </main>

          <details className="ops-side-panel workflow-agent-dock">
            <summary className="workflow-agent-dock-summary">
              <span>Agent 工作台</span>
              <strong>{scheduleText}{blockedArtifacts.length ? ` · ${blockedArtifacts.length} 个阻塞` : ''}</strong>
            </summary>
            <section className="ops-side-card ops-action-card">
              <div className="ops-inspector-title">
                <span>Agent</span>
                <strong>补充要求</strong>
              </div>
              {!hasWorkflowSession ? (
                <p className="ops-muted-line">未绑定可操作会话</p>
              ) : (
                <>
                  <textarea
                    value={agentNote}
                    onChange={(event) => setAgentNote(event.target.value)}
                    placeholder="写下你想调整、继续或询问的内容。"
                    rows={5}
                  />
                  <button
                    className="ink-button"
                    type="button"
                    onClick={() => void submitAgentNote()}
                    disabled={agentActionMutation.isPending || !agentNote.trim()}
                  >
                    {agentActionMutation.isPending ? '发送中...' : '发给 Agent'}
                  </button>
                  <small>
                    {activeWorkflowSessionId ? `会话 ${projectShortId(activeWorkflowSessionId)}` : '未绑定会话'}
                    {' · '}
                    当前产物 {selectedArtifact ? selectedArtifact.label : '未选中'}
                  </small>
                </>
              )}
              {workbenchNotice && <p className="ops-notice">{workbenchNotice}</p>}
              {agentLastReply && (
                <div className="ops-agent-reply">
                  <span>Agent 回复</span>
                  <p>{agentLastReply}</p>
                </div>
              )}
              {agentActionMutation.isError && <p className="ops-error">Agent 操作没有完成，请稍后重试。</p>}
            </section>

            <section className="ops-side-card ops-compact-card">
              <div className="ops-inspector-title">
                <span>当前调度</span>
                <strong>{scheduleText}</strong>
              </div>
              {selectedProjectTaskQueue.length === 0 ? (
                <p className="ops-muted-line">暂无调度任务</p>
              ) : (
                <>
                  <div className="ops-schedule-summary" aria-label="调度状态摘要">
                    <span><b>{scheduleCounts.running ?? 0}</b><small>运行</small></span>
                    <span><b>{scheduleCounts.queued ?? 0}</b><small>排队</small></span>
                    <span><b>{scheduleCounts.waiting ?? 0}</b><small>确认</small></span>
                    <span><b>{scheduleCounts.blocked ?? 0}</b><small>阻塞</small></span>
                  </div>
                  {primaryScheduledTask && (
                    <div
                      className={`ops-schedule-focus ${taskStatusGroup(primaryScheduledTask.status)}`}
                      title={[
                        taskChapterLabel(primaryScheduledTask.chapterId),
                        taskActionLabel(primaryScheduledTask),
                        primaryScheduledTask.blockedReason,
                      ].filter(Boolean).join(' · ')}
                    >
                      <span>当前焦点</span>
                      <strong>{taskFocusLabel(primaryScheduledTask)}</strong>
                      <em>{taskChapterLabel(primaryScheduledTask.chapterId)}</em>
                      <small>
                        {taskStatusLabel(primaryScheduledTask.status)}
                        {taskRiskLabel(primaryScheduledTask.risk) ? ` · ${taskRiskLabel(primaryScheduledTask.risk)}` : ''}
                      </small>
                      {primaryScheduledTask.blockedReason && <p>{primaryScheduledTask.blockedReason}</p>}
                    </div>
                  )}
                  {!primaryScheduledTask && queuedScheduledTasks.length > 0 && (
                    <div
                      className="ops-schedule-focus queued"
                      title={queuedScheduledTasks
                        .map((task) => `${taskChapterLabel(task.chapterId)} · ${taskActionLabel(task)}`)
                        .join('\n')}
                    >
                      <span>等待执行</span>
                      <strong>{queuedScheduledTasks.length} 个任务排队</strong>
                      <em>等待 Agent 选择下一步</em>
                      <small>暂无运行中的工具，等待 Agent 决定下一步。</small>
                    </div>
                  )}
                  {visibleScheduledTasks.length > 0 && (
                    <details className="ops-schedule-details">
                      <summary>
                        <span>队列详情</span>
                        <strong>{selectedProjectTaskQueue.length - (primaryScheduledTask ? 1 : 0)} 个待处理</strong>
                      </summary>
                      <div>
                        {visibleScheduledTasks.map((task) => (
                          <p key={task.taskId} title={`${taskChapterLabel(task.chapterId)} · ${taskActionLabel(task)}`}>
                            <span>{taskChapterLabel(task.chapterId)}</span>
                            <em>{taskActionLabel(task)}</em>
                            <small>{taskStatusLabel(task.status)}</small>
                          </p>
                        ))}
                        {selectedProjectTaskQueue.length > visibleScheduledTasks.length + (primaryScheduledTask ? 1 : 0) && (
                          <p className="ops-schedule-more">
                            还有 {selectedProjectTaskQueue.length - visibleScheduledTasks.length - (primaryScheduledTask ? 1 : 0)} 个任务未展开。
                          </p>
                        )}
                      </div>
                    </details>
                  )}
                </>
              )}
            </section>

            <section className="ops-side-card ops-compact-card">
              <div className="ops-inspector-title">
                <span>阻塞关注</span>
                <strong>{blockedArtifacts.length}</strong>
              </div>
              {blockedArtifacts.length === 0 ? (
                <p className="ops-muted-line">暂无阻塞产物</p>
              ) : blockedArtifacts.map((artifact, index) => (
                <button
                  key={artifactRenderKey(artifact, index)}
                  type="button"
                  className="ops-task-row blocked"
                  onClick={() => setSelectedArtifactId(artifact.id)}
                >
                  <strong>{artifact.title}</strong>
                  <small>{artifact.label} · {artifact.status}</small>
                  <em>{artifact.summary}</em>
                </button>
              ))}
            </section>

            <details className="ops-side-card ops-detail-fold" open={diagnosticReasons.length + diagnosticTaskCount > 0}>
              <summary>
                <span>诊断</span>
                <strong>{diagnosticReasons.length + diagnosticTaskCount}</strong>
              </summary>
              {diagnosticReasons.length + diagnosticTaskCount === 0 ? (
                <p className="ops-muted-line">无诊断信息</p>
              ) : (
                diagnosticReasons.slice(0, 5).map((reason) => (
                  <p key={reason}>{reason}</p>
                ))
              )}
            </details>

          </details>
        </section>
      </div>
    </>
  );
}
