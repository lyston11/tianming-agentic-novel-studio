import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  ApiError,
  confirmGoalWorkflow,
  createAgentSession,
  getSessionActiveRuntimeRun,
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
  DirectorTurnView,
} from '../api/types';
import { useAgentStore } from '../stores/useAgentStore';
import { useAppStore } from '../stores/useAppStore';
import { useChatStore } from '../stores/useChatStore';
import { useProjectStore } from '../stores/useProjectStore';
import '../styles/agent.css';
import {
  buildExecutionBlocksFromRuntimeEvents,
  chatItemRenderKey,
  executionBlockDestination,
  executionBlockOutputLabel,
  executionBlockTitle,
  executionBlockTitleFromEvents,
  executionStatusFromEvent,
  executionVisibleEvents,
  formatExecutionDuration,
  isArtifactPreviewView,
  isNovelAgentRun,
  isTerminalRuntimeUpdate,
  isUsefulRuntimeEvent,
  latestUserMessageIdFromTurns,
  makeExecutionBlock,
  mergeExecutionBlockEvents,
  productionExecutionSummary,
  productionStageStatusLabel,
  productionStageTrackGroups,
  resolveExecutionBlockStatus,
  runtimeEventTerminalState,
  runtimeRunIdFromEvent,
  runtimeRunToEvent,
  runtimeSourceMessageIdFromEvent,
  shouldDisplayRuntimeEvent,
  shouldMergeExecutionBlockByAnchor,
  toRuntimeEventView,
} from './agent/runtimeEvents';
import type { ExecutionBlockView, RuntimeEventView, StreamingReplyView } from './agent/runtimeEvents';
import { useAgentRuntimeStream } from './agent/useAgentRuntimeStream';

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
  const navigate = useNavigate();
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
  const [executionBlocks, setExecutionBlocks] = useState<ExecutionBlockView[]>([]);
  const [streamingReply, setStreamingReply] = useState<StreamingReplyView | null>(null);
  const [clockNow, setClockNow] = useState(() => Date.now());
  const [directorProposal, setDirectorProposal] = useState<DirectorTurnView | null>(null);
  const [goalBudgetLimit, setGoalBudgetLimit] = useState('10');
  const [goalConfirmError, setGoalConfirmError] = useState('');
  const [isConfirmingGoal, setIsConfirmingGoal] = useState(false);
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
          inFlightCountRef.current = 0;
          setSending(false);
        }
        return;
      }

      const run = state.run;
      setActiveRuntimeRunId(run.runId);
      appendExecutionEvent(runtimeRunToEvent(run, state.heartbeatAt));
      const status = run.status.toLowerCase();
      if (status === 'completed' || status === 'failed' || status === 'cancelled') {
        markExecutionBlockDone(run.runId, status === 'failed');
        setActiveRuntimeRunId(null);
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
    clearEvents();
    const recentRuntimeEvents = detail.recentRuntimeEvents ?? [];
    resetRuntimeEventReplayState(detail.sessionId, recentRuntimeEvents);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
    setExecutionBlocks(buildExecutionBlocksFromRuntimeEvents(recentRuntimeEvents, latestTurnAnchorMessageIdRef.current || undefined));
    setStreamingReply(null);
    setDirectorProposal(null);
    setGoalConfirmError('');
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
    resetRuntimeEventReplayState(id, []);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = '';
    clearResumeState();
    clearEvents();
    setExecutionBlocks([]);
    setStreamingReply(null);
    setDirectorProposal(null);
    setGoalConfirmError('');
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
      resetRuntimeEventReplayState(detail.sessionId, []);
      runAnchorMessageIdsRef.current = {};
      latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
      clearResumeState();
      clearEvents();
      setExecutionBlocks([]);
      setStreamingReply(null);
      setDirectorProposal(null);
      setGoalConfirmError('');
      loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '新建会话失败');
    }
  }, [clearEvents, clearResumeState, loadSessionMessages, reloadSessions, resetRuntimeEventReplayState, setActiveRun, setCurrentProjectId, setSessionId]);

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
    const timer = window.setTimeout(() => void reloadSessions(), 0);
    return () => window.clearTimeout(timer);
  }, [reloadSessions]);

  useEffect(() => {
    if (!sessionId) return;
    if (!sessionsLoaded) return;
    if (lastResumedSessionIdRef.current === sessionId) return;
    const timer = window.setTimeout(() => {
      if (!sessions.some((item) => item.sessionId === sessionId)) {
        clearMissingSession(sessionId);
        return;
      }
      lastResumedSessionIdRef.current = sessionId;
      void resumeCurrentSession(sessionId);
    }, 0);
    return () => window.clearTimeout(timer);
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
    const timer = window.setTimeout(() => {
      if (sessions.length > 0) {
        void selectSession(sessions[0].sessionId);
        return;
      }
      void createNewSession();
    }, 0);
    return () => window.clearTimeout(timer);
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
      const item = toRuntimeEventView(evt.eventId, evt.type, evt.message, evt.data, evt.timestamp, effectiveRunId, eventSourceMessageId, evt.stage, evt.status);
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
  }, [activeRuntimeRunId, addAgentMessage, appendExecutionEvent, appendExecutionPreview, invalidateAgentState, markExecutionBlockDone, reloadSessions, rememberRuntimeEventId, sessionId, setActiveRun, setCurrentProjectId, setSending]);

  useAgentRuntimeStream({
    sessionId,
    lastRuntimeEventIdRef,
    addSseEvent,
    handleSseEvent,
    setConnected,
  });

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
        addAgentMessage(sessionId, res.reply, res.suggestions, res.runId ?? undefined, res.phase, res.decision, res.rag, res.memory, res.runtimeTrace, res.memoryAudit);
        setDirectorProposal(res.director?.proposedContract ? res.director : null);
        setGoalConfirmError('');
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
      inFlightCountRef.current = Math.max(0, inFlightCountRef.current - 1);
      setSending(inFlightCountRef.current > 0);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void submitMessage(input);
  };

  const handleConfirmGoal = async () => {
    if (!directorProposal?.proposedContract || !sessionId) return;
    const totalCostLimit = Number(goalBudgetLimit);
    if (!Number.isFinite(totalCostLimit) || totalCostLimit <= 0) {
      setGoalConfirmError('请输入大于 0 的金额上限');
      return;
    }

    setIsConfirmingGoal(true);
    setGoalConfirmError('');
    try {
      const result = await confirmGoalWorkflow(directorProposal, sessionId, totalCostLimit);
      const goalId = result.submission.goalId;
      if (!goalId) throw new Error('Goal 已确认，但服务端没有返回 Goal ID');
      setDirectorProposal(null);
      navigate(`/goal/${encodeURIComponent(goalId)}`);
    } catch (err) {
      setGoalConfirmError(err instanceof Error ? err.message : 'Goal 确认失败');
    } finally {
      setIsConfirmingGoal(false);
    }
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
      const anchorMessageId = block.anchorMessageId;
      if (!anchorMessageId) return groups;
      return {
        ...groups,
        [anchorMessageId]: [...(groups[anchorMessageId] ?? []), block],
      };
    }, {});
  }, [executionBlocks]);
  const hasRunningExecutionBlock = executionBlocks.some((block) => block.status === 'running');

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
            {directorProposal?.proposedContract && (
              <section className="agent-goal-proposal" aria-label="Creative Goal 提案">
                <header>
                  <div>
                    <span>Creative Goal</span>
                    <strong>{directorProposal.proposedContract.humanReadableObjective}</strong>
                  </div>
                  <small>{directorProposal.proposedContract.collaborationMode}</small>
                </header>
                <div className="agent-goal-contract-grid">
                  <div>
                    <span>章节范围</span>
                    <strong>{directorProposal.proposedContract.targetChapterRangeJson}</strong>
                  </div>
                  <div>
                    <span>成功条件</span>
                    <strong>{directorProposal.proposedContract.successCriteria.length} 项</strong>
                  </div>
                  <div>
                    <span>必须保留</span>
                    <strong>{directorProposal.proposedContract.mustPreserve.length} 项</strong>
                  </div>
                  <div>
                    <span>禁止改动</span>
                    <strong>{directorProposal.proposedContract.mustNotChange.length} 项</strong>
                  </div>
                </div>
                <div className="agent-goal-confirm-row">
                  <label>
                    <span>金额上限（USD）</span>
                    <span className="agent-goal-money-input">
                      <b>$</b>
                      <input
                        type="number"
                        min="0.01"
                        step="0.01"
                        value={goalBudgetLimit}
                        onChange={(event) => setGoalBudgetLimit(event.target.value)}
                        disabled={isConfirmingGoal}
                      />
                    </span>
                  </label>
                  <button
                    type="button"
                    className="ink-button"
                    onClick={() => void handleConfirmGoal()}
                    disabled={isConfirmingGoal}
                  >
                    {isConfirmingGoal ? '正在确认' : '确认并执行'}
                  </button>
                </div>
                {goalConfirmError && <div className="agent-goal-confirm-error">{goalConfirmError}</div>}
              </section>
            )}
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
