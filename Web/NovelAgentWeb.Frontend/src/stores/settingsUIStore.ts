import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface SettingsUIState {
  activeTab: 'account' | 'ai' | 'creative' | 'ui';
  setActiveTab: (tab: 'account' | 'ai' | 'creative' | 'ui') => void;
}

export const useSettingsUIStore = create<SettingsUIState>()(
  persist(
    (set) => ({
      activeTab: 'account',
      setActiveTab: (tab) => set({ activeTab: tab }),
    }),
    {
      name: 'settings-ui-storage',
    }
  )
);
