import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  ApiError,
  cancelRuntimeRun,
  createAgentSession,
  createSseConnection,
  getSessionActiveRuntimeRun,
  listRuntimeEvents,
  listAgentSessions,
  resumeAgentSession,
  sendChat,
  updateAgentSession,
} from '../api';
import type {
  AgentChatResponse,
  AgentArtifactPreviewView,
  AgentConversationTurnView,
  AgentSessionResumeResponse,
  AgentSessionSummary,
  AgentRuntimeEventView,
  AgentSseEvent,
  AgentToolProgressView,
  NovelAgentRun,
  RuntimeRunDto,
} from '../api/types';
import { useAgentStore } from '../stores/useAgentStore';
import { useAppStore } from '../stores/useAppStore';
import { useChatStore } from '../stores/useChatStore';
import { useProjectStore } from '../stores/useProjectStore';
import '../styles/agent.css';

function formatSessionTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const now = new Date();
  const sameDay = date.toDateString() === now.toDateString();
  if (sameDay) return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  return date.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' });
}

function sessionPreview(session: AgentSessionSummary) {
  if (session.activeRunId) return `运行中 · ${session.activeRunId.slice(0, 8)}`;
  if (session.messageCount > 0) return `${session.messageCount} 条消息`;
  if (session.activeProjectId) return '已绑定项目';
  const phase = userVisibleMissionText(session.phase);
  return phase || '待开始';
}

function userVisibleMissionText(value?: string) {
  const text = value?.trim();
  if (!text) return '';
  const compact = text.toLowerCase();
  const hiddenValues = new Set([
    'idle',
    'continue',
    'unknown',
    'active',
    'queued',
    'running',
    'completed',
    'failed',
    'foundation_candidates',
    'foundation_ready',
    'foundation_committed',
  ]);
  if (hiddenValues.has(compact)) return '';
  if (compact.includes('_')) return '';
  if (/^[a-z][a-z0-9]*(?:[A-Z][A-Za-z0-9]*)+$/.test(text)) return '';
  if (text.includes('tool_search') || text.includes('PlanStoryFoundation')) return '';
  return text;
}

function isToolProgressView(value: unknown): value is AgentToolProgressView {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<AgentToolProgressView>;
  return typeof candidate.title === 'string' && typeof candidate.detail === 'string' && typeof candidate.status === 'string';
}

function isArtifactPreviewView(value: unknown): value is AgentArtifactPreviewView {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<AgentArtifactPreviewView>;
  return typeof candidate.title === 'string'
    && typeof candidate.summary === 'string'
    && typeof candidate.resultLocation === 'string'
    && Array.isArray(candidate.items);
}

function isNovelAgentRun(value: unknown): value is NovelAgentRun {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<NovelAgentRun>;
  return typeof candidate.runId === 'string' && Array.isArray(candidate.steps);
}

interface RuntimeEventView {
  id: string;
  type: string;
  title: string;
  detail: string;
  status: 'running' | 'done' | 'failed' | 'info';
  terminalState?: 'completed' | 'failed' | 'cancelled';
  runId?: string | null;
  sourceMessageId?: string | null;
  stage?: string;
  stageLabel?: string;
  productionKey?: string;
  productionRunId?: string;
  chapterId?: string;
  packageId?: string;
  chapterLabel?: string;
  timestamp: Date;
  repeatCount?: number;
}

interface ExecutionBlockView {
  id: string;
  runId?: string | null;
  anchorMessageId?: string;
  status: 'running' | 'done' | 'failed';
  title: string;
  summary: string;
  startedAt: Date;
  updatedAt: Date;
  events: RuntimeEventView[];
  previews: AgentArtifactPreviewView[];
  expanded: boolean;
}

interface StreamingReplyView {
  id: string;
  runId?: string | null;
  content: string;
  startedAt: Date;
  updatedAt: Date;
}

type ProductionStageTrackStatus = 'pending' | 'running' | 'done' | 'failed';

interface ProductionStageTrackItem {
  key: string;
  label: string;
  status: ProductionStageTrackStatus;
  detail: string;
  timestamp?: Date;
  repeatCount?: number;
}

interface ProductionStageTrackGroup {
  key: string;
  label: string;
  status: ProductionStageTrackStatus;
  summary: string;
  items: ProductionStageTrackItem[];
  updatedAt?: Date;
}

function runtimeRunIdFromEvent(evt: AgentSseEvent) {
  if (evt.runId) return evt.runId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

function runtimeRunIdFromRuntimeEvent(evt: AgentRuntimeEventView) {
  if (evt.runId) return evt.runId;
  const data = runtimeEventData(evt);
  if (!data || typeof data !== 'object') return null;
  const candidate = data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

function runtimeSourceMessageIdFromEvent(evt: AgentSseEvent) {
  if (evt.sourceMessageId) return evt.sourceMessageId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { sourceMessageId?: string; clientMessageId?: string };
  return candidate.sourceMessageId || candidate.clientMessageId || null;
}

function runtimeSourceMessageIdFromRuntimeEvent(evt: AgentRuntimeEventView) {
  if (evt.sourceMessageId) return evt.sourceMessageId;
  const data = runtimeEventData(evt);
  if (!data || typeof data !== 'object') return null;
  const candidate = data as { sourceMessageId?: string; clientMessageId?: string };
  return candidate.sourceMessageId || candidate.clientMessageId || null;
}

function runtimeEventData(evt: AgentRuntimeEventView) {
  if (evt.data !== undefined) return evt.data;
  if (!evt.dataJson) return undefined;
  try {
    return JSON.parse(evt.dataJson);
  } catch {
    return undefined;
  }
}

function runtimeEventTimestamp(evt: AgentRuntimeEventView) {
  return evt.timestamp || evt.createdAt || new Date().toISOString();
}

function runtimeEventToSseEvent(evt: AgentRuntimeEventView, sessionId: string): AgentSseEvent {
  return {
    eventId: evt.eventId,
    type: evt.type,
    sessionId,
    runId: evt.runId ?? undefined,
    sourceMessageId: evt.sourceMessageId ?? undefined,
    stage: evt.stage,
    status: evt.status,
    artifactType: evt.artifactType,
    artifactId: evt.artifactId,
    displaySurface: evt.displaySurface,
    displayPolicy: evt.displayPolicy,
    message: evt.message,
    data: runtimeEventData(evt),
    timestamp: runtimeEventTimestamp(evt),
  };
}

function eventField(data: unknown, field: string) {
  if (!data || typeof data !== 'object') return '';
  const value = (data as Record<string, unknown>)[field];
  return typeof value === 'string' ? value : '';
}

function eventValueText(data: unknown, field: string) {
  if (!data || typeof data !== 'object') return '';
  const value = (data as Record<string, unknown>)[field];
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  return '';
}

function eventStage(data: unknown, explicitStage?: string | null) {
  return explicitStage || eventField(data, 'stage') || eventField(data, 'phase');
}

function eventStatus(data: unknown, explicitStatus?: string | null) {
  return explicitStatus || eventField(data, 'status');
}

function productionStageLabel(value?: string | null) {
  const labels: Record<string, string> = {
    projectresolved: '确认项目',
    knowledgeresolved: '读取项目知识',
    knowledgeclassified: '知识语义分类',
    storydesignbuilt: '构建故事规则',
    volumeplanbuilt: '构建分卷设计',
    chapterblueprintbuilt: '构建章节蓝图',
    context_package: '构建章节上下文包',
    draft_generation: '生成章节正文',
    gate_validation: '校验章节草稿',
    gate_validation_or_repair: '校验/修复章节草稿',
    draft_repair: '修复章节草稿',
    quality_review: 'Agent 质量评审',
    await_user_review: '等待用户审阅',
    chapter_commit: '提交书城',
    packagebuilt: '构建章节生产包',
    draftgenerated: '生成章节正文',
    changesextracted: '抽取章节 CHANGES',
    gatevalidated: '校验章节草稿',
    draftrewritten: '修复章节草稿',
    reviewcompleted: 'Agent 质量评审',
    chaptercommitted: '提交书城',
    factspersisted: '沉淀连续性事实',
    indexupdated: '刷新生产索引',
    runcompleted: '本轮生产完成',
  };
  const key = value?.trim().toLowerCase();
  return key ? labels[key] ?? value!.trim() : '章节生产';
}

function productionStatusLabel(value?: string | null) {
  const labels: Record<string, string> = {
    queued: '排队中',
    running: '进行中',
    processing: '处理中',
    completed: '完成',
    succeeded: '完成',
    validated: '已通过',
    blocked: '已阻塞',
    failed: '失败',
    retryable_failed: '可重试失败',
    waiting_user: '等待确认',
  };
  const key = value?.trim().toLowerCase();
  return key ? labels[key] ?? value!.trim() : '';
}

const PRODUCTION_STAGE_ORDER: Array<{ key: string; label: string }> = [
  { key: 'packagebuilt', label: '生产包' },
  { key: 'draftgenerated', label: '正文' },
  { key: 'changesextracted', label: 'CHANGES' },
  { key: 'gatevalidated', label: '门禁' },
  { key: 'draftrewritten', label: '修复' },
  { key: 'reviewcompleted', label: '评审' },
  { key: 'chaptercommitted', label: '入书城' },
  { key: 'factspersisted', label: '事实' },
  { key: 'indexupdated', label: '索引' },
];

function normalizeProductionStageKey(value?: string | null) {
  const key = value?.trim().toLowerCase();
  if (!key) return '';
  const aliases: Record<string, string> = {
    context_package: 'packagebuilt',
    draft_generation: 'draftgenerated',
    gate_validation: 'gatevalidated',
    gate_validation_or_repair: 'gatevalidated',
    draft_repair: 'draftrewritten',
    quality_review: 'reviewcompleted',
    chapter_commit: 'chaptercommitted',
    packagebuilt: 'packagebuilt',
    draftgenerated: 'draftgenerated',
    changesextracted: 'changesextracted',
    gatevalidated: 'gatevalidated',
    draftrewritten: 'draftrewritten',
    reviewcompleted: 'reviewcompleted',
    chaptercommitted: 'chaptercommitted',
    factspersisted: 'factspersisted',
    indexupdated: 'indexupdated',
    runcompleted: 'indexupdated',
  };
  return aliases[key] ?? key;
}

function isProductionProgressEvent(event: RuntimeEventView) {
  return event.type === 'production_progress';
}

function productionEventMetadata(data: unknown) {
  const productionRunId = eventValueText(data, 'productionRunId')
    || eventValueText(data, 'novelRunId')
    || eventValueText(data, 'agentRunId');
  const chapterId = eventValueText(data, 'chapterId')
    || eventValueText(data, 'targetChapterId');
  const packageId = eventValueText(data, 'packageId')
    || eventValueText(data, 'contextPackageId');
  const chapterNumber = eventValueText(data, 'chapterNumber')
    || eventValueText(data, 'targetChapterNumber');
  const chapterTitle = eventValueText(data, 'chapterTitle')
    || eventValueText(data, 'title');
  const productionKey = productionRunId || chapterId || packageId || '';
  const chapterLabel = chapterTitle
    || (chapterNumber ? `第 ${chapterNumber} 章` : '')
    || chapterId
    || (productionRunId ? `生产链 ${productionRunId.slice(0, 8)}` : '');

  return {
    productionKey,
    productionRunId,
    chapterId,
    packageId,
    chapterLabel,
  };
}

function isDoneProductionStatus(status: RuntimeEventView['status'] | ProductionStageTrackStatus) {
  return status === 'done';
}

function isFailedProductionStatus(status: RuntimeEventView['status'] | ProductionStageTrackStatus) {
  return status === 'failed';
}

function productionStageStatusLabel(status: ProductionStageTrackStatus) {
  if (status === 'done') return '完成';
  if (status === 'running') return '进行中';
  if (status === 'failed') return '失败';
  return '等待';
}

function productionStageTrackItems(events: RuntimeEventView[]): ProductionStageTrackItem[] {
  const productionEvents = compactExecutionEvents(events.filter(isProductionProgressEvent));
  if (productionEvents.length === 0) return [];

  const latestByStage = new Map<string, RuntimeEventView>();
  productionEvents.forEach((event) => {
    const key = normalizeProductionStageKey(event.stage || event.stageLabel || event.title);
    if (!key) return;
    latestByStage.set(key, event);
  });

  const lastTouchedIndex = PRODUCTION_STAGE_ORDER.reduce((last, stage, index) => (
    latestByStage.has(stage.key) ? index : last
  ), -1);
  const lastDoneIndex = PRODUCTION_STAGE_ORDER.reduce((last, stage, index) => {
    const event = latestByStage.get(stage.key);
    return event && isDoneProductionStatus(event.status) ? index : last;
  }, -1);

  return PRODUCTION_STAGE_ORDER.map((stage, index) => {
    const event = latestByStage.get(stage.key);
    let status: ProductionStageTrackStatus = 'pending';
    if (event) {
      status = isFailedProductionStatus(event.status)
        ? 'failed'
        : isDoneProductionStatus(event.status)
          ? 'done'
          : 'running';
    } else if (index < lastDoneIndex || (lastTouchedIndex >= 0 && index < lastTouchedIndex)) {
      status = 'done';
    }

    return {
      key: stage.key,
      label: stage.label,
      status,
      detail: event?.detail || productionStageLabel(stage.key),
      timestamp: event?.timestamp,
      repeatCount: event?.repeatCount,
    };
  });
}

function productionGroupStatus(items: ProductionStageTrackItem[]): ProductionStageTrackStatus {
  if (items.some((item) => item.status === 'failed')) return 'failed';
  if (items.some((item) => item.key === 'indexupdated' && item.status === 'done')) return 'done';
  if (items.length > 0 && items.every((item) => item.status === 'done')) return 'done';
  return 'running';
}

function productionGroupSummary(items: ProductionStageTrackItem[]) {
  const failed = items.find((item) => item.status === 'failed');
  if (failed) return `停在：${failed.label}`;
  const running = items.find((item) => item.status === 'running');
  const doneCount = items.filter((item) => item.status === 'done').length;
  if (running) return `${running.label} · ${doneCount}/${items.length}`;
  if (items.some((item) => item.key === 'chaptercommitted' && item.status === 'done') && doneCount < items.length) {
    return `已入书城 · ${doneCount}/${items.length}`;
  }
  return `已完成 ${doneCount}/${items.length}`;
}

function productionStageTrackGroups(events: RuntimeEventView[]): ProductionStageTrackGroup[] {
  const productionEvents = compactExecutionEvents(events.filter(isProductionProgressEvent));
  if (productionEvents.length === 0) return [];

  const grouped = new Map<string, RuntimeEventView[]>();
  productionEvents.forEach((event) => {
    const key = event.productionKey || event.packageId || event.chapterId || event.productionRunId || 'default-production';
    const list = grouped.get(key) ?? [];
    list.push(event);
    grouped.set(key, list);
  });

  return Array.from(grouped.entries()).map(([key, groupEvents], index) => {
    const items = productionStageTrackItems(groupEvents);
    const latest = [...groupEvents].sort((left, right) => right.timestamp.getTime() - left.timestamp.getTime())[0];
    const label = latest?.chapterLabel || (grouped.size > 1 ? `章节生产 ${index + 1}` : '章节生产');
    const status = productionGroupStatus(items);
    return {
      key,
      label,
      status,
      summary: productionGroupSummary(items),
      items,
      updatedAt: latest?.timestamp,
    };
  }).sort((left, right) => (left.updatedAt?.getTime() ?? 0) - (right.updatedAt?.getTime() ?? 0));
}

function isProductionClosureComplete(events: RuntimeEventView[]) {
  const groups = productionStageTrackGroups(events);
  if (groups.length === 0) return false;
  return groups.every((group) => group.status === 'done');
}

function hasCommittedProductionChapter(events: RuntimeEventView[]) {
  return productionStageTrackGroups(events).some((group) => (
    group.items.some((item) => item.key === 'chaptercommitted' && item.status === 'done')
  ));
}

function productionExecutionSummary(events: RuntimeEventView[]) {
  const groups = productionStageTrackGroups(events);
  if (groups.length === 0) return '';
  const failed = groups.find((group) => group.status === 'failed');
  if (failed) return `${failed.label} 停在：${failed.summary}`;
  const running = groups.find((group) => group.status === 'running');
  const doneCount = groups.filter((group) => group.status === 'done').length;
  if (running) return `当前：${running.label} · ${running.summary} · ${doneCount}/${groups.length} 条链完成`;
  return `章节生产闭环已完成 · ${doneCount}/${groups.length} 条链`;
}

function runtimeEventStatus(type: string, data?: unknown, explicitStatus?: string | null): RuntimeEventView['status'] {
  const status = eventStatus(data, explicitStatus).toLowerCase();
  if (type === 'production_progress') {
    if (status === 'failed' || status === 'blocked' || status === 'retryable_failed') return 'failed';
    if (status === 'completed' || status === 'succeeded' || status === 'validated') return 'done';
    return 'running';
  }
  if (type === 'step_fail') return 'failed';
  if (type === 'step_complete') return 'done';
  if (type === 'run_update') return 'info';
  if (type === 'agent_observing' || type === 'agent_planning' || type === 'agent_acting' || type === 'agent_reflecting') {
    return 'running';
  }
  return 'info';
}

function isUsefulRuntimeEvent(type: string, message: string, data: unknown) {
  const compactMessage = message.trim().toLowerCase();
  if (compactMessage === 'idle' || compactMessage === 'continue') return false;
  if (type === 'mission_updated') return false;
  if (type === 'agent_reply') return false;
  if (type === 'agent_reflecting' && (compactMessage === 'idle' || compactMessage === 'continue')) return false;
  if (type === 'run_created' && isNovelAgentRun(data)) return false;
  return true;
}

function shouldDisplayRuntimeEvent(displaySurface?: string | null, displayPolicy?: string | null) {
  const surface = displaySurface?.trim().toLowerCase();
  const policy = displayPolicy?.trim().toLowerCase();
  if (surface === 'admin_debug') return false;
  if (policy === 'hidden' || policy === 'debug_only') return false;
  return true;
}

function runtimeEventTitle(type: string, message: string, data: unknown) {
  if (isToolProgressView(data)) {
    return data.title;
  }
  if (type === 'production_progress') {
    return `阶段：${productionStageLabel(eventStage(data))}`;
  }
  const labels: Record<string, string> = {
    agent_observing: '观察上下文',
    agent_planning: '模型决策',
    agent_acting: '执行工具',
    agent_reflecting: '整理结果',
    mission_updated: '任务状态更新',
    run_created: '运行产物',
    run_update: '运行状态更新',
    step_complete: '步骤完成',
    step_fail: '步骤失败',
    confirmation_required: '等待确认',
  };
  return labels[type] ?? (message || '运行事件');
}

function runtimeEventDetail(message: string, data: unknown) {
  if (isToolProgressView(data)) {
    return data.detail;
  }
  if (data && typeof data === 'object') {
    const candidate = data as { stage?: string; phase?: string; status?: string; artifactId?: string; error?: string; summary?: string };
    if (eventField(data, 'type') === 'production_progress') {
      const statusLabel = productionStatusLabel(candidate.status);
      const prefix = statusLabel ? `${statusLabel} · ` : '';
      const artifact = candidate.artifactId ? ` · 产物 ${candidate.artifactId}` : '';
      return `${prefix}${candidate.error || candidate.summary || message}${artifact}`;
    }
  }
  if (isNovelAgentRun(data)) {
    return '运行状态已更新。';
  }
  if (data && typeof data === 'object') {
    const candidate = data as { phase?: string; status?: string; runId?: string; error?: string; artifactType?: string; summary?: string };
    if (candidate.artifactType === 'tool_search_result') {
      return '已完成上下文准备，Agent 会继续选择下一步创作动作。';
    }
    return candidate.error || candidate.summary || message;
  }
  return message;
}

function toRuntimeEventView(
  type: string,
  message: string,
  data: unknown,
  timestamp: string | Date | undefined,
  runId?: string | null,
  sourceMessageId?: string | null,
  stage?: string | null,
  status?: string | null,
): RuntimeEventView | null {
  if (!isUsefulRuntimeEvent(type, message || '', data)) return null;
  const normalizedStage = eventStage(data, stage);
  const normalizedStatus = eventStatus(data, status);
  const dataWithEventFields = type === 'production_progress'
    ? {
      ...(data && typeof data === 'object' ? data : {}),
      type,
      stage: normalizedStage,
      status: normalizedStatus,
    }
    : data;
  const productionMeta = type === 'production_progress'
    ? productionEventMetadata(dataWithEventFields)
    : null;
  const terminalState = runtimeEventTerminalState(dataWithEventFields, message);
  return {
    id: `${type}-${timestamp ? new Date(timestamp).getTime() : Date.now()}-${Math.random().toString(16).slice(2)}`,
    type,
    title: runtimeEventTitle(type, message, dataWithEventFields),
    detail: runtimeEventDetail(message, dataWithEventFields),
    status: terminalState
      ? (terminalState === 'failed' ? 'failed' : 'done')
      : runtimeEventStatus(type, dataWithEventFields, normalizedStatus),
    terminalState,
    runId,
    sourceMessageId,
    stage: normalizedStage,
    stageLabel: type === 'production_progress' ? productionStageLabel(normalizedStage) : undefined,
    productionKey: productionMeta?.productionKey || undefined,
    productionRunId: productionMeta?.productionRunId || undefined,
    chapterId: productionMeta?.chapterId || undefined,
    packageId: productionMeta?.packageId || undefined,
    chapterLabel: productionMeta?.chapterLabel || undefined,
    timestamp: timestamp ? new Date(timestamp) : new Date(),
  };
}

function terminalRuntimeStateFromMessage(message?: string | null): RuntimeEventView['terminalState'] {
  const text = message?.trim() || '';
  if (!text) return undefined;
  if (text.includes('后台执行已取消') || text.includes('运行已取消')) return 'cancelled';
  if (text.includes('后台执行已完成') || text.includes('本轮执行已完成')) return 'completed';
  if (text.includes('后台执行失败') || text.includes('心跳超时') || text.includes('可恢复失败')) return 'failed';
  return undefined;
}

function isTerminalRuntimeMessage(message?: string | null) {
  return Boolean(terminalRuntimeStateFromMessage(message));
}

function isTerminalRuntimeUpdate(data: unknown, message?: string | null) {
  if (!data || typeof data !== 'object') return isTerminalRuntimeMessage(message);
  const candidate = data as { status?: string; phase?: string };
  const status = candidate.status?.toLowerCase();
  const phase = candidate.phase?.toLowerCase();
  return status === 'completed'
    || status === 'failed'
    || status === 'cancelled'
    || phase === 'completed'
    || phase === 'failed'
    || phase === 'cancelled'
    || isTerminalRuntimeMessage(message);
}

function runtimeEventTerminalState(data: unknown, message?: string | null): RuntimeEventView['terminalState'] {
  if (!data || typeof data !== 'object') return terminalRuntimeStateFromMessage(message);
  const candidate = data as { status?: string; phase?: string };
  const status = candidate.status?.toLowerCase();
  const phase = candidate.phase?.toLowerCase();
  if (status === 'cancelled' || phase === 'cancelled') return 'cancelled';
  if (status === 'failed' || phase === 'failed') return 'failed';
  if (status === 'completed' || phase === 'completed') return 'completed';
  return terminalRuntimeStateFromMessage(message);
}

function runtimeRunStatusLabel(run: RuntimeRunDto) {
  if (run.cancelRequested) return '正在取消';
  const status = run.status?.toLowerCase();
  if (status === 'queued') return '排队中';
  if (status === 'running') return '后台执行中';
  if (status === 'completed') return '后台执行完成';
  if (status === 'failed') return '后台执行失败';
  if (status === 'cancelled') return '后台执行已取消';
  return '后台运行状态';
}

function runtimeRunToEvent(run: RuntimeRunDto, heartbeatAt?: string | null): RuntimeEventView {
  const status = run.status?.toLowerCase();
  const failed = status === 'failed';
  const done = status === 'completed' || status === 'cancelled';
  const phase = run.currentPhase || run.status;
  const tool = run.activeTool ? `${run.activeTool} · ` : '';
  const detail = run.lastMessage || `${tool}${phase}`;

  return {
    id: `active-run-${run.runId}-${run.updatedAt}-${heartbeatAt || ''}-${run.cancelRequested ? 'cancel' : 'live'}`,
    type: 'run_update',
    title: runtimeRunStatusLabel(run),
    detail,
    status: failed ? 'failed' : done ? 'done' : 'running',
    terminalState: status === 'cancelled' ? 'cancelled' : failed ? 'failed' : status === 'completed' ? 'completed' : undefined,
    runId: run.runId,
    sourceMessageId: run.sourceMessageId,
    stage: phase,
    timestamp: heartbeatAt ? new Date(heartbeatAt) : new Date(run.updatedAt || Date.now()),
  };
}

function makeExecutionBlock(
  id: string,
  runId?: string | null,
  title = '正在执行',
  anchorMessageId?: string,
) : ExecutionBlockView {
  const now = new Date();
  return {
    id,
    runId: runId ?? null,
    anchorMessageId,
    status: 'running',
    title,
    summary: 'Agent 已收到消息，正在判断下一步动作。',
    startedAt: now,
    updatedAt: now,
    events: [],
    previews: [],
    expanded: false,
  };
}

function executionStatusFromEvent(event: RuntimeEventView): ExecutionBlockView['status'] {
  if (event.status === 'failed') return 'failed';
  if (event.type === 'agent_reply') return 'done';
  if (event.type === 'run_update' && event.status === 'done') return 'done';
  return 'running';
}

function hasCancelledRuntimeUpdate(events: RuntimeEventView[]) {
  return events.some((event) => event.type === 'run_update' && event.terminalState === 'cancelled');
}

function resolveExecutionBlockStatus(
  events: RuntimeEventView[],
  incomingStatus: ExecutionBlockView['status'],
  currentStatus: ExecutionBlockView['status'] = 'running',
): ExecutionBlockView['status'] {
  if (incomingStatus === 'failed') return 'failed';
  if (hasCancelledRuntimeUpdate(events)) return 'done';

  if (events.some(isProductionProgressEvent)) {
    if (productionStageTrackItems(events).some((item) => item.status === 'failed')) return 'failed';
    return isProductionClosureComplete(events) ? 'done' : 'running';
  }

  if (currentStatus === 'failed' && incomingStatus === 'running') return 'failed';
  if (incomingStatus === 'done') return 'done';
  if (incomingStatus === 'running') return 'running';
  return currentStatus;
}

function executionBlockTitle(status: ExecutionBlockView['status'], eventCount: number) {
  if (status === 'failed') return '执行遇到问题';
  if (status === 'done') return eventCount > 0 ? '已处理' : '已完成';
  return eventCount > 0 ? '正在处理' : '正在处理';
}

function executionBlockTitleFromEvents(status: ExecutionBlockView['status'], events: RuntimeEventView[]) {
  if (hasCancelledRuntimeUpdate(events)) return '后台执行已取消';
  if (events.some(isProductionProgressEvent)) {
    if (status === 'failed' || events.some((event) => event.type === 'production_progress' && event.status === 'failed')) {
      return '章节生产闭环遇到问题';
    }
    if (isProductionClosureComplete(events)) return '章节生产闭环已完成';
    if (hasCommittedProductionChapter(events)) return '章节已入书城，后台收尾中';
    if (status === 'done') return '章节生产阶段已完成';
    return '章节生产闭环进行中';
  }
  return executionBlockTitle(status, events.length);
}

function executionBlockOutputLabel(block: ExecutionBlockView) {
  if (block.events.some((event) => event.type === 'confirmation_required')) return '需要确认';
  if (block.status === 'failed') return '需要处理';
  if (block.events.some(isProductionProgressEvent)) {
    return block.events.some((event) => event.type === 'production_progress' && normalizeProductionStageKey(event.stage) === 'chaptercommitted' && event.status === 'done')
      ? '已入书城'
      : '生产闭环';
  }
  if (block.events.some((event) => event.type === 'agent_reply')) return '会回复你';
  if (block.previews.length > 0 || block.events.some((event) => event.type === 'production_progress')) return '后台推进';
  if (block.events.some((event) => event.type === 'run_update')) return '后台推进';
  return '后台推进';
}

function executionBlockDestination(block: ExecutionBlockView) {
  const previewLocation = block.previews.find((preview) => preview.resultLocation)?.resultLocation;
  if (previewLocation) return previewLocation;
  if (block.events.some((event) => event.type === 'production_progress' && normalizeProductionStageKey(event.stage) === 'chaptercommitted')) return '书城';
  if (block.events.some((event) => event.type === 'production_progress')) return '工作流';
  if (block.events.some((event) => event.type === 'agent_reply')) return '聊天回复';
  if (block.events.some((event) => event.type === 'confirmation_required')) return '等待你确认';
  return '工作流';
}

function formatExecutionDuration(startedAt: Date, updatedAt: Date | number) {
  const end = typeof updatedAt === 'number' ? updatedAt : updatedAt.getTime();
  const ms = Math.max(0, end - startedAt.getTime());
  const seconds = Math.max(1, Math.round(ms / 1000));
  if (seconds < 60) return `${seconds} 秒`;
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest > 0 ? `${minutes} 分 ${rest} 秒` : `${minutes} 分`;
}

function chatItemRenderKey(itemId: string, index: number) {
  return `chat-item-${itemId}-${index}`;
}

function executionEventCompactKey(event: RuntimeEventView) {
  if (event.type === 'production_progress') {
    return `${event.type}:${event.productionKey || event.packageId || event.chapterId || event.productionRunId || 'default'}:${event.stage || event.stageLabel || event.title}:${event.status}`;
  }
  if (event.type === 'agent_acting' && event.stage) {
    return `${event.type}:${event.stage}:${event.status}`;
  }
  if (event.type === 'agent_acting' && event.title) {
    return `${event.type}:${event.title}:${event.status}`;
  }
  return event.id;
}

function compactExecutionEvents(events: RuntimeEventView[]) {
  const compacted: RuntimeEventView[] = [];
  const indexByKey = new Map<string, number>();

  events.forEach((event) => {
    const key = executionEventCompactKey(event);
    const existingIndex = indexByKey.get(key);
    if (existingIndex === undefined) {
      indexByKey.set(key, compacted.length);
      compacted.push({ ...event, repeatCount: 1 });
      return;
    }

    const existing = compacted[existingIndex];
    compacted[existingIndex] = {
      ...existing,
      ...event,
      repeatCount: (existing.repeatCount ?? 1) + 1,
    };
  });

  return compacted;
}

function mergeExecutionBlockEvents(events: RuntimeEventView[], event: RuntimeEventView) {
  const merged = [...events, event];
  if (!merged.some(isProductionProgressEvent)) {
    return merged.slice(-24);
  }

  const productionEvents = compactExecutionEvents(merged.filter(isProductionProgressEvent));
  const supportingEvents = compactExecutionEvents(
    merged
      .filter((item) => !isProductionProgressEvent(item))
      .slice(-16),
  );

  return [...supportingEvents, ...productionEvents]
    .sort((left, right) => left.timestamp.getTime() - right.timestamp.getTime())
    .slice(-48);
}

function isProductionToolProgressEvent(event: RuntimeEventView) {
  if (event.type !== 'agent_acting') return false;
  const text = `${event.title} ${event.detail}`;
  return /章节生产闭环|章节生产包|章节草稿|章节校验|章节修复|章节评审|章节提交|写入书城/.test(text);
}

function shouldMergeExecutionBlockByAnchor(block: ExecutionBlockView, event: RuntimeEventView) {
  if (block.status !== 'running') return false;
  if (!block.runId) return true;
  if (isProductionToolProgressEvent(event)) return true;
  if (event.type === 'production_progress' && block.events.some(isProductionToolProgressEvent)) return true;
  if (event.type === 'run_update' && block.events.some(isProductionToolProgressEvent)) return true;
  return false;
}

function executionVisibleEvents(block: ExecutionBlockView) {
  const hasProductionTrack = block.events.some(isProductionProgressEvent);
  return compactExecutionEvents(block.events.filter((event) => {
    if (event.type === 'production_progress') return false;
    if (hasProductionTrack && event.type === 'run_update') return false;
    if (hasProductionTrack && isProductionToolProgressEvent(event)) return false;
    return true;
  }));
}

function latestUserMessageIdFromTurns(turns?: AgentConversationTurnView[] | null) {
  if (!turns || turns.length === 0) return undefined;
  for (let index = turns.length - 1; index >= 0; index -= 1) {
    if (turns[index].role === 'user') {
      return turns[index].turnId?.trim()
        || `${turns[index].role}-${turns[index].turnIndex ?? index}-${turns[index].createdAt}`;
    }
  }
  return undefined;
}

function buildExecutionBlocksFromRuntimeEvents(
  events: AgentRuntimeEventView[],
  fallbackAnchorMessageId?: string,
): ExecutionBlockView[] {
  const blocks: ExecutionBlockView[] = [];

  events.forEach((evt, index) => {
    const runId = runtimeRunIdFromRuntimeEvent(evt);
    const sourceMessageId = runtimeSourceMessageIdFromRuntimeEvent(evt);
    const data = runtimeEventData(evt);
    const timestamp = runtimeEventTimestamp(evt);
    if (!shouldDisplayRuntimeEvent(evt.displaySurface, evt.displayPolicy)) return;
    if (evt.type === 'artifact_preview' && isArtifactPreviewView(data)) {
      const blockId = runId ? `run-${runId}` : `resume-${index}`;
      let block = blocks.find((item) => item.id === blockId || (runId && item.runId === runId));
      if (!block) {
        block = makeExecutionBlock(blockId, runId, '最近执行产物', sourceMessageId || (!runId ? fallbackAnchorMessageId : undefined));
        block.startedAt = new Date(timestamp);
        blocks.push(block);
      }
      block.previews = [data as AgentArtifactPreviewView, ...block.previews].slice(0, 3);
      block.summary = (data as AgentArtifactPreviewView).summary;
      block.updatedAt = new Date(timestamp);
      return;
    }

    const item = toRuntimeEventView(evt.type, evt.message, data, timestamp, runId, sourceMessageId, evt.stage, evt.status);
    if (!item) return;
    const blockId = runId ? `run-${runId}` : `resume-${index}`;
    let block = blocks.find((entry) => entry.id === blockId || (runId && entry.runId === runId));
    if (!block) {
      block = makeExecutionBlock(blockId, runId, '最近执行', sourceMessageId || (!runId ? fallbackAnchorMessageId : undefined));
      block.startedAt = item.timestamp;
      blocks.push(block);
    }
    const status = executionStatusFromEvent(item);
    block.events = mergeExecutionBlockEvents(block.events, item);
    block.status = resolveExecutionBlockStatus(block.events, status, block.status);
    block.title = executionBlockTitleFromEvents(block.status, block.events);
    block.summary = item.detail || item.title;
    block.updatedAt = item.timestamp;
  });

  return blocks.slice(-12);
}

function mergeResumeMemory(resume: AgentSessionResumeResponse) {
  return {
    ...resume.memory,
    missionPlan: resume.memory?.missionPlan ?? resume.missionPlan,
    pendingConfirmation: resume.memory?.pendingConfirmation ?? resume.pendingConfirmation ?? null,
  };
}

function isMissingSessionError(err: unknown) {
  return err instanceof ApiError && err.status === 404;
}

export default function AgentPage() {
  const queryClient = useQueryClient();
  const [input, setInput] = useState('');
  const [sessions, setSessions] = useState<AgentSessionSummary[]>([]);
  const [sessionsLoaded, setSessionsLoaded] = useState(false);
  const [sessionLoadError, setSessionLoadError] = useState('');
  const [sessionMenu, setSessionMenu] = useState<{
    session: AgentSessionSummary;
    x: number;
    y: number;
  } | null>(null);
  const [activeRuntimeRunId, setActiveRuntimeRunId] = useState<string | null>(null);
  const [cancellingRuntimeRunId, setCancellingRuntimeRunId] = useState<string | null>(null);
  const [executionBlocks, setExecutionBlocks] = useState<ExecutionBlockView[]>([]);
  const [streamingReply, setStreamingReply] = useState<StreamingReplyView | null>(null);
  const [clockNow, setClockNow] = useState(() => Date.now());
  const inFlightCountRef = useRef(0);
  const chatThreadRef = useRef<HTMLDivElement>(null);
  const chatEndRef = useRef<HTMLDivElement>(null);
  const chatFormRef = useRef<HTMLFormElement>(null);
  const lastResumedSessionIdRef = useRef('');
  const seenRuntimeEventIdsRef = useRef<Record<string, Set<string>>>({});
  const lastRuntimeEventIdRef = useRef<Record<string, string>>({});
  const runAnchorMessageIdsRef = useRef<Record<string, string>>({});
  const latestTurnAnchorMessageIdRef = useRef<string>('');

  const {
    messages,
    isSending,
    addUserMessage,
    addAgentMessage,
    loadSessionMessages,
    setCurrentSessionMessages,
    setSending,
  } = useChatStore();
  const addLog = useAppStore((s) => s.addLog);
  const setCurrentProjectId = useProjectStore((s) => s.setCurrentProjectId);
  const {
    sessionId,
    resumeState,
    setSessionId,
    setActiveRun,
    addSseEvent,
    setConnected,
    setResumeState,
    clearEvents,
    clearResumeState,
  } = useAgentStore();

  const reloadSessions = useCallback(async () => {
    try {
      setSessionLoadError('');
      const next = await listAgentSessions();
      setSessions(Array.isArray(next) ? next : []);
      setSessionsLoaded(true);
      return Array.isArray(next) ? next : [];
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '会话历史加载失败');
      setSessionsLoaded(true);
      return [];
    }
  }, []);

  const invalidateAgentState = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
    queryClient.invalidateQueries({ queryKey: ['projects'] });
    queryClient.invalidateQueries({ queryKey: ['novelLibrary'] });
    queryClient.invalidateQueries({ queryKey: ['storyBible'] });
    queryClient.invalidateQueries({ queryKey: ['runs'] });
  }, [queryClient]);

  const rememberRuntimeEventId = useCallback((targetSessionId: string, eventId?: string | null) => {
    if (!eventId) return true;
    const seen = seenRuntimeEventIdsRef.current[targetSessionId] ?? new Set<string>();
    seenRuntimeEventIdsRef.current[targetSessionId] = seen;
    if (seen.has(eventId)) return false;
    seen.add(eventId);
    lastRuntimeEventIdRef.current[targetSessionId] = eventId;
    return true;
  }, []);

  const resetRuntimeEventReplayState = useCallback((targetSessionId: string, events: AgentRuntimeEventView[] = []) => {
    const seen = new Set<string>();
    let lastEventId = '';
    events.forEach((evt) => {
      if (!evt.eventId) return;
      seen.add(evt.eventId);
      lastEventId = evt.eventId;
    });
    seenRuntimeEventIdsRef.current[targetSessionId] = seen;
    if (lastEventId) {
      lastRuntimeEventIdRef.current[targetSessionId] = lastEventId;
    } else {
      delete lastRuntimeEventIdRef.current[targetSessionId];
    }
  }, []);

  const resolveExecutionAnchor = useCallback((runId?: string | null, anchorMessageId?: string, sourceMessageId?: string | null) => {
    const mapped = runId ? runAnchorMessageIdsRef.current[runId] : '';
    const source = sourceMessageId?.trim() || '';
    const resolved = mapped || source || anchorMessageId || (!runId ? latestTurnAnchorMessageIdRef.current : '') || undefined;
    if (runId && resolved) {
      runAnchorMessageIdsRef.current[runId] = resolved;
    }
    return resolved;
  }, []);

  const appendExecutionEvent = useCallback((event: RuntimeEventView, anchorMessageId?: string) => {
    const resolvedAnchorMessageId = resolveExecutionAnchor(event.runId, anchorMessageId, event.sourceMessageId);
    setExecutionBlocks((prev) => {
      const now = event.timestamp;
      const runIndex = event.runId ? prev.findIndex((block) => block.runId === event.runId) : -1;
      const targetIndex = runIndex >= 0
        ? runIndex
        : resolvedAnchorMessageId
          ? prev.findIndex((block) => block.anchorMessageId === resolvedAnchorMessageId && shouldMergeExecutionBlockByAnchor(block, event))
          : prev.findIndex((block) => block.status === 'running' && !block.runId);
      const next = [...prev];
      const status = executionStatusFromEvent(event);

      if (targetIndex >= 0) {
        const current = next[targetIndex];
        if (current.events.some((entry) => entry.id === event.id)) return prev;
        const events = mergeExecutionBlockEvents(current.events, event);
        const nextStatus = resolveExecutionBlockStatus(events, status, current.status);
        next[targetIndex] = {
          ...current,
          runId: current.runId ?? event.runId ?? null,
          anchorMessageId: current.anchorMessageId ?? resolvedAnchorMessageId,
          status: nextStatus,
          title: executionBlockTitleFromEvents(nextStatus, events),
          summary: event.detail || event.title,
          updatedAt: now,
          events,
        };
        return next.slice(-24);
      }

      const block = makeExecutionBlock(
        event.runId ? `run-${event.runId}` : `event-${event.id}`,
        event.runId,
        executionBlockTitleFromEvents(status, [event]),
        resolvedAnchorMessageId,
      );
      block.status = resolveExecutionBlockStatus([event], status);
      block.summary = event.detail || event.title;
      block.startedAt = now;
      block.updatedAt = now;
      block.events = [event];
      return [...next, block].slice(-24);
    });
  }, [resolveExecutionAnchor]);

  const appendExecutionPreview = useCallback((preview: AgentArtifactPreviewView, runId?: string | null, anchorMessageId?: string) => {
    const previewRunId = runId || preview.runId || null;
    const resolvedAnchorMessageId = resolveExecutionAnchor(previewRunId, anchorMessageId);
    setExecutionBlocks((prev) => {
      const runIndex = previewRunId ? prev.findIndex((block) => block.runId === previewRunId) : -1;
      const targetIndex = runIndex >= 0
        ? runIndex
        : resolvedAnchorMessageId
          ? prev.findIndex((block) => block.status === 'running' && !block.runId && block.anchorMessageId === resolvedAnchorMessageId)
          : prev.findIndex((block) => block.status === 'running' && !block.runId);
      const next = [...prev];

      if (targetIndex >= 0) {
        const current = next[targetIndex];
        next[targetIndex] = {
          ...current,
          runId: current.runId ?? previewRunId,
          anchorMessageId: current.anchorMessageId ?? resolvedAnchorMessageId,
          status: current.status === 'failed' ? 'failed' : current.status,
          title: current.status === 'done' ? current.title : executionBlockTitle(current.status, current.events.length),
          summary: preview.summary || current.summary,
          updatedAt: new Date(preview.createdAt || Date.now()),
          previews: [preview, ...current.previews].slice(0, 3),
        };
        return next.slice(-24);
      }

      const block = makeExecutionBlock(
        previewRunId ? `run-${previewRunId}` : `preview-${Date.now()}`,
        previewRunId,
        '收到阶段性产物',
        resolvedAnchorMessageId,
      );
      block.summary = preview.summary;
      block.startedAt = new Date(preview.createdAt || Date.now());
      block.updatedAt = block.startedAt;
      block.previews = [preview];
      return [...next, block].slice(-24);
    });
  }, [resolveExecutionAnchor]);

  const markExecutionBlockDone = useCallback((runId?: string | null, failed = false) => {
    setExecutionBlocks((prev) => {
      const targetIndex = runId
        ? prev.findIndex((block) => block.runId === runId)
        : [...prev].reverse().findIndex((block) => block.status === 'running');
      if (targetIndex < 0) return prev;
      const realIndex = runId ? targetIndex : prev.length - 1 - targetIndex;
      return prev.map((block, index) => {
        if (index !== realIndex) return block;
        const status = failed ? 'failed' : resolveExecutionBlockStatus(block.events, 'done', block.status);
        return {
          ...block,
          status,
          title: executionBlockTitleFromEvents(status, block.events),
          updatedAt: new Date(),
        };
      });
    });
  }, []);

  const toggleExecutionBlock = useCallback((id: string) => {
    setExecutionBlocks((prev) => prev.map((block) => (
      block.id === id ? { ...block, expanded: !block.expanded } : block
    )));
  }, []);

  const refreshActiveRuntimeRun = useCallback(async (id: string) => {
    try {
      const state = await getSessionActiveRuntimeRun(id);
      if (!state.hasActiveRun || !state.run) {
        if (activeRuntimeRunId) {
          setActiveRuntimeRunId(null);
          setCancellingRuntimeRunId(null);
          inFlightCountRef.current = 0;
          setSending(false);
        }
        return;
      }

      const run = state.run;
      setActiveRuntimeRunId(run.runId);
      setCancellingRuntimeRunId(run.cancelRequested ? run.runId : null);
      appendExecutionEvent(runtimeRunToEvent(run, state.heartbeatAt));
      const status = run.status.toLowerCase();
      if (status === 'completed' || status === 'failed' || status === 'cancelled') {
        markExecutionBlockDone(run.runId, status === 'failed');
        setActiveRuntimeRunId(null);
        setCancellingRuntimeRunId(null);
        inFlightCountRef.current = 0;
        setSending(false);
        void reloadSessions();
      } else {
        setSending(true);
      }
    } catch {
      // SSE remains the primary live channel; polling failures should not break chat input.
    }
  }, [activeRuntimeRunId, appendExecutionEvent, markExecutionBlockDone, reloadSessions, setSending]);

  const applySessionResume = useCallback((detail: AgentSessionResumeResponse) => {
    const memory = mergeResumeMemory(detail);
    lastResumedSessionIdRef.current = detail.sessionId;
    setSessionId(detail.sessionId);
    setCurrentProjectId(detail.activeProjectId || null);
    setActiveRun(null);
    setActiveRuntimeRunId(detail.activeRunId ?? null);
    setCancellingRuntimeRunId(null);
    clearEvents();
    const recentRuntimeEvents = detail.recentRuntimeEvents ?? [];
    resetRuntimeEventReplayState(detail.sessionId, recentRuntimeEvents);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
    setExecutionBlocks(buildExecutionBlocksFromRuntimeEvents(recentRuntimeEvents, latestTurnAnchorMessageIdRef.current || undefined));
    setStreamingReply(null);
    setResumeState({
      pendingToolCall: detail.pendingToolCall ?? null,
      pendingConfirmation: detail.pendingConfirmation ?? memory.pendingConfirmation ?? null,
      discoveredPhase: detail.discoveredPhase ?? null,
      discoveredTools: detail.discoveredTools ?? [],
      toolSearchCacheVersion: detail.toolSearchCacheVersion ?? null,
      lastToolSearchAt: detail.lastToolSearchAt ?? null,
      toolSearchCacheFresh: detail.toolSearchCacheFresh,
      toolSearchCacheSource: detail.toolSearchCacheSource,
      recentToolExecutions: detail.recentToolExecutions ?? [],
    });
    loadSessionMessages(detail.sessionId, detail.messages, memory);
  }, [clearEvents, loadSessionMessages, resetRuntimeEventReplayState, setActiveRun, setCurrentProjectId, setResumeState, setSessionId]);

  const clearMissingSession = useCallback((id: string) => {
    if (lastResumedSessionIdRef.current === id) {
      lastResumedSessionIdRef.current = '';
    }
    setSessionLoadError('');
    setSessionId('');
    setActiveRun(null);
    setActiveRuntimeRunId(null);
    setCancellingRuntimeRunId(null);
    resetRuntimeEventReplayState(id, []);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = '';
    clearResumeState();
    clearEvents();
    setExecutionBlocks([]);
    setStreamingReply(null);
    setCurrentSessionMessages('');
  }, [clearEvents, clearResumeState, resetRuntimeEventReplayState, setActiveRun, setCurrentSessionMessages, setSessionId]);

  const resumeCurrentSession = useCallback(async (id: string) => {
    try {
      setSessionLoadError('');
      const detail = await resumeAgentSession(id);
      applySessionResume(detail);
    } catch (err) {
      if (isMissingSessionError(err)) {
        clearMissingSession(id);
        return;
      }
      lastResumedSessionIdRef.current = '';
      setSessionLoadError(err instanceof Error ? err.message : '会话恢复失败');
    }
  }, [applySessionResume, clearMissingSession]);

  const selectSession = useCallback(async (id: string) => {
    try {
      setSessionLoadError('');
      const detail = await resumeAgentSession(id);
      applySessionResume(detail);
    } catch (err) {
      if (isMissingSessionError(err)) {
        clearMissingSession(id);
        void reloadSessions();
        return;
      }
      setSessionLoadError(err instanceof Error ? err.message : '会话详情加载失败');
    }
  }, [applySessionResume, clearMissingSession, reloadSessions]);

  const createNewSession = useCallback(async () => {
    try {
      setSessionLoadError('');
      setSessionMenu(null);
      const detail = await createAgentSession();
      lastResumedSessionIdRef.current = detail.sessionId;
      setSessionId(detail.sessionId);
      setCurrentProjectId(detail.activeProjectId || null);
      setActiveRun(null);
      setActiveRuntimeRunId(detail.activeRunId ?? null);
      setCancellingRuntimeRunId(null);
      resetRuntimeEventReplayState(detail.sessionId, []);
      runAnchorMessageIdsRef.current = {};
      latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
      clearResumeState();
      clearEvents();
      setExecutionBlocks([]);
      setStreamingReply(null);
      loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '新建会话失败');
    }
  }, [clearEvents, clearResumeState, loadSessionMessages, reloadSessions, resetRuntimeEventReplayState, setActiveRun, setSessionId]);

  const renameSession = useCallback(async (target: AgentSessionSummary) => {
    setSessionMenu(null);
    const title = window.prompt('重命名会话', target.title || '新会话')?.trim();
    if (!title || title === target.title) return;

    try {
      setSessionLoadError('');
      await updateAgentSession(target.sessionId, { title });
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '重命名失败');
    }
  }, [reloadSessions]);

  const archiveSession = useCallback(async (target: AgentSessionSummary) => {
    setSessionMenu(null);
    try {
      setSessionLoadError('');
      await updateAgentSession(target.sessionId, { isArchived: true });
      const next = await reloadSessions();
      if (target.sessionId === sessionId) {
        const fallback = next.find((item) => item.sessionId !== target.sessionId);
        if (fallback) {
          await selectSession(fallback.sessionId);
        } else {
          await createNewSession();
        }
      }
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '归档失败');
    }
  }, [createNewSession, reloadSessions, selectSession, sessionId]);

  useEffect(() => {
    void reloadSessions();
  }, [reloadSessions]);

  useEffect(() => {
    if (!sessionId) return;
    if (!sessionsLoaded) return;
    if (lastResumedSessionIdRef.current === sessionId) return;
    if (!sessions.some((item) => item.sessionId === sessionId)) {
      clearMissingSession(sessionId);
      return;
    }
    lastResumedSessionIdRef.current = sessionId;
    void resumeCurrentSession(sessionId);
  }, [clearMissingSession, resumeCurrentSession, sessionId, sessions, sessionsLoaded]);

  useEffect(() => {
    if (!sessionMenu) return;
    const close = () => setSessionMenu(null);
    window.addEventListener('click', close);
    window.addEventListener('keydown', close);
    window.addEventListener('resize', close);
    return () => {
      window.removeEventListener('click', close);
      window.removeEventListener('keydown', close);
      window.removeEventListener('resize', close);
    };
  }, [sessionMenu]);

  useEffect(() => {
    if (sessionId) return;
    if (!sessionsLoaded) return;
    if (sessions.length > 0) {
      void selectSession(sessions[0].sessionId);
      return;
    }
    void createNewSession();
  }, [createNewSession, selectSession, sessionId, sessions, sessionsLoaded]);

  useEffect(() => {
    if (!sessionId) return;
    setCurrentSessionMessages(sessionId);
  }, [sessionId, setCurrentSessionMessages]);

  useEffect(() => {
    if (!sessionId) return;
    let disposed = false;

    const poll = async () => {
      if (disposed) return;
      await refreshActiveRuntimeRun(sessionId);
    };

    void poll();
    const timer = window.setInterval(() => {
      void poll();
    }, 6000);

    return () => {
      disposed = true;
      window.clearInterval(timer);
    };
  }, [refreshActiveRuntimeRun, sessionId]);

  useEffect(() => {
    const hasRunningBlock = executionBlocks.some((block) => block.status === 'running');
    if (!hasRunningBlock && !streamingReply) return;
    const timer = window.setInterval(() => setClockNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [executionBlocks, streamingReply]);

  const handleSseEvent = useCallback((evt: AgentSseEvent) => {
    if (!sessionId || !rememberRuntimeEventId(sessionId, evt.eventId)) {
      return;
    }
    const eventRunId = runtimeRunIdFromEvent(evt);
    const eventSourceMessageId = runtimeSourceMessageIdFromEvent(evt);
    if (eventRunId && eventSourceMessageId) {
      runAnchorMessageIdsRef.current[eventRunId] = eventSourceMessageId;
    }
    const effectiveRunId = eventRunId || activeRuntimeRunId;
    const canDisplayRuntimeEvent = shouldDisplayRuntimeEvent(evt.displaySurface, evt.displayPolicy);
    const processTypes = new Set([
      'agent_observing',
      'agent_planning',
      'agent_acting',
      'agent_reflecting',
      'mission_updated',
      'run_created',
      'run_update',
      'step_complete',
      'step_fail',
      'confirmation_required',
      'production_progress',
    ]);
    if (canDisplayRuntimeEvent && processTypes.has(evt.type) && isUsefulRuntimeEvent(evt.type, evt.message || '', evt.data)) {
      const item = toRuntimeEventView(evt.type, evt.message, evt.data, evt.timestamp, effectiveRunId, eventSourceMessageId, evt.stage, evt.status);
      if (item) {
        appendExecutionEvent(item, eventSourceMessageId || undefined);
      }
    }
    switch (evt.type) {
      case 'artifact_preview':
        if (canDisplayRuntimeEvent && isArtifactPreviewView(evt.data)) {
          const preview = evt.data as AgentArtifactPreviewView;
          appendExecutionPreview(preview, effectiveRunId, eventSourceMessageId || undefined);
        }
        break;
      case 'agent_acting':
        break;
      case 'mission_updated':
      case 'confirmation_required':
        invalidateAgentState();
        break;
      case 'run_created':
      case 'run_update':
        if (eventRunId && !isTerminalRuntimeUpdate(evt.data, evt.message)) {
          setActiveRuntimeRunId(eventRunId);
        }
        if (isNovelAgentRun(evt.data)) {
          setActiveRun(evt.data);
        }
        {
          const terminalState = runtimeEventTerminalState(evt.data, evt.message);
          if (terminalState) {
            markExecutionBlockDone(effectiveRunId, terminalState === 'failed');
            setCancellingRuntimeRunId(null);
            if (!eventRunId || eventRunId === activeRuntimeRunId) {
              setActiveRuntimeRunId(null);
            }
            inFlightCountRef.current = 0;
            setSending(false);
            void reloadSessions();
          }
        }
        if (evt.data && typeof evt.data === 'object') {
          const candidate = evt.data as { status?: string; phase?: string; activeProjectId?: string; projectId?: string; runId?: string };
          if (candidate.activeProjectId || candidate.projectId) {
            setCurrentProjectId(candidate.activeProjectId || candidate.projectId || null);
          }
          if (isTerminalRuntimeUpdate(evt.data, evt.message)) {
            const status = (candidate.status || candidate.phase || '').toLowerCase();
            markExecutionBlockDone(effectiveRunId, status === 'failed');
            setCancellingRuntimeRunId(null);
            if (!eventRunId || eventRunId === activeRuntimeRunId) {
              setActiveRuntimeRunId(null);
            }
            inFlightCountRef.current = 0;
            setSending(false);
            void reloadSessions();
          }
        }
        invalidateAgentState();
        break;
      case 'step_complete':
      case 'step_fail':
        if (evt.type === 'step_fail') {
          markExecutionBlockDone(effectiveRunId, true);
          setCancellingRuntimeRunId(null);
          setSending(false);
        }
        invalidateAgentState();
        break;
      case 'agent_reply_delta': {
        const candidate = evt.data && typeof evt.data === 'object'
          ? evt.data as { delta?: string; content?: string; append?: string }
          : null;
        const delta = candidate?.delta ?? candidate?.append ?? candidate?.content ?? evt.message;
        if (delta) {
          setStreamingReply((prev) => {
            const now = new Date();
            if (prev && (prev.runId === effectiveRunId || (!prev.runId && !effectiveRunId))) {
              return {
                ...prev,
                runId: prev.runId ?? effectiveRunId ?? null,
                content: `${prev.content}${delta}`,
                updatedAt: now,
              };
            }
            return {
              id: `stream-${effectiveRunId || Date.now()}`,
              runId: effectiveRunId ?? null,
              content: delta,
              startedAt: now,
              updatedAt: now,
            };
          });
        }
        break;
      }
      case 'agent_reply': {
        const response = evt.data as AgentChatResponse | null;
        const content = response?.reply || evt.message;
        const activeProjectId = response?.activeProjectId
          || response?.memory?.missionPlan?.projectId
          || response?.missionPlan?.projectId
          || null;
        if (activeProjectId) {
          setCurrentProjectId(activeProjectId);
        }
        if (content && sessionId) {
          addAgentMessage(
            sessionId,
            content,
            response?.suggestions,
            response?.runId ?? evt.runId ?? undefined,
            response?.phase,
            response?.decision,
            response?.rag,
            response?.memory,
            response?.runtimeTrace,
            response?.memoryAudit,
          );
        }
        setStreamingReply(null);
        const replyDoneEvent: RuntimeEventView = {
          id: `agent-reply-done-${eventRunId || response?.runId || Date.now()}`,
          type: 'agent_reply',
          title: '后台执行已完成',
          detail: '执行结果已写入正式回复。',
          status: 'done',
          runId: effectiveRunId || response?.runId || null,
          sourceMessageId: eventSourceMessageId,
          timestamp: new Date(),
        };
        appendExecutionEvent(replyDoneEvent, eventSourceMessageId || undefined);
        markExecutionBlockDone(effectiveRunId || response?.runId || null);
        setCancellingRuntimeRunId(null);
        if (!eventRunId || eventRunId === activeRuntimeRunId) {
          setActiveRuntimeRunId(null);
        }
        inFlightCountRef.current = 0;
        setSending(false);
        void reloadSessions();
        invalidateAgentState();
        break;
      }
    }
  }, [activeRuntimeRunId, addAgentMessage, appendExecutionEvent, appendExecutionPreview, invalidateAgentState, markExecutionBlockDone, rememberRuntimeEventId, sessionId, setActiveRun, setCurrentProjectId, setSending]);

  const replayRuntimeEvents = useCallback(async (targetSessionId: string, afterEventId?: string | null) => {
    try {
      const events = await listRuntimeEvents({
        sessionId: targetSessionId,
        limit: 80,
        afterEventId,
      });
      events.forEach((evt) => {
        handleSseEvent(runtimeEventToSseEvent(evt, targetSessionId));
      });
    } catch {
      // Live SSE and active-run polling remain available; replay failures should not break chat input.
    }
  }, [handleSseEvent]);

  useEffect(() => {
    if (!sessionId) return;
    setConnected(false);
    const afterEventId = lastRuntimeEventIdRef.current[sessionId] || null;
    const es = createSseConnection(sessionId, afterEventId);

    es.onopen = () => {
      setConnected(true);
      void replayRuntimeEvents(sessionId, afterEventId);
    };
    es.onmessage = (event) => {
      try {
        const evt: AgentSseEvent = JSON.parse(event.data);
        addSseEvent(evt);
        handleSseEvent(evt);
      } catch {
        /* ignore malformed event */
      }
    };
    es.onerror = () => setConnected(false);

    return () => {
      es.close();
      setConnected(false);
    };
  }, [addSseEvent, handleSseEvent, replayRuntimeEvents, sessionId, setConnected]);

  const scrollChatToBottom = useCallback((behavior: ScrollBehavior = 'smooth') => {
    const thread = chatThreadRef.current;
    if (!thread) return;
    window.requestAnimationFrame(() => {
      thread.scrollTo({ top: thread.scrollHeight, behavior });
    });
  }, []);

  const autoScrollKey = useMemo(() => {
    const lastMessage = messages[messages.length - 1];
    const messageKey = lastMessage
      ? `${messages.length}:${lastMessage.id}:${lastMessage.timestamp.getTime()}`
      : '0';
    const streamKey = streamingReply
      ? `${streamingReply.id}:${streamingReply.content.length}`
      : '';
    return `${messageKey}::${streamKey}`;
  }, [messages, streamingReply]);

  useEffect(() => {
    const panel = chatThreadRef.current?.closest<HTMLElement>('.agent-chat-panel');
    const form = chatFormRef.current;
    if (!panel || !form) return;

    const syncInputSpace = () => {
      const height = Math.ceil(form.getBoundingClientRect().height);
      panel.style.setProperty('--chat-form-height', `${height}px`);
      panel.style.setProperty('--chat-input-space', `${height + 36}px`);
    };

    syncInputSpace();
    const observer = new ResizeObserver(syncInputSpace);
    observer.observe(form);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    scrollChatToBottom('smooth');
  }, [autoScrollKey, scrollChatToBottom]);

  const submitMessage = async (message: string) => {
    const msg = message.trim();
    if (!msg || !sessionId) {
      return;
    }

    setInput('');
    const userMessageId = addUserMessage(sessionId, msg);
    latestTurnAnchorMessageIdRef.current = userMessageId;
    const pendingBlockId = `turn-${userMessageId}`;
    setExecutionBlocks((prev) => [...prev, makeExecutionBlock(pendingBlockId, null, '正在处理请求', userMessageId)].slice(-24));
    inFlightCountRef.current += 1;
    setSending(true);
    setStreamingReply(null);
    addLog(`用户: ${msg}`);

    try {
      const res: AgentChatResponse = await sendChat({ message: msg, sessionId, clientMessageId: userMessageId });
      const isRuntimeAck = res.phase === 'queued';
      if (isRuntimeAck) {
        const runId = res.runId ?? null;
        if (runId) {
          runAnchorMessageIdsRef.current[runId] = userMessageId;
        }
        setActiveRuntimeRunId(runId);
        setCancellingRuntimeRunId(null);
        appendExecutionEvent({
          id: `runtime-ack-${runId || Date.now()}`,
          type: 'run_update',
          title: '后台任务已启动',
          detail: res.reply,
          status: 'running',
          runId,
          timestamp: new Date(),
        }, userMessageId);
      }
      if (!isRuntimeAck) {
        setActiveRuntimeRunId(null);
        setCancellingRuntimeRunId(null);
        addAgentMessage(sessionId, res.reply, res.suggestions, res.runId ?? undefined, res.phase, res.decision, res.rag, res.memory, res.runtimeTrace, res.memoryAudit);
        setExecutionBlocks((prev) => prev.filter((block) => (
          block.id !== pendingBlockId || block.events.length > 0 || block.previews.length > 0
        )));
      }
      setResumeState({
        ...resumeState,
        pendingToolCall: null,
        pendingConfirmation: res.pendingConfirmation ?? res.memory?.pendingConfirmation ?? null,
      });
      const activeProjectId = res.activeProjectId
        || res.memory?.missionPlan?.projectId
        || res.missionPlan?.projectId
        || null;
      if (activeProjectId) {
        setCurrentProjectId(activeProjectId);
      }
      await reloadSessions();
      invalidateAgentState();
      addLog(`Agent: ${res.phase}`);
      if (!isRuntimeAck) {
        inFlightCountRef.current = Math.max(0, inFlightCountRef.current - 1);
        setSending(inFlightCountRef.current > 0);
      }
    } catch (err) {
      console.error('sendChat error:', err);
      addAgentMessage(sessionId, `请求失败: ${err instanceof Error ? err.message : '未知错误'}`);
      appendExecutionEvent({
        id: `request-failed-${Date.now()}`,
        type: 'step_fail',
        title: '请求失败',
        detail: err instanceof Error ? err.message : '未知错误',
        status: 'failed',
        runId: null,
        timestamp: new Date(),
      }, userMessageId);
      setActiveRuntimeRunId(null);
      setCancellingRuntimeRunId(null);
      inFlightCountRef.current = Math.max(0, inFlightCountRef.current - 1);
      setSending(inFlightCountRef.current > 0);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void submitMessage(input);
  };

  const conversationItems: Array<
    | { kind: 'message'; id: string; at: number; message: (typeof messages)[number] }
    | { kind: 'stream'; id: string; at: number; stream: StreamingReplyView }
  > = [
    ...messages.map((message) => ({
      kind: 'message' as const,
      id: `message-${message.id}`,
      at: message.timestamp.getTime(),
      message,
    })),
    ...(streamingReply ? [{
      kind: 'stream' as const,
      id: `stream-${streamingReply.id}`,
      at: streamingReply.startedAt.getTime(),
      stream: streamingReply,
    }] : []),
  ].sort((left, right) => left.at - right.at);
  const messageExecutionBlocks = useMemo(() => {
    return executionBlocks.reduce<Record<string, ExecutionBlockView[]>>((groups, block) => {
      const anchorMessageId = block.anchorMessageId
        || (block.runId ? runAnchorMessageIdsRef.current[block.runId] : '');
      if (!anchorMessageId) return groups;
      return {
        ...groups,
        [anchorMessageId]: [...(groups[anchorMessageId] ?? []), block],
      };
    }, {});
  }, [executionBlocks, messages]);
  const hasRunningExecutionBlock = executionBlocks.some((block) => block.status === 'running');

  const handleCancelRuntimeRun = useCallback(async (runId: string) => {
    if (!runId || cancellingRuntimeRunId === runId) return;
    setCancellingRuntimeRunId(runId);
    appendExecutionEvent({
      id: `cancel-request-${runId}-${Date.now()}`,
      type: 'run_update',
      title: '正在请求取消',
      detail: '已发送取消请求，Agent 会在安全边界停止后台执行。',
      status: 'running',
      runId,
      timestamp: new Date(),
    });

    try {
      const run = await cancelRuntimeRun(runId);
      appendExecutionEvent(runtimeRunToEvent(run));
      setActiveRuntimeRunId(run.runId);
      invalidateAgentState();
      void refreshActiveRuntimeRun(run.sessionId);
    } catch (err) {
      setCancellingRuntimeRunId(null);
      appendExecutionEvent({
        id: `cancel-failed-${runId}-${Date.now()}`,
        type: 'step_fail',
        title: '取消请求失败',
        detail: err instanceof Error ? err.message : '无法取消当前后台任务',
        status: 'failed',
        runId,
        timestamp: new Date(),
      });
    }
  }, [appendExecutionEvent, cancellingRuntimeRunId, invalidateAgentState, refreshActiveRuntimeRun]);

  const renderExecutionBlock = (block: ExecutionBlockView) => {
    const productionTracks = productionStageTrackGroups(block.events);
    const visibleEvents = block.expanded ? executionVisibleEvents(block) : [];
    const visiblePreviews = block.expanded ? block.previews : block.previews.slice(0, 1);
    const detailCount = block.events.length + block.previews.length;
    const productionSummary = productionExecutionSummary(block.events);
    const duration = formatExecutionDuration(
      block.startedAt,
      block.status === 'running' ? clockNow : block.updatedAt,
    );
    const outputLabel = executionBlockOutputLabel(block);
    const destination = executionBlockDestination(block);

    return (
      <section className={`agent-execution-block ${block.status}`} key={block.id}>
        <button
          type="button"
          className="agent-execution-summary"
          onClick={() => toggleExecutionBlock(block.id)}
          aria-expanded={block.expanded}
        >
          <span className="agent-execution-dot" aria-hidden="true" />
          <strong>{block.title}</strong>
          <span className="agent-execution-output">{outputLabel}</span>
          <span className="agent-execution-duration">{duration}</span>
          <span className="agent-execution-chevron">{block.expanded ? '⌃' : '›'}</span>
        </button>

        {block.status === 'running' && block.runId && (
          <div className="agent-execution-actions">
            <button
              type="button"
              className="agent-runtime-cancel"
              disabled={cancellingRuntimeRunId === block.runId}
              onClick={() => void handleCancelRuntimeRun(block.runId!)}
            >
              {cancellingRuntimeRunId === block.runId ? '取消中' : '取消任务'}
            </button>
          </div>
        )}

        {block.expanded && detailCount > 0 && (
          <div className="agent-execution-details">
            <div className="agent-execution-brief">
              <span>{productionSummary || block.summary || '后台执行状态已更新。'}</span>
              <small>{destination}</small>
              {block.runId && <small>{block.runId.slice(0, 8)}</small>}
            </div>

            {productionTracks.length > 0 && (
              <div className="agent-production-track-list" aria-label="章节生产闭环阶段">
                {productionTracks.map((track) => (
                  <section className={`agent-production-track-card ${track.status}`} key={track.key}>
                    <header className="agent-production-track-header">
                      <strong>{track.label}</strong>
                      <span>{track.summary}</span>
                    </header>
                    <div className="agent-production-stage-track">
                      {track.items.map((stage) => (
                        <div
                          className={`agent-production-stage-step ${stage.status}`}
                          key={stage.key}
                          title={stage.detail}
                        >
                          <i aria-hidden="true" />
                          <span>{stage.label}</span>
                          <small>
                            {productionStageStatusLabel(stage.status)}
                            {(stage.repeatCount ?? 1) > 1 ? ` · ${stage.repeatCount} 次` : ''}
                          </small>
                        </div>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            )}

            {visibleEvents.length > 0 && (
              <div className="agent-execution-events">
                {visibleEvents.map((event) => (
                  <div className={`agent-execution-event ${event.status}`} key={event.id}>
                    <i aria-hidden="true" />
                    <div>
                      <strong>{event.title}</strong>
                      <span>
                        {event.detail || '已收到运行事件。'}
                        {(event.repeatCount ?? 1) > 1 ? `（同阶段更新 ${event.repeatCount} 次）` : ''}
                      </span>
                      <small>{formatSessionTime(event.timestamp.toISOString())}</small>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {visiblePreviews.length > 0 && (
              <div className="agent-execution-previews">
                {visiblePreviews.map((preview) => (
                  <article className="agent-execution-preview" key={`${preview.title}-${preview.createdAt}`}>
                    <div>
                      <strong>{preview.title}</strong>
                      <span>{preview.resultLocation}</span>
                    </div>
                    <p>{preview.summary}</p>
                    {preview.items.length > 0 && (
                      <ul>
                        {preview.items.slice(0, block.expanded ? 4 : 2).map((previewItem, index) => (
                          <li key={`${preview.title}-${index}`}>{previewItem}</li>
                        ))}
                      </ul>
                    )}
                  </article>
                ))}
              </div>
            )}
          </div>
        )}
      </section>
    );
  };

  return (
    <div className="agent-command-center">
      <aside className="agent-conversation-sidebar">
        <section className="agent-sidebar-head">
          <div>
            <p className="agent-kicker">Agent Sessions</p>
            <h1>会话历史</h1>
          </div>
          <button type="button" className="new-session-btn primary" onClick={() => void createNewSession()}>
            新建
          </button>
        </section>

        <section className="session-history-panel">
          <div className="session-history-header">
            <div>
              <span>独立记忆</span>
              <strong>{sessionsLoaded ? `${sessions.length} 个会话` : '加载中'}</strong>
            </div>
          </div>

          {sessionLoadError && <div className="session-load-error">{sessionLoadError}</div>}

          <div className="session-list">
            {!sessionsLoaded ? (
              <div className="empty slim">正在读取会话历史...</div>
            ) : sessions.length === 0 ? (
              <div className="empty slim">还没有历史会话</div>
            ) : sessions.map((item) => (
              <button
                key={item.sessionId}
                type="button"
                className={`session-item ${item.sessionId === sessionId ? 'active' : ''}`}
                onClick={() => void selectSession(item.sessionId)}
                onContextMenu={(e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  setSessionMenu({ session: item, x: e.clientX, y: e.clientY });
                }}
              >
                <strong className="session-item-title" title={item.title || '新会话'}>
                  {item.title || '新会话'}
                </strong>
                <span className="session-item-preview" title={sessionPreview(item)}>
                  {sessionPreview(item)}
                </span>
                <small className="session-item-meta">
                  <span>{item.messageCount} 条消息</span>
                  <time>{formatSessionTime(item.updatedAt)}</time>
                </small>
              </button>
            ))}
          </div>
        </section>
      </aside>

      <main className="agent-main-stage">
        <section className="agent-chat-panel">
          <div className="chat-thread" ref={chatThreadRef}>
            {resumeState.pendingConfirmation && (
              <div className="agent-session-state">
                <div className="message-meta">恢复状态</div>
                <div className="agent-confirmation-line">
                  <strong>待确认：{resumeState.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                  <span>{resumeState.pendingConfirmation.impactSummary}</span>
                </div>
              </div>
            )}
            {conversationItems.map((item, index) => {
              if (item.kind === 'stream') {
                return (
                  <div key={chatItemRenderKey(item.id, index)} className="agent-message agent streaming">
                    <div className="message-meta">Agent</div>
                    <div className="message-content">{item.stream.content}</div>
                  </div>
                );
              }

              const msg = item.message;
              const ownedExecutionBlocks = messageExecutionBlocks[msg.id] ?? [];
              return (
                <div key={chatItemRenderKey(item.id, index)} className={`agent-turn ${msg.role}`}>
                  <div className={`agent-message ${msg.role}`}>
                    <div className="message-meta">{msg.role === 'user' ? '你' : 'Agent'}</div>
                    <div className="message-content">{msg.content}</div>
                    {msg.role === 'agent' && msg.memory?.pendingConfirmation && (
                      <div className="agent-confirmation-line">
                        <strong>待确认：{msg.memory.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                        <span>{msg.memory.pendingConfirmation.impactSummary}</span>
                      </div>
                    )}
                  </div>
                  {ownedExecutionBlocks.length > 0 && (
                    <div className="agent-turn-executions" aria-label="本轮后台执行">
                      {ownedExecutionBlocks.map((block) => renderExecutionBlock(block))}
                    </div>
                  )}
                </div>
              );
            })}
            {isSending && !hasRunningExecutionBlock && !streamingReply && (
              <div className="agent-message agent thinking">
                <div className="message-meta">Agent</div>
                <div className="thinking-dots"><span /><span /><span /></div>
              </div>
            )}
            <div ref={chatEndRef} />
          </div>

          <form className="chat-form" ref={chatFormRef} onSubmit={handleSubmit}>
            <textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder={sessionId ? (isSending ? 'Agent 正在执行中，你可以继续追问进度、补充要求或要求暂停...' : '把你的提示词直接给 Agent，它会结合系统提示词、工程状态和工具自己决定下一步...') : '正在准备会话...'}
              rows={3}
              disabled={!sessionId}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  handleSubmit(e);
                }
              }}
            />
            <button type="submit" className="ink-button" disabled={!input.trim() || !sessionId}>
              发送
            </button>
          </form>
        </section>
      </main>

      {sessionMenu && (
        <div
          className="session-context-menu"
          style={{ left: sessionMenu.x, top: sessionMenu.y }}
          onClick={(e) => e.stopPropagation()}
        >
          <button type="button" onClick={() => void renameSession(sessionMenu.session)}>
            重命名
          </button>
          <button type="button" onClick={() => void archiveSession(sessionMenu.session)}>
            归档
          </button>
        </div>
      )}
    </div>
  );
}
