import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type {
  AgentPendingConfirmation,
  AgentSseEvent,
  AgentToolCall,
  AgentToolExecutionSnapshot,
  AgentToolSchema,
  NovelAgentPlanStep,
  NovelAgentRun,
} from '../api/types';

interface AgentResumeState {
  pendingToolCall?: AgentToolCall | null;
  pendingConfirmation?: AgentPendingConfirmation | null;
  discoveredPhase?: string | null;
  discoveredTools: AgentToolSchema[];
  toolSearchCacheVersion?: string | null;
  lastToolSearchAt?: string | null;
  toolSearchCacheFresh: boolean;
  toolSearchCacheSource?: string | null;
  recentToolExecutions: AgentToolExecutionSnapshot[];
}

interface AgentState {
  sessionId: string;
  activeRun: NovelAgentRun | null;
  selectedStep: NovelAgentPlanStep | null;
  sseEvents: AgentSseEvent[];
  isConnected: boolean;
  resumeState: AgentResumeState;

  setSessionId: (id: string) => void;
  setActiveRun: (run: NovelAgentRun | null) => void;
  setSelectedStep: (step: NovelAgentPlanStep | null) => void;
  addSseEvent: (evt: AgentSseEvent) => void;
  setConnected: (connected: boolean) => void;
  setResumeState: (state: AgentResumeState) => void;
  updateStepInRun: (runId: string, stepId: string, status: NovelAgentPlanStep['status']) => void;
  clearEvents: () => void;
  clearResumeState: () => void;
}

const emptyResumeState: AgentResumeState = {
  pendingToolCall: null,
  pendingConfirmation: null,
  discoveredPhase: null,
  discoveredTools: [],
  toolSearchCacheVersion: null,
  lastToolSearchAt: null,
  toolSearchCacheFresh: false,
  toolSearchCacheSource: null,
  recentToolExecutions: [],
};

export const useAgentStore = create<AgentState>()(
  persist(
    (set) => ({
      sessionId: '',
      activeRun: null,
      selectedStep: null,
      sseEvents: [],
      isConnected: false,
      resumeState: emptyResumeState,

      setSessionId: (id) => set({ sessionId: id }),
      setActiveRun: (run) => set({ activeRun: run }),
      setSelectedStep: (step) => set({ selectedStep: step }),
      addSseEvent: (evt) =>
        set((state) => ({
          sseEvents: [...state.sseEvents, evt].slice(-200),
        })),
      setConnected: (connected) => set({ isConnected: connected }),
      setResumeState: (state) => set({ resumeState: state }),
      updateStepInRun: (runId, stepId, status) =>
        set((state) => {
          if (!state.activeRun || state.activeRun.runId !== runId) return state;
          const steps = state.activeRun.steps.map((s) =>
            s.id === stepId ? { ...s, status } : s
          );
          return { activeRun: { ...state.activeRun, steps } };
        }),
      clearEvents: () => set({ sseEvents: [] }),
      clearResumeState: () => set({ resumeState: emptyResumeState }),
    }),
    {
      name: 'agent-storage',
      partialize: (state) => ({ sessionId: state.sessionId }),
    }
  )
);
