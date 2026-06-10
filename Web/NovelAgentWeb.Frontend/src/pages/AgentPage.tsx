import { useCallback, useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  createAgentSession,
  createSseConnection,
  getAgentSession,
  listAgentSessions,
  sendChat,
  updateAgentSession,
} from '../api';
import type {
  AgentChatResponse,
  AgentRuntimeStep,
  AgentSessionSummary,
  AgentSseEvent,
  NovelAgentRun,
} from '../api/types';
import { useAgentStore } from '../stores/useAgentStore';
import { useAppStore } from '../stores/useAppStore';
import { useChatStore } from '../stores/useChatStore';
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
  if (session.memory?.mission?.pendingUserDecision) return session.memory.mission.pendingUserDecision;
  if (session.memory?.mission?.currentGoal) return session.memory.mission.currentGoal;
  if (session.memory?.currentGoal) return session.memory.currentGoal;
  if (session.memory?.lastIntent) return session.memory.lastIntent;
  return session.phase || '新会话';
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
  return value ? labels[value] ?? value : '';
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

function runtimeStageLabel(value?: string) {
  const labels: Record<string, string> = {
    observe: '观察上下文',
    plan: '计划动作',
    act: '执行工具',
    reflect: '反思结果',
    final: '生成回复',
    agent_observing: '观察上下文',
    agent_planning: '计划动作',
    agent_acting: '执行工具',
    agent_reflecting: '反思结果',
    conversation_intent: '识别意图',
    mission_observe: '任务黑板',
    domain_plan: '领域规划',
    policy_check: '策略检查',
    policy_replacement: '策略改写',
    tool_execute: '执行工具',
    reflect_quality: '质量裁判',
    mission_patch: '更新黑板',
  };
  return value ? labels[value] ?? value : '';
}

function runtimeStepSummary(step: AgentRuntimeStep) {
  if (step.action?.toolCall?.name) return step.action.toolCall.name;
  if (step.action?.brief) return step.action.brief;
  if (step.reflection?.summary) return step.reflection.summary;
  if (step.observation?.artifact?.summary) return step.observation.artifact.summary;
  if (step.observation?.message) return step.observation.message;
  return step.stopReason || 'Runtime step';
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
  const chatEndRef = useRef<HTMLDivElement>(null);

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
  const {
    sessionId,
    setSessionId,
    setActiveRun,
    addSseEvent,
    setConnected,
    clearEvents,
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
    queryClient.invalidateQueries({ queryKey: ['novelLibrary'] });
    queryClient.invalidateQueries({ queryKey: ['storyBible'] });
    queryClient.invalidateQueries({ queryKey: ['runs'] });
  }, [queryClient]);

  const selectSession = useCallback(async (id: string) => {
    try {
      setSessionLoadError('');
      const detail = await getAgentSession(id);
      setSessionId(detail.sessionId);
      setActiveRun(null);
      clearEvents();
      loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '会话详情加载失败');
    }
  }, [clearEvents, loadSessionMessages, setActiveRun, setSessionId]);

  const createNewSession = useCallback(async () => {
    try {
      setSessionLoadError('');
      setSessionMenu(null);
      const detail = await createAgentSession();
      setSessionId(detail.sessionId);
      setActiveRun(null);
      clearEvents();
      loadSessionMessages(detail.sessionId, detail.messages, detail.memory);
      await reloadSessions();
    } catch (err) {
      setSessionLoadError(err instanceof Error ? err.message : '新建会话失败');
    }
  }, [clearEvents, loadSessionMessages, reloadSessions, setActiveRun, setSessionId]);

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
    switch (evt.type) {
      case 'mission_updated':
      case 'confirmation_required':
        invalidateAgentState();
        break;
      case 'run_created':
      case 'run_update':
        if (evt.data) {
          const run = evt.data as NovelAgentRun;
          setActiveRun(run);
        }
        invalidateAgentState();
        break;
      case 'step_complete':
      case 'step_fail':
        invalidateAgentState();
        break;
      case 'agent_reply':
        if (evt.message) addAgentMessage(evt.sessionId || sessionId, evt.message);
        break;
    }
  }, [addAgentMessage, invalidateAgentState, sessionId, setActiveRun]);

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
  }, [messages]);

  const submitMessage = async (message: string) => {
    const msg = message.trim();
    if (!msg || isSending || !sessionId) {
      console.log('submitMessage blocked:', { msg: !!msg, isSending, sessionId });
      return;
    }

    setInput('');
    addUserMessage(sessionId, msg);
    setSending(true);
    addLog(`用户: ${msg}`);

    try {
      const res: AgentChatResponse = await sendChat({ message: msg, sessionId });
      addAgentMessage(sessionId, res.reply, res.suggestions, res.runId ?? undefined, res.phase, res.decision, res.rag, res.memory, res.runtimeTrace);
      await reloadSessions();
      invalidateAgentState();
      addLog(`Agent: ${res.phase}`);
    } catch (err) {
      console.error('sendChat error:', err);
      addAgentMessage(sessionId, `请求失败: ${err instanceof Error ? err.message : '未知错误'}`);
    } finally {
      setSending(false);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    void submitMessage(input);
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
            {messages.map((msg) => (
              <div key={msg.id} className={`agent-message ${msg.role}`}>
                <div className="message-meta">{msg.role === 'user' ? '你' : 'Agent'}</div>
                <div className="message-content">{msg.content}</div>
                {msg.role === 'agent' && msg.memory?.mission && (
                  <div className="agent-memory-line">
                    {agentPhaseLabel(msg.memory.mission.creativePhase) && (
                      <span>{agentPhaseLabel(msg.memory.mission.creativePhase)}</span>
                    )}
                    {readinessLabel(msg.memory.mission.readiness) && msg.memory.mission.readiness !== 'unknown' && (
                      <span>{readinessLabel(msg.memory.mission.readiness)}</span>
                    )}
                    {msg.memory.mission.pendingUserDecision && (
                      <span>{msg.memory.mission.pendingUserDecision}</span>
                    )}
                    {msg.memory.missionPlan?.status && (
                      <span>{msg.memory.missionPlan.status}</span>
                    )}
                  </div>
                )}
                {msg.role === 'agent' && msg.memory?.pendingConfirmation && (
                  <div className="agent-confirmation-line">
                    <strong>待确认：{msg.memory.pendingConfirmation.toolCall?.name || '写入动作'}</strong>
                    <span>{msg.memory.pendingConfirmation.impactSummary}</span>
                  </div>
                )}
                {msg.role === 'agent' && (msg.runtimeTrace?.length ?? 0) > 0 && (
                  <div className="agent-runtime-line">
                    <strong>Agent Runtime</strong>
                    <div>
                      {msg.runtimeTrace!.slice(-5).map((step) => (
                        <span key={`${step.stepIndex}-${step.stage}-${step.createdAt}`}>
                          <i>{runtimeStageLabel(step.stage)}</i>
                          {runtimeStepSummary(step)}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ))}
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
              placeholder={sessionId ? '把你的提示词直接给 Agent，它会结合系统提示词、工程状态和工具自己决定下一步...' : '正在准备会话...'}
              rows={3}
              disabled={isSending || !sessionId}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  handleSubmit(e);
                }
              }}
            />
            <button type="submit" className="ink-button" disabled={isSending || !input.trim() || !sessionId}>
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
