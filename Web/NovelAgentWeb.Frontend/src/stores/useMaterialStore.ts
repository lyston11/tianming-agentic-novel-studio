import { create } from 'zustand';
import type { MaterialAnalysisProgress, CreativeKnowledgeCategory } from '../api/types';

interface MaterialState {
  activeAnalysisId: string | null;
  analysisStages: MaterialAnalysisProgress[];
  currentStage: MaterialAnalysisProgress | null;
  isAnalyzing: boolean;

  selectedCategory: CreativeKnowledgeCategory | 'All';
  searchQuery: string;

  startAnalysis: (materialId: string) => void;
  updateProgress: (progress: MaterialAnalysisProgress) => void;
  completeAnalysis: () => void;
  setSelectedCategory: (cat: CreativeKnowledgeCategory | 'All') => void;
  setSearchQuery: (q: string) => void;
}

export const useMaterialStore = create<MaterialState>((set) => ({
  activeAnalysisId: null,
  analysisStages: [],
  currentStage: null,
  isAnalyzing: false,
  selectedCategory: 'All',
  searchQuery: '',

  startAnalysis: (materialId) =>
    set({
      activeAnalysisId: materialId,
      analysisStages: [],
      currentStage: null,
      isAnalyzing: true,
    }),

  updateProgress: (progress) =>
    set((state) => {
      const stages = [...state.analysisStages];
      const existingIdx = stages.findIndex((s) => s.stage === progress.stage);
      if (existingIdx >= 0) stages[existingIdx] = progress;
      else stages.push(progress);
      return { analysisStages: stages, currentStage: progress };
    }),

  completeAnalysis: () =>
    set({ isAnalyzing: false, currentStage: null, activeAnalysisId: null }),

  setSelectedCategory: (cat) => set({ selectedCategory: cat }),
  setSearchQuery: (q) => set({ searchQuery: q }),
}));
