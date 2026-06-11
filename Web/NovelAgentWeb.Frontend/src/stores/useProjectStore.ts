import { create } from 'zustand';

interface ProjectState {
  currentProjectId: string | null;
  setCurrentProject: (id: string | null) => void;
  initializeFromStorage: () => void;
}

export const useProjectStore = create<ProjectState>((set) => ({
  currentProjectId: null,

  setCurrentProject: (id) => {
    set({ currentProjectId: id });
    if (id) {
      sessionStorage.setItem('currentProjectId', id);
    } else {
      sessionStorage.removeItem('currentProjectId');
    }
  },

  initializeFromStorage: () => {
    const stored = sessionStorage.getItem('currentProjectId');
    if (stored) set({ currentProjectId: stored });
  },
}));
