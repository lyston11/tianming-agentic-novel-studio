import { create } from 'zustand';

export type WorkflowStage = 'foundation' | 'volume' | 'chapter';

interface AppState {
  activeWorkflowStage: WorkflowStage;
  selectedMacroTitle: string;
  selectedCandidateTitle: string;
  eventLog: string[];

  setWorkflowStage: (stage: WorkflowStage) => void;
  setSelectedMacroTitle: (title: string) => void;
  setSelectedCandidateTitle: (title: string) => void;
  addLog: (message: string) => void;
  clearLog: () => void;
}

export const useAppStore = create<AppState>((set) => ({
  activeWorkflowStage: 'foundation',
  selectedMacroTitle: '',
  selectedCandidateTitle: '',
  eventLog: [],

  setWorkflowStage: (stage) => set({ activeWorkflowStage: stage }),
  setSelectedMacroTitle: (title) => set({ selectedMacroTitle: title }),
  setSelectedCandidateTitle: (title) => set({ selectedCandidateTitle: title }),
  addLog: (message) =>
    set((state) => ({
      eventLog: [`[${new Date().toLocaleTimeString()}] ${message}`, ...state.eventLog].slice(0, 100),
    })),
  clearLog: () => set({ eventLog: [] }),
}));
