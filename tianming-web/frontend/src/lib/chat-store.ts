import { useSyncExternalStore } from 'react';
import type {
  AgentConversationTurnView,
  AgentDecisionTrace,
  AgentKnowledgeContext,
  AgentMemoryAuditSummary,
  AgentRagContext,
  AgentRuntimeStep,
  AgentWorkingMemorySnapshot,
} from '@/api/types';

export interface ChatMessage {
  id: string;
  role: 'user' | 'agent';
  content: string;
  suggestions?: string[];
  runId?: string;
  phase?: string;
  decision?: AgentDecisionTrace | null;
  rag?: AgentRagContext | null;
  memory?: AgentWorkingMemorySnapshot | null;
  memoryAudit?: AgentMemoryAuditSummary | null;
  runtimeTrace?: AgentRuntimeStep[] | null;
  knowledge?: AgentKnowledgeContext | null;
  timestamp: Date;
}

interface ChatState {
  messages: ChatMessage[];
  messagesBySession: Record<string, ChatMessage[]>;
  isSending: boolean;
}

const welcomeMessage: ChatMessage = {
  id: 'welcome',
  role: 'agent',
  content:
    '你好！我是天命小说 Agent。告诉我你想写什么类型的故事，我来帮你从构思到成稿全程驱动。\n\n你可以直接说：「我想写一本玄幻小说，主角能听见世界规则的裂纹」',
  timestamp: new Date(),
};

let state: ChatState = {
  messages: [welcomeMessage],
  messagesBySession: {},
  isSending: false,
};

const listeners = new Set<() => void>();

function setState(next: Partial<ChatState>) {
  state = { ...state, ...next };
  for (const listener of [...listeners]) {
    listener();
  }
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

let localMessageSequence = 0;

function createLocalMessageId(role: ChatMessage['role']) {
  localMessageSequence += 1;
  return `${role}-${Date.now()}-${localMessageSequence}`;
}

function mapTurn(
  turn: AgentConversationTurnView,
  index: number,
  memory?: AgentWorkingMemorySnapshot | null,
  isLastAgent = false,
): ChatMessage {
  const stableTurnId = turn.turnId?.trim()
    || `${turn.role}-${turn.turnIndex ?? index}-${turn.createdAt}`;
  return {
    id: stableTurnId,
    role: turn.role === 'user' ? 'user' : 'agent',
    content: turn.content,
    knowledge: turn.knowledge,
    memory: isLastAgent ? memory : undefined,
    timestamp: new Date(turn.createdAt),
  };
}

export const chatActions = {
  loadSessionMessages(
    sessionId: string,
    turns: AgentConversationTurnView[],
    memory?: AgentWorkingMemorySnapshot | null,
  ) {
    if (!turns || turns.length === 0) {
      const messages = [welcomeMessage];
      setState({
        messages,
        messagesBySession: { ...state.messagesBySession, [sessionId]: messages },
      });
      return;
    }
    const lastAgentIndex = turns.reduce((last, turn, index) => (turn.role === 'assistant' ? index : last), -1);
    const messages = turns.map((turn, index) => mapTurn(turn, index, memory, index === lastAgentIndex));
    const existing = state.messagesBySession[sessionId] ?? [];
    if (existing.length > messages.length) {
      setState({
        messages: existing,
        messagesBySession: { ...state.messagesBySession, [sessionId]: existing },
      });
      return;
    }
    setState({
      messages,
      messagesBySession: { ...state.messagesBySession, [sessionId]: messages },
    });
  },

  setCurrentSessionMessages(sessionId: string) {
    setState({
      messages: state.messagesBySession[sessionId] ?? [welcomeMessage],
    });
  },

  addUserMessage(sessionId: string, content: string, id?: string): string {
    const messageId = id || createLocalMessageId('user');
    const message: ChatMessage = {
      id: messageId,
      role: 'user',
      content,
      timestamp: new Date(),
    };
    const sessionMessages = state.messagesBySession[sessionId] ?? [];
    setState({
      messages: [...state.messages, message],
      messagesBySession: {
        ...state.messagesBySession,
        [sessionId]: [...sessionMessages, message],
      },
    });
    return messageId;
  },

  addAgentMessage(
    sessionId: string,
    content: string,
    suggestions?: string[],
    runId?: string,
    phase?: string,
    decision?: AgentDecisionTrace | null,
    rag?: AgentRagContext | null,
    memory?: AgentWorkingMemorySnapshot | null,
    runtimeTrace?: AgentRuntimeStep[] | null,
    memoryAudit?: AgentMemoryAuditSummary | null,
    knowledge?: AgentKnowledgeContext | null,
  ) {
    const message: ChatMessage = {
      id: createLocalMessageId('agent'),
      role: 'agent',
      content,
      suggestions,
      runId,
      phase,
      decision,
      rag,
      memory,
      runtimeTrace,
      memoryAudit,
      knowledge,
      timestamp: new Date(),
    };
    const sessionMessages = state.messagesBySession[sessionId] ?? [];
    setState({
      messages: [...state.messages, message],
      messagesBySession: {
        ...state.messagesBySession,
        [sessionId]: [...sessionMessages, message],
      },
    });
  },

  setSending(sending: boolean) {
    setState({ isSending: sending });
  },
};

export function useChatState(): ChatState {
  return useSyncExternalStore(subscribe, () => state);
}

export { welcomeMessage };
