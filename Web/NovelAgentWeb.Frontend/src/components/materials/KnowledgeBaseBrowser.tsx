import { useEffect, useMemo, useRef, useState } from 'react';
import type { MouseEvent, ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createKnowledgeDirectory,
  createKnowledgeEntry,
  deleteKnowledgeDirectory,
  deleteKnowledgeEntryById,
  listKnowledgeDirectories,
  listKnowledgeEntries,
  updateKnowledgeDirectory,
  updateKnowledgeEntryById,
} from '../../api';
import type {
  CreativeKnowledgeEntry,
  KnowledgeConstraintEvidence,
  KnowledgeDirectoryResponse,
  KnowledgeProjectUsage,
  KnowledgeResponse,
} from '../../api/types';
import { projectService } from '../../services/projectService';
import { useProjectStore } from '../../stores/useProjectStore';

type KnowledgeDirectoryKey = 'All' | string;
type KnowledgeEntryView = Omit<CreativeKnowledgeEntry, 'category'> & { category: string };
type EntryDragDraft = {
  entry: KnowledgeEntryView;
  startX: number;
  startY: number;
  x: number;
  y: number;
  active: boolean;
};
type DirectoryDialogState = {
  mode: 'create' | 'rename';
  directory?: KnowledgeDirectoryResponse;
};
type EntryDialogState = {
  mode: 'rename' | 'archive' | 'delete';
  entry: KnowledgeEntryView;
};

const ALL_DIRECTORY: KnowledgeDirectoryResponse = {
  key: 'All',
  name: '全部',
  description: '查看全部知识条目',
  isSystem: true,
  entryCount: 0,
};

const ENTRY_DRAG_THRESHOLD = 12;

const SYSTEM_DIRECTORY_META: Record<string, { name: string; description: string }> = {
  GenrePrinciple: { name: '题材原则', description: '类型承诺和读者预期' },
  TropePattern: { name: '套路模式', description: '高频套路和风险桥段' },
  AntiTropeStrategy: { name: '反套路', description: '变体、反转和规避策略' },
  StyleExample: { name: '风格示例', description: '叙述风格、对话技巧和节奏样例' },
  HardFact: { name: '硬事实', description: '角色、道具、组织、地点和规则等连续性事实' },
  ReaderPromise: { name: '读者承诺', description: '爽点、情绪和持续期待' },
  ThemeDepth: { name: '主题深度', description: '主题母题和深层表达' },
  EmotionArc: { name: '情绪线', description: '情绪推进和转折节奏' },
  RelationshipDynamic: { name: '关系动态', description: '人物关系张力' },
  ProjectUsedPattern: { name: '项目记忆', description: '项目已用桥段记忆' },
  Uncategorized: { name: '未分类', description: '尚未归入目录的知识条目' },
};

const DIRECTORY_COLORS: Record<string, string> = {
  All: 'var(--gold)',
  GenrePrinciple: 'var(--gold)',
  TropePattern: 'var(--red)',
  AntiTropeStrategy: 'var(--jade)',
  StyleExample: 'var(--blue)',
  HardFact: 'var(--gold)',
  ReaderPromise: 'var(--blue)',
  ThemeDepth: 'var(--gold)',
  EmotionArc: 'var(--red)',
  RelationshipDynamic: 'var(--jade)',
  ProjectUsedPattern: 'var(--muted)',
  Uncategorized: 'var(--muted)',
};

const LEGACY_CATEGORY_ALIASES: Record<string, string> = {
  genre_rule: 'GenrePrinciple',
  trope_pattern: 'TropePattern',
  anti_trope: 'AntiTropeStrategy',
  style_example: 'StyleExample',
  hard_fact: 'HardFact',
  reader_promise: 'ReaderPromise',
  theme_depth: 'ThemeDepth',
  emotion_arc: 'EmotionArc',
  relationship_dynamic: 'RelationshipDynamic',
  project_pattern: 'ProjectUsedPattern',
  project_used_pattern: 'ProjectUsedPattern',
  uncategorized: 'Uncategorized',
};

function normalizeCategory(value?: string | null) {
  const category = value?.trim();
  if (!category) return 'Uncategorized';
  return LEGACY_CATEGORY_ALIASES[category.toLowerCase()] ?? category;
}

function getShortContent(entry: KnowledgeEntryView) {
  if (entry.content.length <= 180) return entry.content;
  return `${entry.content.slice(0, 180)}...`;
}

function usageLabel(status?: string) {
  if (status === 'referenced') return '已调用';
  if (status === 'imported') return '已导入';
  return '未调用';
}

function hasCurrentProjectUsage(entry: KnowledgeEntryView) {
  return entry.projectUsageStatus === 'referenced' || entry.projectUsageStatus === 'imported';
}

function getProjectUsageRows(entry: KnowledgeEntryView, currentProjectTitle?: string): KnowledgeProjectUsage[] {
  const usages = entry.projectUsages ?? [];
  if (usages.length > 0 || !hasCurrentProjectUsage(entry)) return usages;

  return [{
    projectId: entry.sourceProjectId ?? 'current-project',
    projectTitle: currentProjectTitle || entry.sourceProjectTitle || '当前打开项目',
    status: entry.projectUsageStatus ?? 'referenced',
    usageCount: entry.projectUsageCount ?? 0,
    firstSeenAt: entry.projectLastUsedAt ?? entry.createdAt ?? '',
    lastUsedAt: entry.projectLastUsedAt ?? null,
  }];
}

function usageProjectCountLabel(entry: KnowledgeEntryView, currentProjectTitle?: string) {
  const count = getProjectUsageRows(entry, currentProjectTitle).length;
  if (count > 0) return `${count} 个项目调用`;
  return '0 个项目调用';
}

function formatUsageTime(value?: string | null) {
  if (!value) return '暂无';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '暂无';
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function evidenceStatusLabel(status?: string | null) {
  const normalized = (status ?? '').toLowerCase();
  if (normalized === 'satisfied') return '已满足';
  if (normalized === 'violated') return '已违反';
  if (normalized === 'unknown') return '未知';
  return status || '未记录';
}

function evidenceSummary(evidence: KnowledgeConstraintEvidence) {
  return [
    evidence.projectTitle,
    evidence.chapterId,
    evidence.constraintLevel || evidence.entryType,
    evidence.gateStatus ? `Gate ${evidence.gateStatus}` : '',
    evidence.factSnapshotVersion > 0 ? `事实 v${evidence.factSnapshotVersion}` : '',
  ].filter(Boolean).join(' · ');
}

function parseTagInput(value: string) {
  return value
    .split(/[,，;；|\n\r\t]/)
    .map((tag) => tag.trim())
    .filter(Boolean)
    .filter((tag, index, arr) => arr.findIndex((item) => item.toLowerCase() === tag.toLowerCase()) === index);
}

function formatTags(tags?: string[]) {
  return (tags ?? []).join('，');
}

function normalizeWeight(value: string) {
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return 5;
  return Math.min(10, Math.max(1, parsed));
}

function directoryColor(key: string) {
  return DIRECTORY_COLORS[key] ?? 'var(--gold)';
}

function isSystemDirectoryKey(key: string) {
  return key === ALL_DIRECTORY.key || !!SYSTEM_DIRECTORY_META[key];
}

function directoryDisplayName(directoryOrKey: KnowledgeDirectoryResponse | string) {
  const key = typeof directoryOrKey === 'string' ? directoryOrKey : directoryOrKey.key;
  const name = typeof directoryOrKey === 'string' ? '' : directoryOrKey.name;
  return key === ALL_DIRECTORY.key
    ? ALL_DIRECTORY.name
    : (SYSTEM_DIRECTORY_META[key]?.name ?? (name || key));
}

function directoryDescription(directory: KnowledgeDirectoryResponse) {
  return directory.key === ALL_DIRECTORY.key
    ? ALL_DIRECTORY.description
    : SYSTEM_DIRECTORY_META[directory.key]?.description ?? directory.description;
}

interface KnowledgeBaseBrowserProps {
  projectId?: string;
  actions?: ReactNode;
}

export default function KnowledgeBaseBrowser({ projectId, actions }: KnowledgeBaseBrowserProps) {
  const queryClient = useQueryClient();
  const currentProject = useProjectStore((state) => state.currentProject);
  const [selectedDirectory, setSelectedDirectory] = useState<KnowledgeDirectoryKey>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [directoryMenu, setDirectoryMenu] = useState<{
    directory: KnowledgeDirectoryResponse;
    x: number;
    y: number;
  } | null>(null);
  const [entryMenu, setEntryMenu] = useState<{
    entry: KnowledgeEntryView;
    x: number;
    y: number;
  } | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [creatingEntry, setCreatingEntry] = useState(false);
  const [directoryDialog, setDirectoryDialog] = useState<DirectoryDialogState | null>(null);
  const [directoryNameDraft, setDirectoryNameDraft] = useState('');
  const [entryDialog, setEntryDialog] = useState<EntryDialogState | null>(null);
  const [entryTitleDraft, setEntryTitleDraft] = useState('');
  const [editTitle, setEditTitle] = useState('');
  const [editCategory, setEditCategory] = useState('');
  const [editContent, setEditContent] = useState('');
  const [editTags, setEditTags] = useState('');
  const [editWeight, setEditWeight] = useState('5');
  const dragDraftRef = useRef<EntryDragDraft | null>(null);
  const suppressCardClickRef = useRef(false);
  const emptyEntriesRecoveryKeyRef = useRef<string | null>(null);
  const [draggingEntry, setDraggingEntry] = useState<KnowledgeEntryView | null>(null);
  const [dropDirectoryKey, setDropDirectoryKey] = useState<string | null>(null);
  const [dragPreviewPoint, setDragPreviewPoint] = useState<{ x: number; y: number } | null>(null);

  const {
    data: rawEntries = [],
    isLoading: entriesLoading,
    isFetching: entriesFetching,
    isError: entriesLoadFailed,
    error: entriesLoadError,
    refetch: refetchEntries,
  } = useQuery<KnowledgeResponse[], Error>({
    queryKey: ['knowledgeEntries', projectId || 'all'],
    queryFn: () => listKnowledgeEntries(projectId),
  });

  const { data: rawDirectories = [] } = useQuery({
    queryKey: ['knowledgeDirectories'],
    queryFn: listKnowledgeDirectories,
  });

  const { data: projects = [] } = useQuery({
    queryKey: ['projects'],
    queryFn: () => projectService.listProjects(),
    staleTime: 5 * 60 * 1000,
  });

  const currentProjectTitle = currentProject && currentProject.id === projectId
    ? currentProject.title
    : projects.find((project) => project.id === projectId)?.title;

  const entries: KnowledgeEntryView[] = useMemo(
    () => rawEntries.filter((entry) => !entry.isArchived).map((entry) => ({
      id: entry.id,
      category: normalizeCategory(entry.entryType),
      title: entry.title,
      content: entry.content,
      genre: '',
      subGenre: '',
      tags: entry.tags ?? [],
      weight: entry.weight ?? 5,
      source: '',
      usageCount: entry.usageCount,
      createdAt: entry.createdAt,
      isArchived: entry.isArchived ?? false,
      sourceProjectId: entry.sourceProjectId,
      sourceProjectTitle: entry.sourceProjectTitle,
      sourceType: entry.sourceType,
      sourceUploadTaskId: entry.sourceUploadTaskId,
      chunkIndex: entry.chunkIndex,
      extractionContext: entry.extractionContext,
      projectUsageStatus: entry.projectUsageStatus,
      projectUsageCount: entry.projectUsageCount,
      projectLastUsedAt: entry.projectLastUsedAt,
      projectUsages: entry.projectUsages ?? [],
      constraintEvidence: entry.constraintEvidence ?? [],
    })),
    [rawEntries],
  );

  const isEntryListPending = entriesLoading || (entriesFetching && rawEntries.length === 0);
  const directoryEntryTotal = useMemo(
    () => rawDirectories.reduce((sum, directory) => sum + Math.max(0, directory.entryCount ?? 0), 0),
    [rawDirectories],
  );
  const hasDirectoryEntriesButNoLoadedEntries = !entriesLoading
    && !entriesFetching
    && !entriesLoadFailed
    && rawEntries.length === 0
    && directoryEntryTotal > 0;
  const shouldUseDirectoryTotal = (isEntryListPending || entriesLoadFailed || hasDirectoryEntriesButNoLoadedEntries)
    && directoryEntryTotal > 0;
  const totalEntryCount = shouldUseDirectoryTotal ? directoryEntryTotal : entries.length;

  useEffect(() => {
    const recoveryKey = `${projectId || 'all'}:${directoryEntryTotal}`;
    if (!hasDirectoryEntriesButNoLoadedEntries) {
      if (rawEntries.length > 0) {
        emptyEntriesRecoveryKeyRef.current = null;
      }
      return;
    }

    if (emptyEntriesRecoveryKeyRef.current === recoveryKey) return;
    emptyEntriesRecoveryKeyRef.current = recoveryKey;
    void refetchEntries();
  }, [directoryEntryTotal, hasDirectoryEntriesButNoLoadedEntries, projectId, rawEntries.length, refetchEntries]);

  const directories = useMemo(() => {
    const entryCounts = new Map<string, number>();
    for (const entry of entries) {
      entryCounts.set(entry.category, (entryCounts.get(entry.category) ?? 0) + 1);
    }

    const merged = new Map<string, KnowledgeDirectoryResponse>();
    merged.set('All', { ...ALL_DIRECTORY, entryCount: totalEntryCount });
    for (const directory of rawDirectories) {
      if (merged.has(directory.key)) {
        continue;
      }

      merged.set(directory.key, {
        ...directory,
        name: directoryDisplayName(directory),
        description: directoryDescription(directory),
        isSystem: directory.isSystem || isSystemDirectoryKey(directory.key),
        entryCount: entryCounts.get(directory.key) ?? directory.entryCount ?? 0,
      });
    }
    for (const [key, count] of entryCounts) {
      if (!merged.has(key)) {
        const isSystem = isSystemDirectoryKey(key);
        merged.set(key, {
          key,
          name: directoryDisplayName(key),
          description: isSystem ? SYSTEM_DIRECTORY_META[key]?.description ?? '系统知识目录' : '由已有条目自动识别的目录',
          isSystem,
          entryCount: count,
        });
      }
    }

    return Array.from(merged.values()).sort((a, b) => {
      if (a.key === 'All') return -1;
      if (b.key === 'All') return 1;
      if (a.isSystem !== b.isSystem) return a.isSystem ? -1 : 1;
      return directoryDisplayName(a).localeCompare(directoryDisplayName(b), 'zh-Hans-CN');
    });
  }, [entries, rawDirectories, totalEntryCount]);

  const selectedDirectoryInfo = directories.find((directory) => directory.key === selectedDirectory)
    ?? directories[0]
    ?? ALL_DIRECTORY;

  const filtered = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return entries
      .filter((entry) => selectedDirectory === 'All' || entry.category === selectedDirectory)
      .filter((entry) => {
        if (!q) return true;
        return entry.title.toLowerCase().includes(q) || entry.content.toLowerCase().includes(q);
      })
      .sort((a, b) => a.title.localeCompare(b.title, 'zh-Hans-CN'));
  }, [entries, searchQuery, selectedDirectory]);

  const selectedEntry = useMemo(() => {
    if (creatingEntry) return null;
    if (!selectedId) return null;
    return entries.find((entry) => entry.id === selectedId) ?? null;
  }, [creatingEntry, entries, selectedId]);

  const directoryOptions = useMemo(
    () => directories.filter((directory) => directory.key !== 'All'),
    [directories],
  );
  const shouldUseDirectoryScopedCount = !searchQuery.trim()
    && (isEntryListPending || entriesLoadFailed || hasDirectoryEntriesButNoLoadedEntries);
  const currentEntryCount = shouldUseDirectoryScopedCount
    ? selectedDirectoryInfo.entryCount
    : filtered.length;

  useEffect(() => {
    if (selectedDirectory !== 'All' && !directories.some((directory) => directory.key === selectedDirectory)) {
      setSelectedDirectory('All');
      setSelectedId(null);
    }
  }, [directories, selectedDirectory]);

  useEffect(() => {
    if (!directoryMenu && !entryMenu) return;
    const closeMenu = () => {
      setDirectoryMenu(null);
      setEntryMenu(null);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeMenu();
    };
    window.addEventListener('click', closeMenu);
    window.addEventListener('contextmenu', closeMenu);
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      window.removeEventListener('click', closeMenu);
      window.removeEventListener('contextmenu', closeMenu);
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [directoryMenu, entryMenu]);

  const invalidateKnowledge = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', projectId || 'all'] }),
      queryClient.invalidateQueries({ queryKey: ['knowledgeDirectories'] }),
    ]);
  };

  const createDirectoryMutation = useMutation({
    mutationFn: (name: string) => createKnowledgeDirectory({ name }),
    onSuccess: async (directory) => {
      setDirectoryDialog(null);
      setDirectoryNameDraft('');
      setSelectedDirectory(directory.key);
      setSelectedId(null);
      await invalidateKnowledge();
    },
  });

  const updateDirectoryMutation = useMutation({
    mutationFn: ({ key, name }: { key: string; name: string }) => updateKnowledgeDirectory(key, { name }),
    onSuccess: async (directory) => {
      setDirectoryDialog(null);
      setDirectoryNameDraft('');
      setSelectedDirectory(directory.key);
      setSelectedId(null);
      await invalidateKnowledge();
    },
  });

  const deleteDirectoryMutation = useMutation({
    mutationFn: (key: string) => deleteKnowledgeDirectory(key),
    onSuccess: async () => {
      setSelectedDirectory('All');
      setSelectedId(null);
      await invalidateKnowledge();
    },
  });

  const createMutation = useMutation({
    mutationFn: () => {
      if (!projectId) throw new Error('请先选择项目后再新建知识条目');
      return createKnowledgeEntry({
        projectId,
        title: editTitle.trim(),
        content: editContent.trim(),
        entryType: normalizeCategory(editCategory || selectedDirectoryInfo.key),
        tags: parseTagInput(editTags),
        weight: normalizeWeight(editWeight),
      });
    },
    onSuccess: async (created) => {
      setCreatingEntry(false);
      setSelectedDirectory(normalizeCategory(created.entryType));
      setSelectedId(created.id);
      await invalidateKnowledge();
    },
  });

  const updateMutation = useMutation({
    mutationFn: () => {
      const base = entries.find((entry) => entry.id === editingId);
      if (!base) throw new Error('未选择知识条目');
      return updateKnowledgeEntryById(base.id, {
        title: editTitle,
        content: editContent,
        entryType: normalizeCategory(editCategory),
        tags: parseTagInput(editTags),
        weight: normalizeWeight(editWeight),
      });
    },
    onSuccess: async (updated) => {
      setEditingId(null);
      setSelectedDirectory(normalizeCategory(updated.entryType));
      setSelectedId(updated.id);
      await invalidateKnowledge();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteKnowledgeEntryById(id),
    onSuccess: async () => {
      setEntryDialog(null);
      setEntryTitleDraft('');
      setEditingId(null);
      setSelectedId(null);
      await invalidateKnowledge();
    },
  });

  const entryActionMutation = useMutation({
    mutationFn: ({ entry, mode, title }: { entry: KnowledgeEntryView; mode: EntryDialogState['mode']; title?: string }) => {
      if (mode === 'archive') {
        return updateKnowledgeEntryById(entry.id, { isArchived: true });
      }

      const nextTitle = title?.trim();
      if (!nextTitle) throw new Error('标题不能为空');
      return updateKnowledgeEntryById(entry.id, { title: nextTitle });
    },
    onSuccess: async (updated) => {
      setEntryDialog(null);
      setEntryTitleDraft('');
      setSelectedId(updated.isArchived ? null : updated.id);
      await invalidateKnowledge();
    },
  });

  const moveEntryMutation = useMutation({
    mutationFn: ({ id, directoryKey }: { id: string; directoryKey: string }) =>
      updateKnowledgeEntryById(id, { entryType: normalizeCategory(directoryKey) }),
    onSuccess: async () => {
      await invalidateKnowledge();
    },
    onSettled: () => {
      setEntryMenu(null);
      setDraggingEntry(null);
      setDropDirectoryKey(null);
      setDragPreviewPoint(null);
      dragDraftRef.current = null;
    },
  });

  const startCreating = (directoryKey = selectedDirectoryInfo.key) => {
    setCreatingEntry(true);
    setEditingId(null);
    setSelectedId(null);
    setEditTitle('');
    setEditCategory(directoryKey !== 'All' ? directoryKey : 'Uncategorized');
    setEditContent('');
    setEditTags('');
    setEditWeight('5');
  };

  const startEditing = (entry: KnowledgeEntryView) => {
    setCreatingEntry(false);
    setSelectedId(entry.id);
    setEditingId(entry.id);
    setEditTitle(entry.title);
    setEditCategory(entry.category);
    setEditContent(entry.content);
    setEditTags(formatTags(entry.tags));
    setEditWeight(String(entry.weight ?? 5));
  };

  const selectDirectory = (directory: KnowledgeDirectoryResponse) => {
    setSelectedDirectory(directory.key);
    setSelectedId(null);
    setCreatingEntry(false);
    setEditingId(null);
  };

  const openCreateDirectoryDialog = () => {
    setDirectoryMenu(null);
    setDirectoryDialog({ mode: 'create' });
    setDirectoryNameDraft('');
  };

  const openRenameDirectoryDialog = (directory: KnowledgeDirectoryResponse) => {
    if (directory.key === 'All' || directory.isSystem) return;
    setDirectoryMenu(null);
    setDirectoryDialog({ mode: 'rename', directory });
    setDirectoryNameDraft(directory.name);
  };

  const closeDirectoryDialog = () => {
    if (createDirectoryMutation.isPending || updateDirectoryMutation.isPending) return;
    setDirectoryDialog(null);
    setDirectoryNameDraft('');
  };

  const submitDirectoryDialog = () => {
    if (!directoryDialog) return;
    const name = directoryNameDraft.trim();
    if (!name) return;

    if (directoryDialog.mode === 'create') {
      createDirectoryMutation.mutate(name);
      return;
    }

    const directory = directoryDialog.directory;
    if (!directory || name === directory.name) {
      closeDirectoryDialog();
      return;
    }

    updateDirectoryMutation.mutate({ key: directory.key, name });
  };

  const removeDirectory = (directory: KnowledgeDirectoryResponse) => {
    if (directory.key === 'All' || directory.isSystem) return;
    const ok = window.confirm(`删除目录「${directoryDisplayName(directory)}」？其中 ${directory.entryCount} 个条目会移动到“未分类”，不会删除条目。`);
    if (!ok) return;
    deleteDirectoryMutation.mutate(directory.key);
    setDirectoryMenu(null);
  };

  const moveEntryToDirectory = (entryId: string, directoryKey: string) => {
    const targetDirectory = normalizeCategory(directoryKey);
    if (targetDirectory === 'All' || moveEntryMutation.isPending) return;
    const entry = entries.find((item) => item.id === entryId);
    if (!entry || entry.category === targetDirectory) {
      setEntryMenu(null);
      return;
    }

    moveEntryMutation.mutate({ id: entry.id, directoryKey: targetDirectory });
  };

  const openEntryActionDialog = (mode: EntryDialogState['mode'], entry: KnowledgeEntryView) => {
    setEntryMenu(null);
    setEntryDialog({ mode, entry });
    setEntryTitleDraft(mode === 'rename' ? entry.title : '');
  };

  const closeEntryDialog = () => {
    if (entryActionMutation.isPending || deleteMutation.isPending) return;
    setEntryDialog(null);
    setEntryTitleDraft('');
  };

  const submitEntryDialog = () => {
    if (!entryDialog) return;
    const { entry, mode } = entryDialog;

    if (mode === 'delete') {
      deleteMutation.mutate(entry.id);
      return;
    }

    if (mode === 'rename') {
      const title = entryTitleDraft.trim();
      if (!title || title === entry.title) {
        closeEntryDialog();
        return;
      }
      entryActionMutation.mutate({ entry, mode, title });
      return;
    }

    entryActionMutation.mutate({ entry, mode });
  };

  const openEntryMenu = (event: MouseEvent<HTMLElement>, entry: KnowledgeEntryView) => {
    event.preventDefault();
    event.stopPropagation();
    dragDraftRef.current = null;
    setDraggingEntry(null);
    setDropDirectoryKey(null);
    setDragPreviewPoint(null);
    setDirectoryMenu(null);
    setEntryMenu({ entry, x: event.clientX, y: event.clientY });
  };

  const beginEntryDrag = (event: MouseEvent<HTMLElement>, entry: KnowledgeEntryView) => {
    if (event.button !== 0 || moveEntryMutation.isPending) return;
    dragDraftRef.current = {
      entry,
      startX: event.clientX,
      startY: event.clientY,
      x: event.clientX,
      y: event.clientY,
      active: false,
    };
  };

  useEffect(() => {
    const findDirectoryKeyAtPoint = (x: number, y: number) => {
      const element = document.elementFromPoint(x, y);
      const directory = element?.closest<HTMLElement>('[data-knowledge-directory-key]');
      const key = directory?.dataset.knowledgeDirectoryKey;
      return key && key !== 'All' ? key : null;
    };

    const handleMouseMove = (event: globalThis.MouseEvent) => {
      const draft = dragDraftRef.current;
      if (!draft) return;

      const dx = event.clientX - draft.startX;
      const dy = event.clientY - draft.startY;
      if (!draft.active && Math.hypot(dx, dy) < ENTRY_DRAG_THRESHOLD) return;

      event.preventDefault();
      draft.active = true;
      draft.x = event.clientX;
      draft.y = event.clientY;
      setDraggingEntry(draft.entry);
      setDragPreviewPoint({ x: event.clientX, y: event.clientY });
      setDropDirectoryKey(findDirectoryKeyAtPoint(event.clientX, event.clientY));
    };

    const handleMouseUp = (event: globalThis.MouseEvent) => {
      const draft = dragDraftRef.current;
      if (!draft) return;

      dragDraftRef.current = null;
      if (!draft.active) return;

      suppressCardClickRef.current = true;
      window.setTimeout(() => {
        suppressCardClickRef.current = false;
      }, 0);

      const targetDirectory = findDirectoryKeyAtPoint(event.clientX, event.clientY);
      setDraggingEntry(null);
      setDropDirectoryKey(null);
      setDragPreviewPoint(null);

      if (targetDirectory && targetDirectory !== draft.entry.category) {
        moveEntryToDirectory(draft.entry.id, targetDirectory);
      }
    };

    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [entries, moveEntryMutation.isPending]);

  const isDetailMode = creatingEntry || !!selectedEntry;
  const isEditingDetail = creatingEntry || (!!selectedEntry && editingId === selectedEntry.id);

  return (
    <section className="knowledge-browser">
      {!isDetailMode && (
        <div className="knowledge-search-row">
          <input
            className="knowledge-search"
            placeholder="搜索标题或内容"
            value={searchQuery}
            onChange={(event) => setSearchQuery(event.target.value)}
          />
          <div className="knowledge-command-side">
            <div className="knowledge-command-stats" aria-label="知识库统计">
              <span><strong>{totalEntryCount}</strong> 条目</span>
              <span><strong>{directories.length - 1}</strong> 目录</span>
              <span><strong>{currentEntryCount}</strong> 当前</span>
            </div>
            {actions && <div className="knowledge-command-actions">{actions}</div>}
          </div>
        </div>
      )}

      <div className={`knowledge-workspace directory-mode ${isDetailMode ? 'detail-mode' : 'list-mode'}`}>
        {!isDetailMode && (
          <>
        <aside className="knowledge-directory-rail" aria-label="知识目录">
          <div className="knowledge-directory-head">
            <span>Knowledge</span>
            <div className="knowledge-directory-title-row">
              <strong>知识目录</strong>
              <button className="ghost-button compact" type="button" onClick={openCreateDirectoryDialog}>
                新建
              </button>
            </div>
          </div>
          <div className="knowledge-directory-list">
            {directories.map((directory) => (
              <button
                key={directory.key}
                type="button"
                data-knowledge-directory-key={directory.key}
                className={[
                  'knowledge-directory-item',
                  selectedDirectoryInfo.key === directory.key ? 'active' : '',
                  draggingEntry && directory.key !== 'All' ? 'drop-target' : '',
                  dropDirectoryKey === directory.key ? 'drop-over' : '',
                ].filter(Boolean).join(' ')}
                onClick={() => selectDirectory(directory)}
                onContextMenu={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  setDirectoryMenu({ directory, x: event.clientX, y: event.clientY });
                }}
              >
                <i style={{ color: directoryColor(directory.key) }} />
                <span>
                  <strong>{directoryDisplayName(directory)}</strong>
                  <small>{directory.isSystem ? '系统目录' : '自定义目录'} · {directory.entryCount} 条</small>
                </span>
                <em>{directory.entryCount}</em>
              </button>
            ))}
          </div>
        </aside>

        {directoryMenu && (
          <div
            className="knowledge-category-context-menu"
            style={{ left: directoryMenu.x, top: directoryMenu.y }}
            onClick={(event) => event.stopPropagation()}
            onContextMenu={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
          >
            <button
              type="button"
              onClick={() => {
                startCreating(directoryMenu.directory.key);
                setDirectoryMenu(null);
              }}
            >
              在此目录创建
            </button>
            <button
              type="button"
              disabled={directoryMenu.directory.key === 'All' || directoryMenu.directory.isSystem || updateDirectoryMutation.isPending}
              onClick={() => openRenameDirectoryDialog(directoryMenu.directory)}
            >
              重命名
            </button>
            <button
              type="button"
              className="danger"
              disabled={directoryMenu.directory.key === 'All' || directoryMenu.directory.isSystem || deleteDirectoryMutation.isPending}
              onClick={() => removeDirectory(directoryMenu.directory)}
            >
              删除
            </button>
          </div>
        )}

        {entryMenu && (
          <div
            className="knowledge-category-context-menu knowledge-entry-context-menu"
            style={{ left: entryMenu.x, top: entryMenu.y }}
            onClick={(event) => event.stopPropagation()}
            onContextMenu={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
          >
            <button
              type="button"
              disabled={entryActionMutation.isPending}
              onClick={() => openEntryActionDialog('rename', entryMenu.entry)}
            >
              改名
            </button>
            <button
              type="button"
              disabled={entryActionMutation.isPending}
              onClick={() => openEntryActionDialog('archive', entryMenu.entry)}
            >
              归档
            </button>
            <button
              type="button"
              className="danger"
              disabled={deleteMutation.isPending}
              onClick={() => openEntryActionDialog('delete', entryMenu.entry)}
            >
              删除
            </button>
          </div>
        )}

        {entryDialog && (
          <div
            className="knowledge-dialog-backdrop"
            role="presentation"
            onMouseDown={(event) => {
              if (event.target === event.currentTarget) closeEntryDialog();
            }}
          >
            <section
              className="knowledge-directory-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="knowledge-entry-dialog-title"
              onMouseDown={(event) => event.stopPropagation()}
            >
              <div className="knowledge-directory-dialog-head">
                <span>{entryDialog.mode === 'rename' ? '条目改名' : entryDialog.mode === 'archive' ? '条目归档' : '条目删除'}</span>
                <h3 id="knowledge-entry-dialog-title">
                  {entryDialog.mode === 'rename' ? '重命名条目' : entryDialog.mode === 'archive' ? '归档条目' : '删除条目'}
                </h3>
              </div>

              {entryDialog.mode === 'rename' ? (
                <label className="knowledge-directory-dialog-field">
                  <span>条目名称</span>
                  <input
                    autoFocus
                    value={entryTitleDraft}
                    placeholder="输入新的条目名称"
                    onChange={(event) => setEntryTitleDraft(event.target.value)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') submitEntryDialog();
                      if (event.key === 'Escape') closeEntryDialog();
                    }}
                  />
                </label>
              ) : (
                <p className="knowledge-action-copy">
                  {entryDialog.mode === 'archive'
                    ? `归档「${entryDialog.entry.title}」后，它会从当前知识库列表隐藏。`
                    : `确认删除知识条目「${entryDialog.entry.title}」吗？删除后不可恢复。`}
                </p>
              )}

              {(entryActionMutation.error || deleteMutation.error) && (
                <p className="knowledge-edit-error">
                  {entryActionMutation.error?.message || deleteMutation.error?.message}
                </p>
              )}

              <div className="knowledge-directory-dialog-actions">
                <button
                  className="ghost-button compact"
                  type="button"
                  onClick={closeEntryDialog}
                  disabled={entryActionMutation.isPending || deleteMutation.isPending}
                >
                  取消
                </button>
                <button
                  className={entryDialog.mode === 'delete' ? 'danger-button compact' : 'ink-button'}
                  type="button"
                  onClick={submitEntryDialog}
                  disabled={
                    entryActionMutation.isPending ||
                    deleteMutation.isPending ||
                    (entryDialog.mode === 'rename' && !entryTitleDraft.trim())
                  }
                >
                  {entryActionMutation.isPending || deleteMutation.isPending ? '处理中...' : '确认'}
                </button>
              </div>
            </section>
          </div>
        )}

        {directoryDialog && (
          <div
            className="knowledge-dialog-backdrop"
            role="presentation"
            onMouseDown={(event) => {
              if (event.target === event.currentTarget) closeDirectoryDialog();
            }}
          >
            <section
              className="knowledge-directory-dialog"
              role="dialog"
              aria-modal="true"
              aria-labelledby="knowledge-directory-dialog-title"
              onMouseDown={(event) => event.stopPropagation()}
            >
              <div className="knowledge-directory-dialog-head">
                <span>{directoryDialog.mode === 'create' ? '新建目录' : '目录改名'}</span>
                <h3 id="knowledge-directory-dialog-title">
                  {directoryDialog.mode === 'create' ? '新建知识目录' : '重命名知识目录'}
                </h3>
              </div>
              <label className="knowledge-directory-dialog-field">
                <span>目录名称</span>
                <input
                  autoFocus
                  value={directoryNameDraft}
                  placeholder="输入目录名称"
                  onChange={(event) => setDirectoryNameDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') submitDirectoryDialog();
                    if (event.key === 'Escape') closeDirectoryDialog();
                  }}
                />
              </label>
              {(createDirectoryMutation.error || updateDirectoryMutation.error) && (
                <p className="knowledge-edit-error">
                  {createDirectoryMutation.error?.message || updateDirectoryMutation.error?.message}
                </p>
              )}
              <div className="knowledge-directory-dialog-actions">
                <button
                  className="ghost-button compact"
                  type="button"
                  onClick={closeDirectoryDialog}
                  disabled={createDirectoryMutation.isPending || updateDirectoryMutation.isPending}
                >
                  取消
                </button>
                <button
                  className="ink-button"
                  type="button"
                  onClick={submitDirectoryDialog}
                  disabled={!directoryNameDraft.trim() || createDirectoryMutation.isPending || updateDirectoryMutation.isPending}
                >
                  {createDirectoryMutation.isPending || updateDirectoryMutation.isPending ? '保存中...' : '保存'}
                </button>
              </div>
            </section>
          </div>
        )}

        <main className="knowledge-entry-board">
          <div className="knowledge-board-head">
            <div>
              <span>{selectedDirectoryInfo.isSystem ? '系统目录' : '自定义目录'}</span>
              <h3>{directoryDisplayName(selectedDirectoryInfo)}</h3>
              <p>{directoryDescription(selectedDirectoryInfo)}</p>
            </div>
          </div>

          {entriesLoadFailed ? (
            <div className="empty knowledge-empty">
              知识条目加载失败：{entriesLoadError?.message || '请稍后重试'}
            </div>
          ) : isEntryListPending ? (
            <div className="empty knowledge-empty">正在加载知识条目...</div>
          ) : hasDirectoryEntriesButNoLoadedEntries ? (
            <div className="empty knowledge-empty">正在同步知识条目...</div>
          ) : filtered.length === 0 ? (
            <div className="empty knowledge-empty">当前目录没有知识条目</div>
          ) : (
            <div className="knowledge-card-grid">
              {filtered.map((entry) => (
                <article
                  key={entry.id}
                  className={`knowledge-entry-card ${draggingEntry?.id === entry.id ? 'dragging' : ''}`}
                  onMouseDown={(event) => beginEntryDrag(event, entry)}
                  onClick={() => {
                    if (suppressCardClickRef.current) return;
                    setCreatingEntry(false);
                    setEditingId(null);
                    setSelectedId(entry.id);
                  }}
                  onContextMenu={(event) => {
                    openEntryMenu(event, entry);
                  }}
                >
                  <div className="knowledge-card-header">
                <span className="category-badge" style={{ color: directoryColor(entry.category) }}>
                  {directoryDisplayName(directories.find((directory) => directory.key === entry.category) ?? entry.category)}
                </span>
                <span className="knowledge-project-count">{usageProjectCountLabel(entry, currentProjectTitle)}</span>
              </div>
                  <strong>{entry.title}</strong>
                  <p>{getShortContent(entry)}</p>
                  <div className="knowledge-card-footer">
                    <span>{entry.sourceProjectTitle ? `来源：${entry.sourceProjectTitle}` : '来源未记录'}</span>
                    <span>权重 {entry.weight ?? 5}</span>
                  </div>
                </article>
              ))}
            </div>
          )}
        </main>

          </>
        )}

        {draggingEntry && dragPreviewPoint && (
          <div
            className="knowledge-drag-preview"
            style={{ left: dragPreviewPoint.x + 12, top: dragPreviewPoint.y + 12 }}
          >
            <span>{directoryDisplayName(directories.find((directory) => directory.key === draggingEntry.category) ?? draggingEntry.category)}</span>
            <strong>{draggingEntry.title}</strong>
          </div>
        )}

        {isDetailMode && (
        <section className="knowledge-detail knowledge-detail-panel knowledge-detail-page">
          <div className="knowledge-detail-toolbar">
            <div className="knowledge-detail-toolbar-actions">
              <button
                className="ghost-button compact knowledge-back-button"
                type="button"
                onClick={() => {
                  setSelectedId(null);
                  setCreatingEntry(false);
                  setEditingId(null);
                }}
              >
                返回条目列表
              </button>
              {selectedEntry && editingId !== selectedEntry.id && !creatingEntry && (
                <>
                <button className="ink-button" type="button" onClick={() => startEditing(selectedEntry)}>
                  编辑条目
                </button>
                <button
                  className="danger-button knowledge-delete"
                  type="button"
                  onClick={() => deleteMutation.mutate(selectedEntry.id)}
                  disabled={deleteMutation.isPending}
                >
                  {deleteMutation.isPending ? '删除中...' : '删除条目'}
                </button>
                </>
              )}
            </div>
          </div>
          <div className={`knowledge-detail-content ${isEditingDetail ? 'editing' : ''}`}>
          {creatingEntry ? (
            <KnowledgeEditForm
              title="创建知识"
              editTitle={editTitle}
              setEditTitle={setEditTitle}
              editCategory={editCategory}
              setEditCategory={setEditCategory}
              editContent={editContent}
              setEditContent={setEditContent}
              editTags={editTags}
              setEditTags={setEditTags}
              editWeight={editWeight}
              setEditWeight={setEditWeight}
              directoryOptions={directoryOptions}
              primaryLabel={createMutation.isPending ? '创建中...' : '创建条目'}
              primaryDisabled={createMutation.isPending || !editTitle.trim() || !editContent.trim() || !normalizeCategory(editCategory)}
              onPrimary={() => createMutation.mutate()}
              onCancel={() => setCreatingEntry(false)}
              error={createMutation.error?.message}
            />
          ) : selectedEntry ? (
            <>
              <div className="knowledge-detail-head">
                <div>
                  <span className="category-badge" style={{ color: directoryColor(selectedEntry.category) }}>
                    {directoryDisplayName(directories.find((directory) => directory.key === selectedEntry.category) ?? selectedEntry.category)}
                  </span>
                  <h3>{selectedEntry.title}</h3>
                </div>
                <span className="knowledge-project-count">{usageProjectCountLabel(selectedEntry, currentProjectTitle)}</span>
              </div>

              {editingId === selectedEntry.id ? (
                <KnowledgeEditForm
                  title="编辑知识"
                  editTitle={editTitle}
                  setEditTitle={setEditTitle}
                  editCategory={editCategory}
                  setEditCategory={setEditCategory}
                  editContent={editContent}
                  setEditContent={setEditContent}
                  editTags={editTags}
                  setEditTags={setEditTags}
                  editWeight={editWeight}
                  setEditWeight={setEditWeight}
                  directoryOptions={directoryOptions}
                  primaryLabel={updateMutation.isPending ? '保存中...' : '保存条目'}
                  primaryDisabled={updateMutation.isPending || !editTitle.trim() || !editContent.trim() || !normalizeCategory(editCategory)}
                  onPrimary={() => updateMutation.mutate()}
                  onCancel={() => setEditingId(null)}
                  error={updateMutation.error?.message}
                />
              ) : (
                <>
                  <p>{selectedEntry.content}</p>
                  {selectedEntry.tags.length > 0 && (
                    <div className="knowledge-detail-tags">
                      {selectedEntry.tags.map((tag) => <span className="tag" key={tag}>{tag}</span>)}
                    </div>
                  )}
                  <dl className="knowledge-detail-meta">
                    <div>
                      <dt>所属目录</dt>
                      <dd>{directoryDisplayName(directories.find((directory) => directory.key === selectedEntry.category) ?? selectedEntry.category)}</dd>
                    </div>
                    <div>
                      <dt>来源项目</dt>
                      <dd>{selectedEntry.sourceProjectTitle || '未记录'}</dd>
                    </div>
                    <div>
                      <dt>来源类型</dt>
                      <dd>{selectedEntry.sourceType === 'extracted' ? '上传抽取' : '手动创建'}</dd>
                    </div>
                    {selectedEntry.sourceUploadTaskId && (
                      <div>
                        <dt>来源任务</dt>
                        <dd>{selectedEntry.sourceUploadTaskId}{selectedEntry.chunkIndex != null ? ` · 分块 ${selectedEntry.chunkIndex}` : ''}</dd>
                      </div>
                    )}
                    <div>
                      <dt>调用项目</dt>
                      <dd>{getProjectUsageRows(selectedEntry, currentProjectTitle).length} 个</dd>
                    </div>
                    <div>
                      <dt>当前项目</dt>
                      <dd>{currentProjectTitle || '当前打开项目'} · {usageLabel(selectedEntry.projectUsageStatus)} · {selectedEntry.projectUsageCount ?? 0} 次</dd>
                    </div>
                    <div>
                      <dt>全库累计调用</dt>
                      <dd>{selectedEntry.usageCount ?? 0}</dd>
                    </div>
                    <div>
                      <dt>检索权重</dt>
                      <dd>{selectedEntry.weight ?? 5}</dd>
                    </div>
                  </dl>

                  <section className="knowledge-project-usage-panel">
                    <strong>项目调用记录</strong>
                    {getProjectUsageRows(selectedEntry, currentProjectTitle).length > 0 ? (
                      <div className="knowledge-project-usage-list">
                        {getProjectUsageRows(selectedEntry, currentProjectTitle).map((usage) => (
                          <div className="knowledge-project-usage-row" key={`${usage.projectId}-${usage.status}`}>
                            <span>
                              <b>{usage.projectTitle}</b>
                              <small>{usageLabel(usage.status)} · {usage.usageCount} 次</small>
                            </span>
                            <em>{formatUsageTime(usage.lastUsedAt ?? usage.firstSeenAt)}</em>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p>还没有项目调用过这条知识；Agent 检索或导入项目后会在这里留下记录。</p>
                    )}
                  </section>

                  <section className="knowledge-project-usage-panel knowledge-constraint-evidence-panel">
                    <strong>章节约束证据</strong>
                    {(selectedEntry.constraintEvidence ?? []).length > 0 ? (
                      <div className="knowledge-constraint-evidence-list">
                        {(selectedEntry.constraintEvidence ?? []).slice(0, 8).map((evidence) => (
                          <div
                            className={`knowledge-constraint-evidence-row status-${(evidence.evidenceStatus || 'unknown').toLowerCase()}`}
                            key={`${evidence.factSnapshotId}-${evidence.chapterId}-${evidence.knowledgeId}`}
                            title={[
                              evidence.allowedTerms?.length > 0 ? `允许：${evidence.allowedTerms.join(' / ')}` : '',
                              evidence.forbiddenTerms?.length > 0 ? `禁止：${evidence.forbiddenTerms.join(' / ')}` : '',
                              evidence.violations?.length > 0 ? `违规：${evidence.violations.join(' / ')}` : '',
                            ].filter(Boolean).join(' · ')}
                          >
                            <span>
                              <b>{evidence.title || selectedEntry.title}</b>
                              <small>{evidenceSummary(evidence)}</small>
                            </span>
                            <em>{evidenceStatusLabel(evidence.evidenceStatus)}</em>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p>还没有章节 FactSnapshot 记录这条知识的约束证据。</p>
                    )}
                  </section>
                </>
              )}
            </>
          ) : (
            <div className="empty knowledge-empty">选择条目查看详情</div>
          )}
          </div>
        </section>
        )}
      </div>
    </section>
  );
}

interface KnowledgeEditFormProps {
  title: string;
  editTitle: string;
  setEditTitle: (value: string) => void;
  editCategory: string;
  setEditCategory: (value: string) => void;
  editContent: string;
  setEditContent: (value: string) => void;
  editTags: string;
  setEditTags: (value: string) => void;
  editWeight: string;
  setEditWeight: (value: string) => void;
  directoryOptions: KnowledgeDirectoryResponse[];
  primaryLabel: string;
  primaryDisabled: boolean;
  onPrimary: () => void;
  onCancel: () => void;
  error?: string;
}

function KnowledgeEditForm({
  title,
  editTitle,
  setEditTitle,
  editCategory,
  setEditCategory,
  editContent,
  setEditContent,
  editTags,
  setEditTags,
  editWeight,
  setEditWeight,
  directoryOptions,
  primaryLabel,
  primaryDisabled,
  onPrimary,
  onCancel,
  error,
}: KnowledgeEditFormProps) {
  return (
    <>
      <div className="knowledge-detail-head">
        <div>
          <span className="category-badge">知识条目</span>
          <h3>{title}</h3>
        </div>
      </div>
      <div className="knowledge-edit-form">
        <label>
          <span>标题</span>
          <input value={editTitle} onChange={(event) => setEditTitle(event.target.value)} />
        </label>
        <div className="knowledge-edit-grid">
          <DirectorySelect
            value={editCategory}
            options={directoryOptions}
            onChange={setEditCategory}
          />
          <label>
            <span>权重 1-10</span>
            <input value={editWeight} onChange={(event) => setEditWeight(event.target.value)} />
          </label>
        </div>
        <label>
          <span>标签</span>
          <input value={editTags} onChange={(event) => setEditTags(event.target.value)} placeholder="用逗号分隔" />
        </label>
        <label>
          <span>内容</span>
          <textarea value={editContent} onChange={(event) => setEditContent(event.target.value)} rows={12} />
        </label>
        <div className="knowledge-detail-actions">
          <button className="ink-button" type="button" onClick={onPrimary} disabled={primaryDisabled}>
            {primaryLabel}
          </button>
          <button className="ghost-button" type="button" onClick={onCancel}>取消</button>
          {error && <span className="knowledge-edit-error">{error}</span>}
        </div>
      </div>
    </>
  );
}

interface DirectorySelectProps {
  value: string;
  options: KnowledgeDirectoryResponse[];
  onChange: (value: string) => void;
}

function DirectorySelect({ value, options, onChange }: DirectorySelectProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const selectedDirectory = options.find((directory) => directory.key === value) ?? options[0];
  const selectedLabel = selectedDirectory ? directoryDisplayName(selectedDirectory) : '选择目录';

  useEffect(() => {
    if (!open) return;
    const closeOnPointerDown = (event: PointerEvent) => {
      if (!rootRef.current || rootRef.current.contains(event.target as Node)) return;
      setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };

    window.addEventListener('pointerdown', closeOnPointerDown);
    window.addEventListener('keydown', closeOnEscape);
    return () => {
      window.removeEventListener('pointerdown', closeOnPointerDown);
      window.removeEventListener('keydown', closeOnEscape);
    };
  }, [open]);

  return (
    <label className="knowledge-directory-select-field">
      <span>所属目录</span>
      <div className="knowledge-directory-select" ref={rootRef}>
        <button
          className="knowledge-directory-select-trigger"
          type="button"
          aria-haspopup="listbox"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          <span>{selectedLabel}</span>
          <i aria-hidden="true">▼</i>
        </button>
        {open && (
          <div className="knowledge-directory-select-menu" role="listbox" aria-label="所属目录">
            {options.map((directory) => {
              const selected = directory.key === value;
              return (
                <button
                  key={directory.key}
                  className={selected ? 'selected' : ''}
                  type="button"
                  role="option"
                  aria-selected={selected}
                  onClick={() => {
                    onChange(directory.key);
                    setOpen(false);
                  }}
                >
                  <span>{directoryDisplayName(directory)}</span>
                  {selected && <i aria-hidden="true">✓</i>}
                </button>
              );
            })}
          </div>
        )}
      </div>
    </label>
  );
}
