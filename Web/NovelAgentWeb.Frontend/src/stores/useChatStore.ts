import { create } from 'zustand';
import type {
  AgentConversationTurnView,
  AgentDecisionTrace,
  AgentMemoryAuditSummary,
  AgentRagContext,
  AgentRuntimeStep,
  AgentWorkingMemorySnapshot,
} from '../api/types';

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
  timestamp: Date;
}

interface ChatState {
  messages: ChatMessage[];
  messagesBySession: Record<string, ChatMessage[]>;
  isSending: boolean;

  loadSessionMessages: (sessionId: string, turns: AgentConversationTurnView[], memory?: AgentWorkingMemorySnapshot | null) => void;
  setCurrentSessionMessages: (sessionId: string) => void;
  addUserMessage: (sessionId: string, content: string, id?: string) => string;
  addAgentMessage: (
    sessionId: string,
    content: string,
    suggestions?: string[],
    runId?: string,
    phase?: string,
    decision?: AgentDecisionTrace | null,
    rag?: AgentRagContext | null,
    memory?: AgentWorkingMemorySnapshot | null,
    runtimeTrace?: AgentRuntimeStep[] | null,
    memoryAudit?: AgentMemoryAuditSummary | null
  ) => void;
  setSending: (sending: boolean) => void;
  clearMessages: () => void;
}

const welcomeMessage: ChatMessage = {
  id: 'welcome',
  role: 'agent',
  content:
    '你好！我是天命小说 Agent。告诉我你想写什么类型的故事，我来帮你从构思到成稿全程驱动。\n\n你可以直接说：「我想写一本玄幻小说，主角能听见世界规则的裂纹」',
  timestamp: new Date(),
};

let localMessageSequence = 0;

function createLocalMessageId(role: ChatMessage['role']) {
  localMessageSequence += 1;
  return `${role}-${Date.now()}-${localMessageSequence}`;
}

function mapTurn(turn: AgentConversationTurnView, index: number, memory?: AgentWorkingMemorySnapshot | null, isLastAgent = false): ChatMessage {
  const stableTurnId = turn.turnId?.trim()
    || `${turn.role}-${turn.turnIndex ?? index}-${turn.createdAt}`;
  return {
    id: stableTurnId,
    role: turn.role === 'user' ? 'user' : 'agent',
    content: turn.content,
    memory: isLastAgent ? memory : undefined,
    timestamp: new Date(turn.createdAt),
  };
}

export const useChatStore = create<ChatState>((set) => ({
  messages: [welcomeMessage],
  messagesBySession: {},
  isSending: false,

  loadSessionMessages: (sessionId, turns, memory) =>
    set((state) => {
      if (!turns || turns.length === 0) {
        const msgs = [welcomeMessage];
        return {
          messages: msgs,
          messagesBySession: { ...state.messagesBySession, [sessionId]: msgs },
        };
      }
      const lastAgentIndex = turns.reduce((last, turn, index) => turn.role === 'assistant' ? index : last, -1);
      const messages = turns.map((turn, index) => mapTurn(turn, index, memory, index === lastAgentIndex));
      const existing = state.messagesBySession[sessionId] ?? [];
      if (existing.length > messages.length) {
        return {
          messages: existing,
          messagesBySession: { ...state.messagesBySession, [sessionId]: existing },
        };
      }
      return {
        messages,
        messagesBySession: { ...state.messagesBySession, [sessionId]: messages },
      };
    }),

  setCurrentSessionMessages: (sessionId) =>
    set((state) => ({
      messages: state.messagesBySession[sessionId] ?? [welcomeMessage],
    })),

  addUserMessage: (sessionId, content, id) => {
    const messageId = id || createLocalMessageId('user');
    set((state) => {
      const message: ChatMessage = {
        id: messageId,
        role: 'user',
        content,
        timestamp: new Date(),
      };
      const sessionMessages = state.messagesBySession[sessionId] ?? [];
      return {
        messages: [...state.messages, message],
        messagesBySession: {
          ...state.messagesBySession,
          [sessionId]: [...sessionMessages, message],
        },
      };
    });
    return messageId;
  },

  addAgentMessage: (sessionId, content, suggestions, runId, phase, decision, rag, memory, runtimeTrace, memoryAudit) =>
    set((state) => {
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
        memoryAudit,
        runtimeTrace,
        timestamp: new Date(),
      };
      const sessionMessages = state.messagesBySession[sessionId] ?? [];
      return {
        messages: [...state.messages, message],
        messagesBySession: {
          ...state.messagesBySession,
          [sessionId]: [...sessionMessages, message],
        },
      };
    }),

  setSending: (sending) => set({ isSending: sending }),
  clearMessages: () => set({ messages: [welcomeMessage] }),
}));
