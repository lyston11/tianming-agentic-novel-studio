import { create } from 'zustand';
import type { NovelProjectInfo } from '../api/types';

interface ProjectState {
  currentProjectId: string | null;
  currentProject: NovelProjectInfo | null;
  setCurrentProject: (project: NovelProjectInfo | null) => void;
  setCurrentProjectId: (id: string | null) => void;
  ensureProjectSelected: (projects: NovelProjectInfo[]) => void;
  initializeFromStorage: () => void;
}

const PROJECT_ID_KEY = 'currentProjectId';

function persistProject(project: NovelProjectInfo | null) {
  if (project) {
    sessionStorage.setItem(PROJECT_ID_KEY, project.id);
  } else {
    sessionStorage.removeItem(PROJECT_ID_KEY);
  }
}

export const useProjectStore = create<ProjectState>((set, get) => ({
  currentProjectId: null,
  currentProject: null,

  setCurrentProject: (project) => {
    set({ currentProjectId: project?.id ?? null, currentProject: project });
    persistProject(project);
  },

  setCurrentProjectId: (id) => {
    const current = get().currentProject;
    const currentProject = current?.id === id ? current : null;
    set({ currentProjectId: id, currentProject });
    if (id) {
      sessionStorage.setItem(PROJECT_ID_KEY, id);
    } else {
      sessionStorage.removeItem(PROJECT_ID_KEY);
    }
  },

  ensureProjectSelected: (projects) => {
    if (projects.length === 0) {
      get().setCurrentProject(null);
      return;
    }

    const currentId = get().currentProjectId;
    const current = currentId ? projects.find((project) => project.id === currentId) : null;
    get().setCurrentProject(current ?? projects[0]);
  },

  initializeFromStorage: () => {
    const storedId = sessionStorage.getItem(PROJECT_ID_KEY);
    if (!storedId) return;
    set({ currentProjectId: storedId, currentProject: null });
  },
}));
