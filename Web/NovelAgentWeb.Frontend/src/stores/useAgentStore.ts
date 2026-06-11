import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AgentSseEvent, NovelAgentRun, NovelAgentPlanStep } from '../api/types';

interface AgentState {
  sessionId: string;
  activeRun: NovelAgentRun | null;
  selectedStep: NovelAgentPlanStep | null;
  sseEvents: AgentSseEvent[];
  isConnected: boolean;

  setSessionId: (id: string) => void;
  setActiveRun: (run: NovelAgentRun | null) => void;
  setSelectedStep: (step: NovelAgentPlanStep | null) => void;
  addSseEvent: (evt: AgentSseEvent) => void;
  setConnected: (connected: boolean) => void;
  updateStepInRun: (runId: string, stepId: string, status: NovelAgentPlanStep['status']) => void;
  clearEvents: () => void;
}

export const useAgentStore = create<AgentState>()(
  persist(
    (set) => ({
      sessionId: '',
      activeRun: null,
      selectedStep: null,
      sseEvents: [],
      isConnected: false,

      setSessionId: (id) => set({ sessionId: id }),
  setActiveRun: (run) => set({ activeRun: run }),
  setSelectedStep: (step) => set({ selectedStep: step }),
  addSseEvent: (evt) =>
    set((state) => ({
      sseEvents: [...state.sseEvents, evt].slice(-200),
    })),
  setConnected: (connected) => set({ isConnected: connected }),
  updateStepInRun: (runId, stepId, status) =>
    set((state) => {
      if (!state.activeRun || state.activeRun.runId !== runId) return state;
      const steps = state.activeRun.steps.map((s) =>
        s.id === stepId ? { ...s, status } : s
      );
      return { activeRun: { ...state.activeRun, steps } };
    }),
  clearEvents: () => set({ sseEvents: [] }),
    }),
    {
      name: 'agent-storage',
      partialize: (state) => ({ sessionId: state.sessionId }),
    }
  )
);
