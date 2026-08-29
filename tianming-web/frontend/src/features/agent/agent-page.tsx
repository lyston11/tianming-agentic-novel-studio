import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import {
  ApiError,
  confirmGoalWorkflow,
  createAgentSession,
  getLatestProjectGoalWorkflowStatus,
  getSessionActiveRuntimeRun,
  listAgentSessions,
  resumeAgentSession,
  appendNovelAgentTurn,
  updateAgentSession,
} from '@/api';
import type {
  AgentArtifactPreviewView,
  AgentChatResponse,
  AgentPendingConfirmation,
  AgentSessionResumeResponse,
  AgentSessionSummary,
  AgentSseEvent,
  DirectorTurnView,
} from '@/api/types';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Spinner } from '@/components/ui/spinner';
import { Textarea } from '@/components/ui/textarea';
import { MarkdownContent } from '@/components/shared/markdown-content';
import { chatActions, useChatState } from '@/lib/chat-store';
import { setCurrentProjectId, useProjectSelection } from '@/lib/project-store';
import {
  buildExecutionBlocksFromRuntimeEvents,
  chatItemRenderKey,
  executionBlockTitle,
  executionBlockTitleFromEvents,
  executionStatusFromEvent,
  isArtifactPreviewView,
  isNovelAgentRun,
  isTerminalRuntimeUpdate,
  isUsefulRuntimeEvent,
  latestUserMessageIdFromTurns,
  makeExecutionBlock,
  mergeExecutionBlockEvents,
  resolveExecutionBlockStatus,
  runtimeEventTerminalState,
  runtimeRunIdFromEvent,
  runtimeRunToEvent,
  runtimeSourceMessageIdFromEvent,
  shouldDisplayRuntimeEvent,
  shouldMergeExecutionBlockByAnchor,
  toRuntimeEventView,
} from '@/lib/runtime-events';
import type { ExecutionBlockView, RuntimeEventView, StreamingReplyView } from '@/lib/runtime-events';
import { useAgentRuntimeStream } from './use-agent-runtime-stream';
import { ExecutionBlockCard } from './execution-block';
import { KnowledgeContextCard } from './knowledge-context-card';
import { BookProductionStatusCard } from './book-production-status-card';
import { MoreVertical } from 'lucide-react';

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
  return '待开始';
}

interface ResumeState {
  pendingToolCall: unknown;
  pendingConfirmation: AgentPendingConfirmation | null;
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
  const [activeRuntimeRunId, setActiveRuntimeRunId] = useState<string | null>(null);
  const [executionBlocks, setExecutionBlocks] = useState<ExecutionBlockView[]>([]);
  const [streamingReply, setStreamingReply] = useState<StreamingReplyView | null>(null);
  const [clockNow, setClockNow] = useState(() => Date.now());
  const [directorProposal, setDirectorProposal] = useState<DirectorTurnView | null>(null);
  const [goalBudgetLimit, setGoalBudgetLimit] = useState('10');
  const [goalConfirmError, setGoalConfirmError] = useState('');
  const [isConfirmingGoal, setIsConfirmingGoal] = useState(false);
  const [resumeState, setResumeState] = useState<ResumeState>({ pendingToolCall: null, pendingConfirmation: null });
  const [sessionId, setSessionId] = useState('');
  const [isConnected, setIsConnected] = useState(false);
  const inFlightCountRef = useRef(0);
  const chatThreadRef = useRef<HTMLDivElement>(null);
  const chatFormRef = useRef<HTMLFormElement>(null);
  const lastResumedSessionIdRef = useRef('');
  const seenRuntimeEventIdsRef = useRef<Record<string, Set<string>>>({});
  const lastRuntimeEventIdRef = useRef<Record<string, string>>({});
  const runAnchorMessageIdsRef = useRef<Record<string, string>>({});
  const latestTurnAnchorMessageIdRef = useRef<string>('');
  const sseEventsRef = useRef<AgentSseEvent[]>([]);

  const { messages, isSending } = useChatState();
  const { currentProjectId } = useProjectSelection();

  const latestProjectGoalQuery = useQuery({
    queryKey: ['latest-project-goal-workflow', currentProjectId],
    queryFn: () => getLatestProjectGoalWorkflowStatus(currentProjectId!),
    enabled: Boolean(currentProjectId),
    refetchInterval: 30_000,
  });

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

  const resetRuntimeEventReplayState = useCallback((targetSessionId: string, events: AgentSseEvent[] = []) => {
    void events;
    seenRuntimeEventIdsRef.current[targetSessionId] = new Set();
    delete lastRuntimeEventIdRef.current[targetSessionId];
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
          chatActions.setSending(false);
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
        chatActions.setSending(false);
        void reloadSessions();
      } else {
        chatActions.setSending(true);
      }
    } catch {
      // SSE remains the primary live channel; polling failures should not break chat input.
    }
  }, [activeRuntimeRunId, appendExecutionEvent, markExecutionBlockDone, reloadSessions]);

  const applySessionResume = useCallback((detail: AgentSessionResumeResponse) => {
    const memory = mergeResumeMemory(detail);
    lastResumedSessionIdRef.current = detail.sessionId;
    setSessionId(detail.sessionId);
    setCurrentProjectId(detail.activeProjectId || null);
    setActiveRuntimeRunId(detail.activeRunId ?? null);
    resetRuntimeEventReplayState(detail.sessionId, []);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
    setExecutionBlocks(buildExecutionBlocksFromRuntimeEvents(detail.recentRuntimeEvents ?? [], latestTurnAnchorMessageIdRef.current || undefined));
    setStreamingReply(null);
    setDirectorProposal(null);
    setGoalConfirmError('');
    setResumeState({
      pendingToolCall: detail.pendingToolCall ?? null,
      pendingConfirmation: detail.pendingConfirmation ?? memory.pendingConfirmation ?? null,
    });
    chatActions.loadSessionMessages(detail.sessionId, detail.messages, memory);
  }, [resetRuntimeEventReplayState]);

  const clearMissingSession = useCallback((id: string) => {
    if (lastResumedSessionIdRef.current === id) {
      lastResumedSessionIdRef.current = '';
    }
    setSessionLoadError('');
    setSessionId('');
    setActiveRuntimeRunId(null);
    resetRuntimeEventReplayState(id, []);
    runAnchorMessageIdsRef.current = {};
    latestTurnAnchorMessageIdRef.current = '';
    setResumeState({ pendingToolCall: null, pendingConfirmation: null });
    setExecutionBlocks([]);
    setStreamingReply(null);
    setDirectorProposal(null);
    setGoalConfirmError('');
    chatActions.setCurrentSessionMessages('');
  }, [resetRuntimeEventReplayState]);

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
      const detail = await createAgentSession();
      lastResumedSessionIdRef.current = detail.sessionId;
      setSessionId(detail.sessionId);
      setCurrentProjectId(detail.activeProjectId || null);
      setActiveRuntimeRunId(detail.activeRunId ?? null);
      resetRuntimeEventReplayState(detail.sessionId, []);
      runAnchorMessageIdsRef.current = {};
      latestTurnAnchorMessageIdRef.current = latestUserMessageIdFromTurns(detail.messages) || '';
      setResumeState({ pendingToolCall: null, pendingConfirmation: null });
      setExecutionBlocks([]);
      setStreamingReply(null);
      setDirectorProposal(null);
      setGoalConfirmError('');
      chatActions.loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '新建会话失败');
    }
  }, [reloadSessions, resetRuntimeEventReplayState]);

  const renameSession = useCallback(async (target: AgentSessionSummary) => {
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
    if (!sessionId || !sessionsLoaded) return;
    if (lastResumedSessionIdRef.current === sessionId) return;
    if (!sessions.some((item) => item.sessionId === sessionId)) {
      clearMissingSession(sessionId);
      return;
    }
    lastResumedSessionIdRef.current = sessionId;
    void resumeCurrentSession(sessionId);
  }, [clearMissingSession, resumeCurrentSession, sessionId, sessions, sessionsLoaded]);

  useEffect(() => {
    if (sessionId || !sessionsLoaded) return;
    if (sessions.length > 0) {
      void selectSession(sessions[0].sessionId);
      return;
    }
    void createNewSession();
  }, [createNewSession, selectSession, sessionId, sessions, sessionsLoaded]);

  useEffect(() => {
    if (!sessionId) return;
    chatActions.setCurrentSessionMessages(sessionId);
  }, [sessionId]);

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

  const addSseEvent = useCallback((event: AgentSseEvent) => {
    sseEventsRef.current = [...sseEventsRef.current.slice(-99), event];
  }, []);

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
      case 'mission_updated':
      case 'confirmation_required':
        invalidateAgentState();
        break;
      case 'run_created':
      case 'run_update':
      case 'step_complete':
      case 'step_fail': {
        if ((evt.type === 'run_created' || evt.type === 'run_update') && eventRunId && !isTerminalRuntimeUpdate(evt.data, evt.message)) {
          setActiveRuntimeRunId(eventRunId);
        }
        if (isNovelAgentRun(evt.data)) {
          // The run payload is only used for display; execution blocks own the state.
        }
        const terminalState = runtimeEventTerminalState(evt.data, evt.message);
        if (terminalState) {
          markExecutionBlockDone(effectiveRunId, terminalState === 'failed');
          if (!eventRunId || eventRunId === activeRuntimeRunId) {
            setActiveRuntimeRunId(null);
          }
          inFlightCountRef.current = 0;
          chatActions.setSending(false);
          void reloadSessions();
        }
        if (evt.data && typeof evt.data === 'object') {
          const candidate = evt.data as { status?: string; phase?: string; activeProjectId?: string; projectId?: string };
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
            chatActions.setSending(false);
            void reloadSessions();
          }
        }
        if (evt.type === 'step_fail') {
          markExecutionBlockDone(effectiveRunId, true);
          chatActions.setSending(false);
        }
        invalidateAgentState();
        break;
      }
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
          chatActions.addAgentMessage(
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
            response?.knowledge,
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
        chatActions.setSending(false);
        void reloadSessions();
        invalidateAgentState();
        break;
      }
    }
  }, [activeRuntimeRunId, appendExecutionEvent, appendExecutionPreview, invalidateAgentState, markExecutionBlockDone, reloadSessions, rememberRuntimeEventId, sessionId]);

  useAgentRuntimeStream({
    sessionId,
    lastRuntimeEventIdRef,
    addSseEvent,
    handleSseEvent,
    setConnected: setIsConnected,
  });

  const autoScrollKey = useMemo(() => {
    const lastMessage = messages[messages.length - 1];
    const messageKey = lastMessage
      ? `${messages.length}:${lastMessage.id}:${lastMessage.timestamp.getTime()}`
      : '0';
    const streamKey = streamingReply ? `${streamingReply.id}:${streamingReply.content.length}` : '';
    return `${messageKey}::${streamKey}`;
  }, [messages, streamingReply]);

  useEffect(() => {
    const thread = chatThreadRef.current;
    if (!thread) return;
    window.requestAnimationFrame(() => {
      thread.scrollTo({ top: thread.scrollHeight, behavior: 'smooth' });
    });
  }, [autoScrollKey]);

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

  const submitMessage = async (message: string) => {
    const msg = message.trim();
    if (!msg || !sessionId) return;

    setInput('');
    const userMessageId = chatActions.addUserMessage(sessionId, msg);
    latestTurnAnchorMessageIdRef.current = userMessageId;
    const pendingBlockId = `turn-${userMessageId}`;
    setExecutionBlocks((prev) => [...prev, makeExecutionBlock(pendingBlockId, null, '正在处理请求', userMessageId)].slice(-24));
    inFlightCountRef.current += 1;
    chatActions.setSending(true);
    setStreamingReply(null);

    try {
      const res = await appendNovelAgentTurn(sessionId, {
        idempotencyKey: userMessageId,
        content: msg,
      });
      setActiveRuntimeRunId(null);
      chatActions.addAgentMessage(sessionId, res.decision.message);
      if (res.decision.proposalId) {
        appendExecutionEvent({
          id: `proposal-${res.decision.proposalId}`,
          type: 'run_update',
          title: '创作提案已生成',
          detail: `提案等待用户确认（提案 ID: ${res.decision.proposalId}）`,
          status: 'done',
          runId: null,
          timestamp: new Date(),
        }, userMessageId);
      }
      setGoalConfirmError('');
      setExecutionBlocks((prev) => prev.filter((block) => (
        block.id !== pendingBlockId || block.events.length > 0 || block.previews.length > 0
      )));
      setResumeState((prev) => ({ ...prev, pendingToolCall: null }));
      await reloadSessions();
      invalidateAgentState();
      inFlightCountRef.current = Math.max(0, inFlightCountRef.current - 1);
      chatActions.setSending(inFlightCountRef.current > 0);
    } catch (err) {
      console.error('conversation turn error:', err);
      chatActions.addAgentMessage(sessionId, `请求失败: ${err instanceof Error ? err.message : '未知错误'}`);
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
      chatActions.setSending(inFlightCountRef.current > 0);
    }
  };

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
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
      const projectId = directorProposal.projectId;
      setCurrentProjectId(null);
      setDirectorProposal(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['latest-project-goal-workflow', projectId] }),
        queryClient.invalidateQueries({ queryKey: ['projectWorkflow', projectId] }),
      ]);
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

  return (
    <div className="flex min-h-0 flex-1 gap-4">
      <aside className="flex w-64 shrink-0 flex-col rounded-xl border bg-card">
        <section className="flex items-center justify-between border-b p-3.5">
          <div>
            <p className="font-mono text-[10px] tracking-wide text-muted-foreground uppercase">Agent Sessions</p>
            <h1 className="font-serif text-base font-bold">会话历史</h1>
          </div>
          <Button size="sm" onClick={() => void createNewSession()}>
            新建
          </Button>
        </section>

        <section className="flex min-h-0 flex-1 flex-col">
          <div className="flex items-baseline justify-between border-b px-3.5 py-2 text-xs text-muted-foreground">
            <span>独立记忆</span>
            <strong>{sessionsLoaded ? `${sessions.length} 个会话` : '加载中'}</strong>
          </div>

          {sessionLoadError && (
            <div className="px-3.5 py-2 text-xs text-destructive">{sessionLoadError}</div>
          )}

          <div className="min-h-0 flex-1 space-y-0.5 overflow-y-auto p-2">
            {!sessionsLoaded ? (
              <div className="p-3 text-xs text-muted-foreground">正在读取会话历史...</div>
            ) : sessions.length === 0 ? (
              <div className="p-3 text-xs text-muted-foreground">还没有历史会话</div>
            ) : (
              sessions.map((item) => (
                <div
                  key={item.sessionId}
                  className={`group relative rounded-lg px-2.5 py-2 transition-colors ${
                    item.sessionId === sessionId ? 'bg-primary/10' : 'hover:bg-muted'
                  }`}
                >
                  <button
                    type="button"
                    className="w-full text-left"
                    onClick={() => void selectSession(item.sessionId)}
                  >
                    <strong className="block truncate text-sm" title={item.title || '新会话'}>
                      {item.title || '新会话'}
                    </strong>
                    <span className="block truncate text-[11px] text-muted-foreground">
                      {sessionPreview(item)}
                    </span>
                    <small className="mt-0.5 flex items-center justify-between text-[10px] text-muted-foreground/80">
                      <span>{item.messageCount} 条消息</span>
                      <time>{formatSessionTime(item.updatedAt)}</time>
                    </small>
                  </button>
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon-xs"
                        className="absolute top-2 right-1.5 opacity-0 group-hover:opacity-100"
                      >
                        <MoreVertical />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onClick={() => void renameSession(item)}>重命名</DropdownMenuItem>
                      <DropdownMenuItem onClick={() => void archiveSession(item)}>归档</DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              ))
            )}
          </div>
          <div className="flex items-center gap-1.5 border-t px-3.5 py-2 text-[10px] text-muted-foreground">
            <span className={`inline-block size-1.5 rounded-full ${isConnected ? 'bg-emerald-500' : 'bg-muted-foreground/40'}`} />
            {isConnected ? '实时事件已连接' : '事件流未连接'}
          </div>
        </section>
      </aside>

      <main className="agent-chat-panel flex min-w-0 flex-1 flex-col rounded-xl border bg-card">
        <div className="chat-thread min-h-0 flex-1 space-y-4 overflow-y-auto p-4" ref={chatThreadRef}>
          {resumeState.pendingConfirmation && (
            <div className="rounded-lg bg-muted/60 px-3 py-2 text-xs">
              <div className="text-[10px] tracking-wide text-muted-foreground uppercase">恢复状态</div>
              <div className="mt-0.5">
                <strong>待确认：{resumeState.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                {resumeState.pendingConfirmation.impactSummary && (
                  <span className="ml-2 text-muted-foreground">{resumeState.pendingConfirmation.impactSummary}</span>
                )}
              </div>
            </div>
          )}
          {conversationItems.map((item, index) => {
            if (item.kind === 'stream') {
              return (
                <div key={chatItemRenderKey(item.id, index)} className="max-w-[85%]">
                  <div className="text-[10px] tracking-wide text-muted-foreground uppercase">Agent</div>
                  <div className="mt-1 rounded-2xl rounded-tl-md bg-muted/60 px-3.5 py-2.5 text-sm">
                    <MarkdownContent>{item.stream.content}</MarkdownContent>
                  </div>
                </div>
              );
            }

            const msg = item.message;
            const ownedExecutionBlocks = messageExecutionBlocks[msg.id] ?? [];
            return (
              <div key={chatItemRenderKey(item.id, index)} className="space-y-1.5">
                <div className={`max-w-[85%] ${msg.role === 'user' ? 'ml-auto' : ''}`}>
                  <div className={`text-[10px] tracking-wide text-muted-foreground uppercase ${msg.role === 'user' ? 'text-right' : ''}`}>
                    {msg.role === 'user' ? '你' : 'Agent'}
                  </div>
                  <div
                    className={`mt-1 rounded-2xl px-3.5 py-2.5 text-sm ${
                      msg.role === 'user'
                        ? 'rounded-tr-md bg-primary text-primary-foreground'
                        : 'rounded-tl-md bg-muted/60'
                    }`}
                  >
                    {msg.role === 'user' ? (
                      <div className="whitespace-pre-wrap">{msg.content}</div>
                    ) : (
                      <MarkdownContent>{msg.content}</MarkdownContent>
                    )}
                  </div>
                  {msg.role === 'agent' && msg.memory?.pendingConfirmation && (
                    <div className="mt-1.5 rounded-lg bg-gold/10 px-3 py-2 text-xs">
                      <strong>待确认：{msg.memory.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                    </div>
                  )}
                </div>
                {msg.role === 'agent' && msg.knowledge && (
                  <KnowledgeContextCard context={msg.knowledge} />
                )}
                {ownedExecutionBlocks.length > 0 && (
                  <div className="space-y-2" aria-label="本轮后台执行">
                    {ownedExecutionBlocks.map((block) => (
                      <ExecutionBlockCard
                        key={block.id}
                        block={block}
                        clockNow={clockNow}
                        onToggle={toggleExecutionBlock}
                      />
                    ))}
                  </div>
                )}
              </div>
            );
          })}
          {directorProposal?.proposedContract && (
            <section className="rounded-xl border border-primary/40 bg-card p-4" aria-label="Creative Goal 提案">
              <header className="flex items-start justify-between gap-2">
                <div>
                  <span className="font-mono text-[10px] tracking-wide text-muted-foreground uppercase">Creative Goal</span>
                  <strong className="block font-serif text-sm">{directorProposal.proposedContract.humanReadableObjective}</strong>
                </div>
                <small className="text-xs text-muted-foreground">
                  {directorProposal.proposedContract.executionStrategy === 'full_auto' ? '整书自动推进' : '分批交互推进'}
                </small>
              </header>
              {directorProposal.rationale && (
                <p className="mt-2 text-xs text-muted-foreground">{directorProposal.rationale}</p>
              )}
              <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3">
                {[
                  { label: '章节范围', value: directorProposal.proposedContract.targetChapterRangeJson },
                  { label: '成功条件', value: `${directorProposal.proposedContract.successCriteria.length} 项` },
                  { label: '必须保留', value: `${directorProposal.proposedContract.mustPreserve.length} 项` },
                ].map((item) => (
                  <div key={item.label} className="rounded-lg bg-muted/50 px-2.5 py-2">
                    <span className="block text-[10px] text-muted-foreground">{item.label}</span>
                    <strong className="block truncate text-xs">{item.value}</strong>
                  </div>
                ))}
              </div>
              <div className="mt-3 flex items-end gap-2">
                <label className="text-xs">
                  <span className="mb-1 block text-muted-foreground">金额上限（USD）</span>
                  <span className="flex items-center gap-1 rounded-lg border px-2">
                    <b>$</b>
                    <input
                      type="number"
                      className="w-24 bg-transparent py-1.5 outline-none"
                      min="0.01"
                      step="0.01"
                      value={goalBudgetLimit}
                      onChange={(event) => setGoalBudgetLimit(event.target.value)}
                      disabled={isConfirmingGoal}
                    />
                  </span>
                </label>
                <Button size="sm" disabled={isConfirmingGoal} onClick={() => void handleConfirmGoal()}>
                  {isConfirmingGoal ? '正在确认' : '确认并执行'}
                </Button>
              </div>
              {goalConfirmError && <div className="mt-2 text-xs text-destructive">{goalConfirmError}</div>}
            </section>
          )}
          {!directorProposal?.proposedContract && latestProjectGoalQuery.data && currentProjectId && (
            <BookProductionStatusCard
              workflow={latestProjectGoalQuery.data}
              context="chat"
              onOpenWorkflow={() => navigate(`/workflow/${encodeURIComponent(currentProjectId)}`)}
            />
          )}
          {isSending && !hasRunningExecutionBlock && !streamingReply && (
            <div className="max-w-[85%]">
              <div className="text-[10px] tracking-wide text-muted-foreground uppercase">Agent</div>
              <div className="mt-1 flex items-center gap-1.5 rounded-2xl rounded-tl-md bg-muted/60 px-4 py-3">
                <span className="size-1.5 animate-bounce rounded-full bg-muted-foreground/60 [animation-delay:0ms]" />
                <span className="size-1.5 animate-bounce rounded-full bg-muted-foreground/60 [animation-delay:150ms]" />
                <span className="size-1.5 animate-bounce rounded-full bg-muted-foreground/60 [animation-delay:300ms]" />
              </div>
            </div>
          )}
        </div>

        <form className="flex items-end gap-2 border-t p-3" ref={chatFormRef} onSubmit={handleSubmit}>
          <Textarea
            value={input}
            onChange={(event) => setInput(event.target.value)}
            placeholder={sessionId
              ? (isSending
                ? 'Agent 正在执行中，你可以继续追问进度、补充要求或要求暂停...'
                : '把你的提示词直接给 Agent，它会结合系统提示词、工程状态和工具自己决定下一步...')
              : '正在准备会话...'}
            rows={3}
            disabled={!sessionId}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                handleSubmit(event);
              }
            }}
          />
          <Button type="submit" disabled={!input.trim() || !sessionId || isSending}>
            {isSending && <Spinner />}
            发送
          </Button>
        </form>
      </main>
    </div>
  );
}
