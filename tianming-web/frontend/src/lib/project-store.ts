import { useSyncExternalStore } from 'react';
import type { NovelProjectInfo } from '@/api/types';

/**
 * Module-level "current project" selection state (learngraph pattern).
 * Mirrors the legacy zustand store semantics, including the sessionStorage
 * persistence key, so existing sessions keep their selected project.
 */

interface ProjectState {
  currentProjectId: string | null;
  currentProject: NovelProjectInfo | null;
}

const PROJECT_ID_KEY = 'currentProjectId';

let state: ProjectState = { currentProjectId: null, currentProject: null };
const listeners = new Set<() => void>();

function persistProject(project: NovelProjectInfo | null) {
  if (project) {
    sessionStorage.setItem(PROJECT_ID_KEY, project.id);
  } else {
    sessionStorage.removeItem(PROJECT_ID_KEY);
  }
}

function setState(next: ProjectState) {
  state = next;
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

export function setCurrentProject(project: NovelProjectInfo | null) {
  setState({ currentProjectId: project?.id ?? null, currentProject: project });
  persistProject(project);
}

export function setCurrentProjectId(id: string | null) {
  const currentProject = state.currentProject?.id === id ? state.currentProject : null;
  setState({ currentProjectId: id, currentProject });
  if (id) {
    sessionStorage.setItem(PROJECT_ID_KEY, id);
  } else {
    sessionStorage.removeItem(PROJECT_ID_KEY);
  }
}

export function ensureProjectSelected(projects: NovelProjectInfo[]) {
  if (projects.length === 0) {
    setCurrentProject(null);
    return;
  }

  const currentId = state.currentProjectId;
  const current = currentId ? projects.find((project) => project.id === currentId) : null;
  setCurrentProject(current ?? projects[0]);
}

export function initializeProjectFromStorage() {
  const storedId = sessionStorage.getItem(PROJECT_ID_KEY);
  if (!storedId) return;
  setState({ currentProjectId: storedId, currentProject: null });
}

export function useProjectSelection(): ProjectState {
  return useSyncExternalStore(subscribe, () => state);
}
