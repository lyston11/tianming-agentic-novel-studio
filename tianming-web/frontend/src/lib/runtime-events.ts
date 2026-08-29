import type {
  AgentArtifactPreviewView,
  AgentConversationTurnView,
  AgentRuntimeEventView,
  AgentSseEvent,
  AgentToolProgressView,
  NovelAgentRun,
  RuntimeRunDto,
} from '@/api/types';

export function isToolProgressView(value: unknown): value is AgentToolProgressView {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<AgentToolProgressView>;
  return typeof candidate.title === 'string' && typeof candidate.detail === 'string' && typeof candidate.status === 'string';
}

export function isArtifactPreviewView(value: unknown): value is AgentArtifactPreviewView {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<AgentArtifactPreviewView>;
  return typeof candidate.title === 'string'
    && typeof candidate.summary === 'string'
    && typeof candidate.resultLocation === 'string'
    && Array.isArray(candidate.items);
}

export function isNovelAgentRun(value: unknown): value is NovelAgentRun {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<NovelAgentRun>;
  return typeof candidate.runId === 'string' && Array.isArray(candidate.steps);
}

export interface RuntimeEventView {
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

export interface ExecutionBlockView {
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

export interface StreamingReplyView {
  id: string;
  runId?: string | null;
  content: string;
  startedAt: Date;
  updatedAt: Date;
}

export type ProductionStageTrackStatus = 'pending' | 'running' | 'done' | 'failed';

export interface ProductionStageTrackItem {
  key: string;
  label: string;
  status: ProductionStageTrackStatus;
  detail: string;
  timestamp?: Date;
  repeatCount?: number;
}

export interface ProductionStageTrackGroup {
  key: string;
  label: string;
  status: ProductionStageTrackStatus;
  summary: string;
  items: ProductionStageTrackItem[];
  updatedAt?: Date;
}

export function runtimeRunIdFromEvent(evt: AgentSseEvent) {
  if (evt.runId) return evt.runId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

export function runtimeRunIdFromRuntimeEvent(evt: AgentRuntimeEventView) {
  if (evt.runId) return evt.runId;
  const data = runtimeEventData(evt);
  if (!data || typeof data !== 'object') return null;
  const candidate = data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

export function runtimeSourceMessageIdFromEvent(evt: AgentSseEvent) {
  if (evt.sourceMessageId) return evt.sourceMessageId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { sourceMessageId?: string; clientMessageId?: string };
  return candidate.sourceMessageId || candidate.clientMessageId || null;
}

export function runtimeSourceMessageIdFromRuntimeEvent(evt: AgentRuntimeEventView) {
  if (evt.sourceMessageId) return evt.sourceMessageId;
  const data = runtimeEventData(evt);
  if (!data || typeof data !== 'object') return null;
  const candidate = data as { sourceMessageId?: string; clientMessageId?: string };
  return candidate.sourceMessageId || candidate.clientMessageId || null;
}

export function runtimeEventData(evt: AgentRuntimeEventView) {
  if (evt.data !== undefined) return evt.data;
  if (!evt.dataJson) return undefined;
  try {
    return JSON.parse(evt.dataJson);
  } catch {
    return undefined;
  }
}

export function runtimeEventTimestamp(evt: AgentRuntimeEventView) {
  return evt.timestamp || evt.createdAt || new Date().toISOString();
}

export function runtimeEventToSseEvent(evt: AgentRuntimeEventView, sessionId: string): AgentSseEvent {
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

export function eventField(data: unknown, field: string) {
  if (!data || typeof data !== 'object') return '';
  const value = (data as Record<string, unknown>)[field];
  return typeof value === 'string' ? value : '';
}

export function eventValueText(data: unknown, field: string) {
  if (!data || typeof data !== 'object') return '';
  const value = (data as Record<string, unknown>)[field];
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' && Number.isFinite(value)) return String(value);
  return '';
}

export function eventStage(data: unknown, explicitStage?: string | null) {
  return explicitStage || eventField(data, 'stage') || eventField(data, 'phase');
}

export function eventStatus(data: unknown, explicitStatus?: string | null) {
  return explicitStatus || eventField(data, 'status');
}

export function productionStageLabel(value?: string | null) {
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

export function productionStatusLabel(value?: string | null) {
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
  { key: 'projectresolved', label: '项目' },
  { key: 'knowledgeresolved', label: '知识' },
  { key: 'knowledgeclassified', label: '分类' },
  { key: 'storydesignbuilt', label: '规则' },
  { key: 'volumeplanbuilt', label: '分卷' },
  { key: 'chapterblueprintbuilt', label: '蓝图' },
  { key: 'packagebuilt', label: '生产包' },
  { key: 'draftgenerated', label: '正文' },
  { key: 'changesextracted', label: 'CHANGES' },
  { key: 'gatevalidated', label: '门禁' },
  { key: 'draftrewritten', label: '修复' },
  { key: 'reviewcompleted', label: '评审' },
  { key: 'chaptercommitted', label: '入书城' },
  { key: 'factspersisted', label: '事实' },
  { key: 'indexupdated', label: '索引' },
  { key: 'runcompleted', label: '完成' },
];

export function normalizeProductionStageKey(value?: string | null) {
  const key = value?.trim().toLowerCase();
  if (!key) return '';
  const aliases: Record<string, string> = {
    project_resolved: 'projectresolved',
    knowledge_resolved: 'knowledgeresolved',
    knowledge_classified: 'knowledgeclassified',
    story_design_built: 'storydesignbuilt',
    volume_plan_built: 'volumeplanbuilt',
    chapter_blueprint_built: 'chapterblueprintbuilt',
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
    runcompleted: 'runcompleted',
  };
  return aliases[key] ?? key;
}

export function isProductionProgressEvent(event: RuntimeEventView) {
  return event.type === 'production_progress';
}

export function productionEventMetadata(data: unknown) {
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

export function isDoneProductionStatus(status: RuntimeEventView['status'] | ProductionStageTrackStatus) {
  return status === 'done';
}

export function isFailedProductionStatus(status: RuntimeEventView['status'] | ProductionStageTrackStatus) {
  return status === 'failed';
}

export function productionStageStatusLabel(status: ProductionStageTrackStatus) {
  if (status === 'done') return '完成';
  if (status === 'running') return '进行中';
  if (status === 'failed') return '失败';
  return '等待';
}

export function productionStageTrackItems(events: RuntimeEventView[]): ProductionStageTrackItem[] {
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

export function productionGroupStatus(items: ProductionStageTrackItem[]): ProductionStageTrackStatus {
  if (items.some((item) => item.status === 'failed')) return 'failed';
  if (items.some((item) => item.key === 'runcompleted' && item.status === 'done')) return 'done';
  if (items.some((item) => item.key === 'indexupdated' && item.status === 'done')) return 'done';
  if (items.length > 0 && items.every((item) => item.status === 'done')) return 'done';
  return 'running';
}

export function productionGroupSummary(items: ProductionStageTrackItem[]) {
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

export function productionStageTrackGroups(events: RuntimeEventView[]): ProductionStageTrackGroup[] {
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

export function isProductionClosureComplete(events: RuntimeEventView[]) {
  const groups = productionStageTrackGroups(events);
  if (groups.length === 0) return false;
  return groups.every((group) => group.status === 'done');
}

export function productionExecutionSummary(events: RuntimeEventView[]) {
  const groups = productionStageTrackGroups(events);
  if (groups.length === 0) return '';
  const failed = groups.find((group) => group.status === 'failed');
  if (failed) return `${failed.label} 停在：${failed.summary}`;
  const running = groups.find((group) => group.status === 'running');
  const doneCount = groups.filter((group) => group.status === 'done').length;
  if (running) return `当前：${running.label} · ${running.summary} · ${doneCount}/${groups.length} 条链完成`;
  return `章节生产闭环已完成 · ${doneCount}/${groups.length} 条链`;
}

export function runtimeEventStatus(type: string, data?: unknown, explicitStatus?: string | null): RuntimeEventView['status'] {
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

export function isUsefulRuntimeEvent(type: string, message: string, data: unknown) {
  const compactMessage = message.trim().toLowerCase();
  if (compactMessage === 'idle' || compactMessage === 'continue') return false;
  if (type === 'mission_updated') return false;
  if (type === 'agent_reply') return false;
  if (type === 'agent_reflecting' && (compactMessage === 'idle' || compactMessage === 'continue')) return false;
  if (type === 'run_created' && isNovelAgentRun(data)) return false;
  return true;
}

export function shouldDisplayRuntimeEvent(displaySurface?: string | null, displayPolicy?: string | null) {
  const surface = displaySurface?.trim().toLowerCase();
  const policy = displayPolicy?.trim().toLowerCase();
  if (surface === 'admin_debug') return false;
  if (policy === 'hidden' || policy === 'debug_only') return false;
  return true;
}

export function runtimeEventTitle(type: string, message: string, data: unknown) {
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

export function runtimeEventDetail(message: string, data: unknown) {
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

export function runtimeEventViewId(parts: Array<string | number | null | undefined>) {
  const explicitEventId = String(parts[0] ?? '').trim();
  if (explicitEventId) return `runtime-event-${explicitEventId}`;

  const text = parts.slice(1)
    .map((part) => String(part ?? '').trim())
    .join('|');
  let hash = 2166136261;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return `runtime-event-${(hash >>> 0).toString(16)}`;
}

export function toRuntimeEventView(
  eventId: string | null | undefined,
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
  const timestampKey = timestamp ? new Date(timestamp).getTime() : '';
  return {
    id: runtimeEventViewId([eventId, type, timestampKey, runId, sourceMessageId, normalizedStage, normalizedStatus, message]),
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

export function terminalRuntimeStateFromMessage(message?: string | null): RuntimeEventView['terminalState'] {
  const text = message?.trim() || '';
  if (!text) return undefined;
  if (text.includes('后台执行已取消') || text.includes('运行已取消')) return 'cancelled';
  if (text.includes('后台执行已完成') || text.includes('本轮执行已完成')) return 'completed';
  if (text.includes('后台执行失败') || text.includes('心跳超时') || text.includes('可恢复失败')) return 'failed';
  return undefined;
}

export function isTerminalRuntimeMessage(message?: string | null) {
  return Boolean(terminalRuntimeStateFromMessage(message));
}

export function isTerminalRuntimeUpdate(data: unknown, message?: string | null) {
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

export function runtimeEventTerminalState(data: unknown, message?: string | null): RuntimeEventView['terminalState'] {
  if (!data || typeof data !== 'object') return terminalRuntimeStateFromMessage(message);
  const candidate = data as { status?: string; phase?: string };
  const status = candidate.status?.toLowerCase();
  const phase = candidate.phase?.toLowerCase();
  if (status === 'cancelled' || phase === 'cancelled') return 'cancelled';
  if (status === 'failed' || phase === 'failed') return 'failed';
  if (status === 'completed' || phase === 'completed') return 'completed';
  return terminalRuntimeStateFromMessage(message);
}

export function runtimeRunStatusLabel(run: RuntimeRunDto) {
  if (run.cancelRequested) return '正在取消';
  const status = run.status?.toLowerCase();
  if (status === 'queued') return '排队中';
  if (status === 'running') return '后台执行中';
  if (status === 'completed') return '后台执行完成';
  if (status === 'failed') return '后台执行失败';
  if (status === 'cancelled') return '后台执行已取消';
  return '后台运行状态';
}

export function runtimeRunToEvent(run: RuntimeRunDto, heartbeatAt?: string | null): RuntimeEventView {
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

export function makeExecutionBlock(
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

export function executionStatusFromEvent(event: RuntimeEventView): ExecutionBlockView['status'] {
  if (event.status === 'failed') return 'failed';
  if (event.type === 'agent_reply') return 'done';
  if (event.type === 'run_update' && event.status === 'done') return 'done';
  return 'running';
}

export function hasCancelledRuntimeUpdate(events: RuntimeEventView[]) {
  return events.some((event) => event.type === 'run_update' && event.terminalState === 'cancelled');
}

export function resolveExecutionBlockStatus(
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

export function executionBlockTitle(status: ExecutionBlockView['status'], eventCount: number) {
  if (status === 'failed') return '执行遇到问题';
  if (status === 'done') return eventCount > 0 ? '已处理' : '已完成';
  return eventCount > 0 ? '正在处理' : '正在处理';
}

export function executionBlockTitleFromEvents(status: ExecutionBlockView['status'], events: RuntimeEventView[]) {
  if (hasCancelledRuntimeUpdate(events)) return '后台执行已取消';
  if (events.some(isProductionProgressEvent)) {
    const productionGroups = productionStageTrackGroups(events);
    if (status === 'failed' || productionGroups.some((group) => group.status === 'failed')) {
      return '章节生产闭环遇到问题';
    }
    if (productionGroups.length > 0 && productionGroups.every((group) => group.status === 'done')) return '章节生产闭环已完成';
    if (productionGroups.some((group) => group.items.some((item) => item.key === 'chaptercommitted' && item.status === 'done'))) return '章节已入书城，后台收尾中';
    if (status === 'done') return '章节生产阶段已完成';
    return '章节生产闭环进行中';
  }
  return executionBlockTitle(status, events.length);
}

export function executionBlockOutputLabel(block: ExecutionBlockView) {
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

export function executionBlockDestination(block: ExecutionBlockView) {
  const previewLocation = block.previews.find((preview) => preview.resultLocation)?.resultLocation;
  if (previewLocation) return previewLocation;
  if (block.events.some((event) => event.type === 'production_progress' && normalizeProductionStageKey(event.stage) === 'chaptercommitted')) return '书城';
  if (block.events.some((event) => event.type === 'production_progress')) return '工作流';
  if (block.events.some((event) => event.type === 'agent_reply')) return '聊天回复';
  if (block.events.some((event) => event.type === 'confirmation_required')) return '等待你确认';
  return '工作流';
}

export function formatExecutionDuration(startedAt: Date, updatedAt: Date | number) {
  const end = typeof updatedAt === 'number' ? updatedAt : updatedAt.getTime();
  const ms = Math.max(0, end - startedAt.getTime());
  const seconds = Math.max(1, Math.round(ms / 1000));
  if (seconds < 60) return `${seconds} 秒`;
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest > 0 ? `${minutes} 分 ${rest} 秒` : `${minutes} 分`;
}

export function chatItemRenderKey(itemId: string, index: number) {
  return `chat-item-${itemId}-${index}`;
}

export function executionEventCompactKey(event: RuntimeEventView) {
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

export function compactExecutionEvents(events: RuntimeEventView[]) {
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

export function mergeExecutionBlockEvents(events: RuntimeEventView[], event: RuntimeEventView) {
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

export function isProductionToolProgressEvent(event: RuntimeEventView) {
  if (event.type !== 'agent_acting') return false;
  const text = `${event.title} ${event.detail}`;
  return /章节生产闭环|章节生产包|章节草稿|章节校验|章节修复|章节评审|章节提交|写入书城/.test(text);
}

export function shouldMergeExecutionBlockByAnchor(block: ExecutionBlockView, event: RuntimeEventView) {
  if (block.status !== 'running') return false;
  if (!block.runId) return true;
  if (isProductionToolProgressEvent(event)) return true;
  if (event.type === 'production_progress' && block.events.some(isProductionToolProgressEvent)) return true;
  if (event.type === 'run_update' && block.events.some(isProductionToolProgressEvent)) return true;
  return false;
}

export function executionVisibleEvents(block: ExecutionBlockView) {
  const hasProductionTrack = block.events.some(isProductionProgressEvent);
  return compactExecutionEvents(block.events.filter((event) => {
    if (event.type === 'production_progress') return false;
    if (hasProductionTrack && event.type === 'run_update') return false;
    if (hasProductionTrack && isProductionToolProgressEvent(event)) return false;
    return true;
  }));
}

export function latestUserMessageIdFromTurns(turns?: AgentConversationTurnView[] | null) {
  if (!turns || turns.length === 0) return undefined;
  for (let index = turns.length - 1; index >= 0; index -= 1) {
    if (turns[index].role === 'user') {
      return turns[index].turnId?.trim()
        || `${turns[index].role}-${turns[index].turnIndex ?? index}-${turns[index].createdAt}`;
    }
  }
  return undefined;
}

export function buildExecutionBlocksFromRuntimeEvents(
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

    const item = toRuntimeEventView(evt.eventId, evt.type, evt.message, data, timestamp, runId, sourceMessageId, evt.stage, evt.status);
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
