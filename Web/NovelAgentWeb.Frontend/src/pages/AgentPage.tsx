import { useCallback, useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  ApiError,
  createAgentSession,
  createSseConnection,
  listAgentSessions,
  resumeAgentSession,
  sendChat,
  updateAgentSession,
} from '../api';
import type {
  AgentChatResponse,
  AgentArtifactPreviewView,
  AgentSessionResumeResponse,
  AgentSessionSummary,
  AgentRuntimeEventView,
  AgentSseEvent,
  AgentToolProgressView,
  AgentWorkingMemorySnapshot,
  NovelAgentRun,
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

function agentPhaseLabel(value?: string) {
  const labels: Record<string, string> = {
    foundation_intake: '补地基',
    foundation_planning: '生成地基候选',
    needs_volume_plan: '需要卷规划',
    volume_planning: '规划卷',
    ready_for_chapter_work: '可推进章节',
    chapter_planning: '规划章节',
    chapter_candidate_selection: '等待选候选',
    chapter_generation: '准备写正文',
  };
  return value ? labels[value] ?? '' : '';
}

function readinessLabel(value?: string) {
  const labels: Record<string, string> = {
    unknown: '未判断',
    needs_author_input: '等作者补充',
    ready_for_foundation: '地基信息已够',
    needs_volume_plan: '缺卷规划',
    ready_for_chapter_plan: '可规划章节',
    needs_candidate_selection: '等候选确认',
    ready_for_chapter_generation: '可生成正文',
    ready: '就绪',
  };
  return value ? labels[value] ?? value : '';
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

function shouldShowMemoryLine(msg: { suggestions?: string[]; memory?: AgentWorkingMemorySnapshot | null }) {
  const mission = msg.memory?.mission;
  if (!mission) return false;
  const hasSuggestions = Boolean(msg.suggestions?.length);
  const hasPhase = hasSuggestions && Boolean(agentPhaseLabel(mission.creativePhase));
  const hasReadiness = hasSuggestions && mission.readiness !== 'unknown' && Boolean(readinessLabel(mission.readiness));
  return hasPhase || hasReadiness || Boolean(userVisibleMissionText(mission.pendingUserDecision));
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
  runId?: string | null;
  timestamp: Date;
}

interface ExecutionBlockView {
  id: string;
  runId?: string | null;
  status: 'running' | 'done' | 'failed';
  title: string;
  summary: string;
  startedAt: Date;
  updatedAt: Date;
  events: RuntimeEventView[];
  previews: AgentArtifactPreviewView[];
  expanded: boolean;
}

function runtimeRunIdFromEvent(evt: AgentSseEvent) {
  if (evt.runId) return evt.runId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

function runtimeRunIdFromRuntimeEvent(evt: AgentRuntimeEventView) {
  if (evt.runId) return evt.runId;
  if (!evt.data || typeof evt.data !== 'object') return null;
  const candidate = evt.data as { runtimeRunId?: string; runId?: string };
  return candidate.runtimeRunId || candidate.runId || null;
}

function runtimeEventStatus(type: string): RuntimeEventView['status'] {
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

function runtimeEventTitle(type: string, message: string, data: unknown) {
  if (isToolProgressView(data)) {
    return data.title;
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
): RuntimeEventView | null {
  if (!isUsefulRuntimeEvent(type, message || '', data)) return null;
  return {
    id: `${type}-${timestamp ? new Date(timestamp).getTime() : Date.now()}-${Math.random().toString(16).slice(2)}`,
    type,
    title: runtimeEventTitle(type, message, data),
    detail: runtimeEventDetail(message, data),
    status: isTerminalRuntimeUpdate(data)
      ? ((data as { status?: string }).status === 'failed' ? 'failed' : 'done')
      : runtimeEventStatus(type),
    runId,
    timestamp: timestamp ? new Date(timestamp) : new Date(),
  };
}

function isTerminalRuntimeUpdate(data: unknown) {
  if (!data || typeof data !== 'object') return false;
  const candidate = data as { status?: string; phase?: string };
  const status = candidate.status?.toLowerCase();
  const phase = candidate.phase?.toLowerCase();
  return status === 'completed'
    || status === 'failed'
    || phase === 'completed'
    || phase === 'failed';
}

function makeExecutionBlock(id: string, runId?: string | null, title = '正在执行') : ExecutionBlockView {
  const now = new Date();
  return {
    id,
    runId: runId ?? null,
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

function executionBlockTitle(status: ExecutionBlockView['status'], eventCount: number) {
  if (status === 'failed') return '执行遇到问题';
  if (status === 'done') return eventCount > 0 ? `已完成 ${eventCount} 个步骤` : '已完成';
  return eventCount > 0 ? `正在执行 ${eventCount} 个步骤` : '正在处理请求';
}

function formatExecutionDuration(startedAt: Date, updatedAt: Date) {
  const ms = Math.max(0, updatedAt.getTime() - startedAt.getTime());
  const seconds = Math.max(1, Math.round(ms / 1000));
  if (seconds < 60) return `${seconds} 秒`;
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest > 0 ? `${minutes} 分 ${rest} 秒` : `${minutes} 分`;
}

function buildExecutionBlocksFromRuntimeEvents(events: AgentRuntimeEventView[]): ExecutionBlockView[] {
  const blocks: ExecutionBlockView[] = [];

  events.forEach((evt, index) => {
    const runId = runtimeRunIdFromRuntimeEvent(evt);
    if (evt.type === 'artifact_preview' && isArtifactPreviewView(evt.data)) {
      const blockId = runId ? `run-${runId}` : `resume-${index}`;
      let block = blocks.find((item) => item.id === blockId || (runId && item.runId === runId));
      if (!block) {
        block = makeExecutionBlock(blockId, runId, '最近执行产物');
        block.startedAt = new Date(evt.timestamp);
        blocks.push(block);
      }
      block.previews = [evt.data as AgentArtifactPreviewView, ...block.previews].slice(0, 3);
      block.summary = (evt.data as AgentArtifactPreviewView).summary;
      block.updatedAt = new Date(evt.timestamp);
      return;
    }

    const item = toRuntimeEventView(evt.type, evt.message, evt.data, evt.timestamp, runId);
    if (!item) return;
    const blockId = runId ? `run-${runId}` : `resume-${index}`;
    let block = blocks.find((entry) => entry.id === blockId || (runId && entry.runId === runId));
    if (!block) {
      block = makeExecutionBlock(blockId, runId, '最近执行');
      block.startedAt = item.timestamp;
      blocks.push(block);
    }
    const status = executionStatusFromEvent(item);
    block.events = [...block.events, item].slice(-8);
    block.status = status === 'running' && block.status === 'failed' ? 'failed' : status;
    block.title = executionBlockTitle(block.status, block.events.length);
    block.summary = item.detail || item.title;
    block.updatedAt = item.timestamp;
  });

  return blocks.slice(-3);
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
  const [liveProgress, setLiveProgress] = useState<AgentToolProgressView | null>(null);
  const [runtimeEvents, setRuntimeEvents] = useState<RuntimeEventView[]>([]);
  const [artifactPreviews, setArtifactPreviews] = useState<AgentArtifactPreviewView[]>([]);
  const [activeRuntimeRunId, setActiveRuntimeRunId] = useState<string | null>(null);
  const [runtimeDetailsOpen, setRuntimeDetailsOpen] = useState(false);
  const [executionBlocks, setExecutionBlocks] = useState<ExecutionBlockView[]>([]);
  const inFlightCountRef = useRef(0);
  const chatEndRef = useRef<HTMLDivElement>(null);
  const lastResumedSessionIdRef = useRef('');

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

  const appendExecutionEvent = useCallback((event: RuntimeEventView) => {
    setExecutionBlocks((prev) => {
      const now = event.timestamp;
      const runIndex = event.runId ? prev.findIndex((block) => block.runId === event.runId) : -1;
      const targetIndex = runIndex >= 0
        ? runIndex
        : prev.findIndex((block) => block.status === 'running' && !block.runId);
      const next = [...prev];
      const status = executionStatusFromEvent(event);

      if (targetIndex >= 0) {
        const current = next[targetIndex];
        const events = [...current.events, event].slice(-8);
        const nextStatus = status === 'running' && current.status === 'failed' ? 'failed' : status;
        next[targetIndex] = {
          ...current,
          runId: current.runId ?? event.runId ?? null,
          status: nextStatus,
          title: executionBlockTitle(nextStatus, events.length),
          summary: event.detail || event.title,
          updatedAt: now,
          events,
        };
        return next.slice(-3);
      }

      const block = makeExecutionBlock(
        event.runId ? `run-${event.runId}` : `event-${event.id}`,
        event.runId,
        executionBlockTitle(status, 1),
      );
      block.status = status;
      block.summary = event.detail || event.title;
      block.startedAt = now;
      block.updatedAt = now;
      block.events = [event];
      return [...next, block].slice(-3);
    });
  }, []);

  const appendExecutionPreview = useCallback((preview: AgentArtifactPreviewView, runId?: string | null) => {
    setExecutionBlocks((prev) => {
      const previewRunId = preview.runId || runId || null;
      const runIndex = previewRunId ? prev.findIndex((block) => block.runId === previewRunId) : -1;
      const targetIndex = runIndex >= 0
        ? runIndex
        : prev.findIndex((block) => block.status === 'running' && !block.runId);
      const next = [...prev];

      if (targetIndex >= 0) {
        const current = next[targetIndex];
        next[targetIndex] = {
          ...current,
          runId: current.runId ?? previewRunId,
          status: current.status === 'failed' ? 'failed' : current.status,
          title: current.status === 'done' ? current.title : executionBlockTitle(current.status, current.events.length),
          summary: preview.summary || current.summary,
          updatedAt: new Date(preview.createdAt || Date.now()),
          previews: [preview, ...current.previews].slice(0, 3),
        };
        return next.slice(-3);
      }

      const block = makeExecutionBlock(
        previewRunId ? `run-${previewRunId}` : `preview-${Date.now()}`,
        previewRunId,
        '收到阶段性产物',
      );
      block.summary = preview.summary;
      block.startedAt = new Date(preview.createdAt || Date.now());
      block.updatedAt = block.startedAt;
      block.previews = [preview];
      return [...next, block].slice(-3);
    });
  }, []);

  const markExecutionBlockDone = useCallback((runId?: string | null, failed = false) => {
    setExecutionBlocks((prev) => {
      const targetIndex = runId
        ? prev.findIndex((block) => block.runId === runId)
        : [...prev].reverse().findIndex((block) => block.status === 'running');
      if (targetIndex < 0) return prev;
      const realIndex = runId ? targetIndex : prev.length - 1 - targetIndex;
      return prev.map((block, index) => {
        if (index !== realIndex) return block;
        const status = failed ? 'failed' : 'done';
        return {
          ...block,
          status,
          title: executionBlockTitle(status, block.events.length),
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

  const applySessionResume = useCallback((detail: AgentSessionResumeResponse) => {
    const memory = mergeResumeMemory(detail);
    lastResumedSessionIdRef.current = detail.sessionId;
    setSessionId(detail.sessionId);
    setCurrentProjectId(detail.activeProjectId || null);
    setActiveRun(null);
    setActiveRuntimeRunId(detail.activeRunId ?? null);
    clearEvents();
    const recentRuntimeEvents = detail.recentRuntimeEvents ?? [];
    setRuntimeEvents(recentRuntimeEvents
      .map((evt) => toRuntimeEventView(
        evt.type,
        evt.message,
        evt.data,
        evt.timestamp,
        runtimeRunIdFromRuntimeEvent(evt),
      ))
      .filter((evt): evt is RuntimeEventView => evt != null)
      .slice(-6));
    setArtifactPreviews(recentRuntimeEvents
      .filter((evt) => evt.type === 'artifact_preview' && isArtifactPreviewView(evt.data))
      .map((evt) => evt.data as AgentArtifactPreviewView)
      .slice(-4)
      .reverse());
    setExecutionBlocks(buildExecutionBlocksFromRuntimeEvents(recentRuntimeEvents));
    setRuntimeDetailsOpen(false);
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
  }, [clearEvents, loadSessionMessages, setActiveRun, setCurrentProjectId, setResumeState, setSessionId]);

  const clearMissingSession = useCallback((id: string) => {
    if (lastResumedSessionIdRef.current === id) {
      lastResumedSessionIdRef.current = '';
    }
    setSessionLoadError('');
    setSessionId('');
    setActiveRun(null);
    setActiveRuntimeRunId(null);
    clearResumeState();
    clearEvents();
    setRuntimeEvents([]);
    setArtifactPreviews([]);
    setExecutionBlocks([]);
    setRuntimeDetailsOpen(false);
    setCurrentSessionMessages('');
  }, [clearEvents, clearResumeState, setActiveRun, setCurrentSessionMessages, setSessionId]);

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
      clearResumeState();
      clearEvents();
      setRuntimeEvents([]);
      setArtifactPreviews([]);
      setExecutionBlocks([]);
      setRuntimeDetailsOpen(false);
      loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '新建会话失败');
    }
  }, [clearEvents, clearResumeState, loadSessionMessages, reloadSessions, setActiveRun, setSessionId]);

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

  const handleSseEvent = useCallback((evt: AgentSseEvent) => {
    const eventRunId = runtimeRunIdFromEvent(evt);
    const effectiveRunId = eventRunId || activeRuntimeRunId;
    const isForeignRuntimeEvent = Boolean(activeRuntimeRunId && eventRunId && eventRunId !== activeRuntimeRunId);
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
    ]);
    if (!isForeignRuntimeEvent && processTypes.has(evt.type) && isUsefulRuntimeEvent(evt.type, evt.message || '', evt.data)) {
      const item = toRuntimeEventView(evt.type, evt.message, evt.data, evt.timestamp, effectiveRunId);
      if (item) {
        setRuntimeEvents((prev) => [...prev.slice(-5), item]);
        appendExecutionEvent(item);
      }
    }
    switch (evt.type) {
      case 'artifact_preview':
        if (isArtifactPreviewView(evt.data)) {
          const preview = evt.data as AgentArtifactPreviewView;
          setArtifactPreviews((prev) => [preview, ...prev].slice(0, 4));
          appendExecutionPreview(preview, effectiveRunId);
          setRuntimeDetailsOpen(false);
        }
        break;
      case 'agent_acting':
        if (isToolProgressView(evt.data)) {
          setLiveProgress(evt.data);
        }
        break;
      case 'mission_updated':
      case 'confirmation_required':
        invalidateAgentState();
        break;
      case 'run_created':
      case 'run_update':
        if (eventRunId && !isForeignRuntimeEvent && !isTerminalRuntimeUpdate(evt.data)) {
          setActiveRuntimeRunId(eventRunId);
        }
        if (isNovelAgentRun(evt.data)) {
          setActiveRun(evt.data);
        }
        if (evt.data && typeof evt.data === 'object') {
          const candidate = evt.data as { status?: string; phase?: string; activeProjectId?: string; projectId?: string; runId?: string };
          if (candidate.activeProjectId || candidate.projectId) {
            setCurrentProjectId(candidate.activeProjectId || candidate.projectId || null);
          }
          if (isTerminalRuntimeUpdate(evt.data)) {
            const status = (candidate.status || candidate.phase || '').toLowerCase();
            setLiveProgress(null);
            setRuntimeEvents((prev) => prev.map((item) => item.status === 'running' ? { ...item, status: 'done' } : item));
            markExecutionBlockDone(effectiveRunId, status === 'failed');
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
        if (isToolProgressView(evt.data)) {
          setLiveProgress(evt.data.isRunning ? evt.data : null);
        }
        if (evt.type === 'step_fail') {
          markExecutionBlockDone(effectiveRunId, true);
          setSending(false);
        }
        invalidateAgentState();
        break;
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
          );
        }
        setLiveProgress(null);
        const replyDoneEvent: RuntimeEventView = {
          id: `agent-reply-done-${eventRunId || response?.runId || Date.now()}`,
          type: 'agent_reply',
          title: '后台执行已完成',
          detail: '执行结果已写入正式回复。',
          status: 'done',
          runId: effectiveRunId || response?.runId || null,
          timestamp: new Date(),
        };
        appendExecutionEvent(replyDoneEvent);
        markExecutionBlockDone(effectiveRunId || response?.runId || null);
        setRuntimeEvents((prev) => [
          ...prev.map((item) => item.status === 'running' ? { ...item, status: 'done' as const } : item).slice(-5),
          replyDoneEvent,
        ]);
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
  }, [activeRuntimeRunId, addAgentMessage, appendExecutionEvent, appendExecutionPreview, invalidateAgentState, markExecutionBlockDone, sessionId, setActiveRun, setCurrentProjectId, setSending]);

  useEffect(() => {
    if (!sessionId) return;
    setConnected(false);
    const es = createSseConnection(sessionId);

    es.onopen = () => setConnected(true);
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
  }, [addSseEvent, handleSseEvent, sessionId, setConnected]);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [executionBlocks, messages]);

  const submitMessage = async (message: string) => {
    const msg = message.trim();
    if (!msg || !sessionId) {
      return;
    }

    setInput('');
    addUserMessage(sessionId, msg);
    const pendingBlockId = `turn-${Date.now()}`;
    setExecutionBlocks((prev) => [...prev.slice(-2), makeExecutionBlock(pendingBlockId, null, '正在处理请求')]);
    inFlightCountRef.current += 1;
    setSending(true);
    setLiveProgress(null);
    setRuntimeEvents([]);
    setArtifactPreviews([]);
    setRuntimeDetailsOpen(false);
    addLog(`用户: ${msg}`);

    try {
      const res: AgentChatResponse = await sendChat({ message: msg, sessionId });
      const isRuntimeAck = res.phase === 'queued';
      if (isRuntimeAck) {
        const runId = res.runId ?? null;
        setActiveRuntimeRunId(runId);
        setRuntimeEvents((prev) => [
          ...prev.slice(-5),
          {
            id: `runtime-ack-${runId || Date.now()}`,
            type: 'run_update',
            title: '后台任务已启动',
            detail: res.reply,
            status: 'running',
            runId,
            timestamp: new Date(),
          },
        ]);
        appendExecutionEvent({
          id: `runtime-ack-${runId || Date.now()}`,
          type: 'run_update',
          title: '后台任务已启动',
          detail: res.reply,
          status: 'running',
          runId,
          timestamp: new Date(),
        });
      }
      if (!isRuntimeAck) {
        setActiveRuntimeRunId(null);
        addAgentMessage(sessionId, res.reply, res.suggestions, res.runId ?? undefined, res.phase, res.decision, res.rag, res.memory, res.runtimeTrace);
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
      });
      setActiveRuntimeRunId(null);
      inFlightCountRef.current = Math.max(0, inFlightCountRef.current - 1);
      setSending(inFlightCountRef.current > 0);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void submitMessage(input);
  };

  const hasRunningRuntime = runtimeEvents.some((event) => event.status === 'running');
  const latestRuntimeEvent = runtimeEvents[runtimeEvents.length - 1] ?? null;
  const primaryPreview = artifactPreviews[0] ?? null;
  const compactPreviewItems = primaryPreview?.items.slice(0, runtimeDetailsOpen ? 4 : 2) ?? [];
  const visibleRuntimeEvents = runtimeDetailsOpen
    ? [...runtimeEvents].slice(-5).reverse()
    : latestRuntimeEvent ? [latestRuntimeEvent] : [];
  const conversationItems: Array<
    | { kind: 'message'; id: string; at: number; message: (typeof messages)[number] }
    | { kind: 'execution'; id: string; at: number; block: ExecutionBlockView }
  > = [
    ...messages.map((message) => ({
      kind: 'message' as const,
      id: `message-${message.id}`,
      at: message.timestamp.getTime(),
      message,
    })),
    ...executionBlocks.map((block) => ({
      kind: 'execution' as const,
      id: `execution-${block.id}`,
      at: block.startedAt.getTime(),
      block,
    })),
  ].sort((left, right) => left.at - right.at);
  const shouldShowRuntimePanel = isSending
    ? Boolean(activeRuntimeRunId) || hasRunningRuntime
    : hasRunningRuntime
    || Boolean(activeRuntimeRunId)
    || Boolean(primaryPreview)
    || latestRuntimeEvent?.status === 'done'
    || latestRuntimeEvent?.status === 'failed';
  const canExpandRuntimePanel = runtimeEvents.length > 1
    || (primaryPreview?.items.length ?? 0) > compactPreviewItems.length
    || artifactPreviews.length > 1;

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
                <strong>{item.title || '新会话'}</strong>
                <span>{sessionPreview(item)}</span>
                <small>{item.messageCount} 条消息 · {formatSessionTime(item.updatedAt)}</small>
              </button>
            ))}
          </div>
        </section>
      </aside>

      <main className="agent-main-stage">
        <section className="agent-chat-panel">
          <div className="chat-thread">
            {resumeState.pendingConfirmation && (
              <div className="agent-session-state">
                <div className="message-meta">恢复状态</div>
                <div className="agent-confirmation-line">
                  <strong>待确认：{resumeState.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                  <span>{resumeState.pendingConfirmation.impactSummary}</span>
                </div>
              </div>
            )}
            {conversationItems.map((item) => {
              if (item.kind === 'execution') {
                const block = item.block;
                const latestEvent = block.events[block.events.length - 1] ?? null;
                const visibleEvents = block.expanded ? block.events : latestEvent ? [latestEvent] : [];
                const visiblePreviews = block.expanded ? block.previews : block.previews.slice(0, 1);
                const detailCount = block.events.length + block.previews.length;
                return (
                  <section className={`agent-execution-block ${block.status}`} key={item.id}>
                    <button
                      type="button"
                      className="agent-execution-summary"
                      onClick={() => toggleExecutionBlock(block.id)}
                      aria-expanded={block.expanded}
                    >
                      <span className="agent-execution-dot" aria-hidden="true" />
                      <span className="agent-execution-copy">
                        <strong>{block.title}</strong>
                        <span>{block.summary || '后台执行状态已更新。'}</span>
                      </span>
                      <span className="agent-execution-meta">
                        {formatExecutionDuration(block.startedAt, block.updatedAt)}
                        {block.runId ? ` · ${block.runId.slice(0, 8)}` : ''}
                      </span>
                      <span className="agent-execution-chevron">{block.expanded ? '收起' : '展开'}</span>
                    </button>

                    {block.expanded && detailCount > 0 && (
                      <div className="agent-execution-details">
                        {visibleEvents.length > 0 && (
                          <div className="agent-execution-events">
                            {visibleEvents.map((event) => (
                              <div className={`agent-execution-event ${event.status}`} key={event.id}>
                                <i aria-hidden="true" />
                                <div>
                                  <strong>{event.title}</strong>
                                  <span>{event.detail || '已收到运行事件。'}</span>
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
              }

              const msg = item.message;
              return (
                <div key={item.id} className={`agent-message ${msg.role}`}>
                  <div className="message-meta">{msg.role === 'user' ? '你' : 'Agent'}</div>
                  <div className="message-content">{msg.content}</div>
                  {msg.role === 'agent' && shouldShowMemoryLine(msg) && msg.memory?.mission && (
                    <div className="agent-memory-line">
                      {msg.suggestions && agentPhaseLabel(msg.memory.mission.creativePhase) && (
                        <span>{agentPhaseLabel(msg.memory.mission.creativePhase)}</span>
                      )}
                      {msg.suggestions && readinessLabel(msg.memory.mission.readiness) && msg.memory.mission.readiness !== 'unknown' && (
                        <span>{readinessLabel(msg.memory.mission.readiness)}</span>
                      )}
                      {userVisibleMissionText(msg.memory.mission.pendingUserDecision) && (
                        <span>{userVisibleMissionText(msg.memory.mission.pendingUserDecision)}</span>
                      )}
                    </div>
                  )}
                  {msg.role === 'agent' && msg.memory?.pendingConfirmation && (
                    <div className="agent-confirmation-line">
                      <strong>待确认：{msg.memory.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                      <span>{msg.memory.pendingConfirmation.impactSummary}</span>
                    </div>
                  )}
                </div>
              );
            })}
            {isSending && (
              <div className="agent-message agent thinking">
                <div className="message-meta">Agent</div>
                <div className="thinking-dots"><span /><span /><span /></div>
              </div>
            )}
            <div ref={chatEndRef} />
          </div>

          <form className="chat-form" onSubmit={handleSubmit}>
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

        <aside className="agent-runtime-rail" aria-label="当前任务进度">
          {shouldShowRuntimePanel ? (
            <div className="agent-runtime-panel">
              <div className="runtime-panel-head">
                <span>当前任务进度</span>
                <div className="runtime-panel-actions">
                  <strong>{hasRunningRuntime || isSending ? '执行中' : '最近状态'}</strong>
                  {canExpandRuntimePanel && (
                    <button type="button" className="runtime-panel-toggle" onClick={() => setRuntimeDetailsOpen((value) => !value)}>
                      {runtimeDetailsOpen ? '收起' : '展开'}
                    </button>
                  )}
                </div>
              </div>
              {liveProgress && (
                <div className="agent-live-progress">
                  <strong>{liveProgress.title}</strong>
                  <span>{liveProgress.detail}</span>
                </div>
              )}
              {primaryPreview && (
                <div className="artifact-preview-list">
                  <section className={`artifact-preview ${runtimeDetailsOpen ? 'expanded' : 'compact'}`} key={`${primaryPreview.runId || 'preview'}-${primaryPreview.createdAt}-${primaryPreview.title}`}>
                    <div className="artifact-preview-head">
                      <strong>{primaryPreview.title}</strong>
                      <span>{primaryPreview.resultLocation}</span>
                    </div>
                    <p>{primaryPreview.summary}</p>
                    {compactPreviewItems.length > 0 && (
                      <ul>
                        {compactPreviewItems.map((item, index) => (
                          <li key={`${primaryPreview.title}-${index}`}>{item}</li>
                        ))}
                      </ul>
                    )}
                    <small>
                      阶段性产物
                      {primaryPreview.runId ? ` · ${primaryPreview.runId.slice(0, 8)}` : ''}
                      {!runtimeDetailsOpen && primaryPreview.items.length > compactPreviewItems.length ? ` · 还有 ${primaryPreview.items.length - compactPreviewItems.length} 条` : ''}
                    </small>
                  </section>
                </div>
              )}
              {visibleRuntimeEvents.length > 0 && (
                <div className={`runtime-event-list ${runtimeDetailsOpen ? 'expanded' : 'compact'}`}>
                  {visibleRuntimeEvents.map((event) => (
                    <div className={`runtime-event ${event.status}`} key={event.id}>
                      <i aria-hidden="true" />
                      <div>
                        <strong>{event.title}</strong>
                        <span>{event.detail || '已收到运行事件。'}</span>
                        <small>
                          {formatSessionTime(event.timestamp.toISOString())}
                          {event.runId ? ` · ${event.runId.slice(0, 8)}` : ''}
                        </small>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          ) : (
            <div className="agent-runtime-empty">
              <span>当前没有后台任务</span>
              <strong>Agent 的工具执行、门禁和产物预览会显示在这里。</strong>
            </div>
          )}
        </aside>
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
