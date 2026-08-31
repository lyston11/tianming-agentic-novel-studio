import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MoreVertical, Plus, Search } from 'lucide-react';
import {
  createKnowledgeDirectory,
  createKnowledgeEntry,
  deleteKnowledgeDirectory,
  deleteKnowledgeEntryById,
  listKnowledgeDirectories,
  listKnowledgeEntries,
  listProjects,
  searchKnowledgeEntries,
  updateKnowledgeDirectory,
  updateKnowledgeEntryById,
} from '@/api';
import type {
  KnowledgeDirectoryResponse,
  KnowledgeResponse,
  KnowledgeSearchResult,
} from '@/api/types';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { EmptyState } from '@/components/shared/empty-state';
import {
  ALL_DIRECTORY,
  directoryColor,
  directoryDescription,
  directoryDisplayName,
  formatTags,
  getShortContent,
  isSystemDirectoryKey,
  normalizeCategory,
  normalizeWeight,
  parseTagInput,
  usageLabel,
  usageProjectCountLabel,
  type KnowledgeEntryView,
} from './knowledge-utils';

type KnowledgeDirectoryKey = 'All' | string;
type DirectoryDialogState = {
  mode: 'create' | 'rename';
  directory?: KnowledgeDirectoryResponse;
};
type EntryDialogState = {
  mode: 'rename' | 'archive' | 'delete';
  entry: KnowledgeEntryView;
};

interface KnowledgeBrowserProps {
  projectId?: string;
  actions?: ReactNode;
}

function toEntryView(entry: KnowledgeResponse): KnowledgeEntryView {
  return {
    id: entry.id,
    category: normalizeCategory(entry.entryType),
    title: entry.title,
    content: entry.content,
    tags: entry.tags ?? [],
    weight: entry.weight ?? 5,
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
  };
}

export function KnowledgeBrowser({ projectId, actions }: KnowledgeBrowserProps) {
  const queryClient = useQueryClient();
  const [selectedDirectory, setSelectedDirectory] = useState<KnowledgeDirectoryKey>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
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

  const {
    data: rawEntries = [],
    isLoading: entriesLoading,
    isFetching: entriesFetching,
    error: entriesLoadError,
    refetch: refetchEntries,
  } = useQuery<KnowledgeResponse[]>({
    queryKey: ['knowledgeEntries', projectId || 'all'],
    queryFn: () => listKnowledgeEntries(projectId),
  });

  const { data: rawDirectories = [] } = useQuery({
    queryKey: ['knowledgeDirectories'],
    queryFn: listKnowledgeDirectories,
  });

  const { data: projects = [] } = useQuery({
    queryKey: ['projects'],
    queryFn: listProjects,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    const normalizedQuery = searchQuery.trim();
    const timer = window.setTimeout(() => setDebouncedSearchQuery(normalizedQuery), 320);
    return () => window.clearTimeout(timer);
  }, [searchQuery]);

  const semanticSearchQuery = useQuery<KnowledgeSearchResult[]>({
    queryKey: ['knowledgeSemanticSearch', projectId, debouncedSearchQuery, selectedDirectory],
    queryFn: ({ signal }) =>
      searchKnowledgeEntries(
        {
          projectId: projectId!,
          query: debouncedSearchQuery,
          topK: 50,
          entryType: selectedDirectory === 'All' ? undefined : selectedDirectory,
        },
        signal,
      ),
    enabled: !!projectId && debouncedSearchQuery.length >= 2,
    retry: 1,
  });

  const currentProjectTitle = projects.find((project) => project.id === projectId)?.title;

  const entries = useMemo(
    () => rawEntries.filter((entry) => !entry.isArchived).map(toEntryView),
    [rawEntries],
  );

  const isEntryListPending = entriesLoading || (entriesFetching && rawEntries.length === 0);
  const directoryEntryTotal = useMemo(
    () => rawDirectories.reduce((sum, directory) => sum + Math.max(0, directory.entryCount ?? 0), 0),
    [rawDirectories],
  );
  const shouldUseDirectoryTotal = (isEntryListPending || entriesLoadError) && directoryEntryTotal > 0;
  const totalEntryCount = shouldUseDirectoryTotal ? directoryEntryTotal : entries.length;

  const directories = useMemo(() => {
    const entryCounts = new Map<string, number>();
    for (const entry of entries) {
      entryCounts.set(entry.category, (entryCounts.get(entry.category) ?? 0) + 1);
    }

    const merged = new Map<string, KnowledgeDirectoryResponse>();
    merged.set('All', { ...ALL_DIRECTORY, entryCount: totalEntryCount });
    for (const directory of rawDirectories) {
      if (merged.has(directory.key)) continue;
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
          description: isSystem ? '系统知识目录' : '由已有条目自动识别的目录',
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

  const selectedDirectoryInfo =
    directories.find((directory) => directory.key === selectedDirectory) ?? directories[0] ?? ALL_DIRECTORY;

  const normalizedSearchQuery = searchQuery.trim();
  const semanticSearchReady =
    !!projectId && normalizedSearchQuery.length >= 2 && normalizedSearchQuery === debouncedSearchQuery;
  const semanticSearchApplied = semanticSearchReady && semanticSearchQuery.data !== undefined;
  const filtered = useMemo(() => {
    const directoryEntries = entries.filter(
      (entry) => selectedDirectory === 'All' || entry.category === selectedDirectory,
    );
    if (semanticSearchApplied) {
      const entriesById = new Map(directoryEntries.map((entry) => [entry.id, entry]));
      return (semanticSearchQuery.data ?? [])
        .map((result) => entriesById.get(result.id))
        .filter((entry): entry is KnowledgeEntryView => !!entry);
    }

    const localQuery = normalizedSearchQuery.toLowerCase();
    return directoryEntries
      .filter((entry) => {
        if (!localQuery) return true;
        return entry.title.toLowerCase().includes(localQuery) || entry.content.toLowerCase().includes(localQuery);
      })
      .sort((a, b) => a.title.localeCompare(b.title, 'zh-Hans-CN'));
  }, [entries, normalizedSearchQuery, selectedDirectory, semanticSearchApplied, semanticSearchQuery.data]);

  const selectedEntry = useMemo(() => {
    if (creatingEntry || !selectedId) return null;
    return entries.find((entry) => entry.id === selectedId) ?? null;
  }, [creatingEntry, entries, selectedId]);

  const directoryOptions = useMemo(
    () => directories.filter((directory) => directory.key !== 'All'),
    [directories],
  );

  useEffect(() => {
    if (selectedDirectory !== 'All' && !directories.some((directory) => directory.key === selectedDirectory)) {
      setSelectedDirectory('All');
      setSelectedId(null);
    }
  }, [directories, selectedDirectory]);

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
      setDirectoryDialog(null);
      setDirectoryNameDraft('');
      return;
    }

    updateDirectoryMutation.mutate({ key: directory.key, name });
  };

  const removeDirectory = (directory: KnowledgeDirectoryResponse) => {
    if (directory.key === 'All' || directory.isSystem) return;
    if (directory.entryCount > 0) {
      window.alert(`目录「${directoryDisplayName(directory)}」仍有 ${directory.entryCount} 个条目。请先将条目移动到其他目录，再删除空目录。`);
      return;
    }
    const ok = window.confirm(`删除空目录「${directoryDisplayName(directory)}」？`);
    if (!ok) return;
    deleteDirectoryMutation.mutate(directory.key);
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
        setEntryDialog(null);
        setEntryTitleDraft('');
        return;
      }
      entryActionMutation.mutate({ entry, mode, title });
      return;
    }

    entryActionMutation.mutate({ entry, mode });
  };

  const isDetailMode = creatingEntry || !!selectedEntry;
  const isEditingDetail = creatingEntry || (!!selectedEntry && editingId === selectedEntry.id);
  const isEditFormPending = createMutation.isPending || updateMutation.isPending;

  return (
    <section className="flex min-h-0 flex-1 flex-col">
      {!isDetailMode && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <div className="min-w-0 flex-1">
            <div className="relative">
              <Search className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                className="pl-8"
                placeholder={projectId ? '按含义搜索知识库' : '搜索标题或内容'}
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
              />
            </div>
            <div
              className={`mt-1.5 text-xs ${semanticSearchQuery.isError ? 'text-destructive' : 'text-muted-foreground'}`}
              aria-live="polite"
            >
              {!projectId && normalizedSearchQuery.length >= 2
                ? '当前使用标题与内容检索；选择项目后可启用语义检索'
                : semanticSearchReady && semanticSearchQuery.isFetching
                  ? '正在理解查询并检索语义索引…'
                  : semanticSearchQuery.isError
                    ? `语义检索失败，已回退到文本匹配：${semanticSearchQuery.error.message}`
                    : semanticSearchApplied
                      ? `语义检索 · ${filtered.length} 条相关知识`
                      : projectId
                        ? '输入至少 2 个字符后启用语义检索'
                        : '当前浏览用户知识库'}
            </div>
          </div>
          <div className="flex items-center gap-3 text-xs text-muted-foreground" aria-label="知识库统计">
            <span><strong className="text-foreground">{totalEntryCount}</strong> 条目</span>
            <span><strong className="text-foreground">{directories.length - 1}</strong> 目录</span>
            <span><strong className="text-foreground">{filtered.length}</strong> 当前</span>
            {actions}
          </div>
        </div>
      )}

      <div className="grid min-h-0 flex-1 grid-cols-1 gap-4 lg:grid-cols-[240px_1fr]">
        {!isDetailMode && (
          <aside className="rounded-xl border bg-card p-3" aria-label="知识目录">
            <div className="mb-2 flex items-center justify-between px-1">
              <strong className="text-sm">知识目录</strong>
              <Button variant="ghost" size="xs" onClick={() => {
                setDirectoryDialog({ mode: 'create' });
                setDirectoryNameDraft('');
              }}>
                <Plus /> 新建
              </Button>
            </div>
            <div className="space-y-0.5">
              {directories.map((directory) => (
                <div key={directory.key} className="group/dir relative">
                  <button
                    type="button"
                    className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm transition-colors ${
                      selectedDirectoryInfo.key === directory.key
                        ? 'bg-primary/10 font-medium'
                        : 'hover:bg-muted'
                    }`}
                    onClick={() => selectDirectory(directory)}
                  >
                    <i className="size-2 shrink-0 rounded-full" style={{ background: directoryColor(directory.key) }} />
                    <span className="min-w-0 flex-1 truncate">
                      {directoryDisplayName(directory)}
                      <small className="ml-1 text-[10px] text-muted-foreground">
                        {directory.isSystem ? '系统' : '自定义'}
                      </small>
                    </span>
                    <em className="text-xs text-muted-foreground not-italic">{directory.entryCount}</em>
                  </button>
                  {directory.key !== 'All' && (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon-xs"
                          className="absolute top-1/2 right-1 -translate-y-1/2 opacity-0 group-hover/dir:opacity-100"
                        >
                          <MoreVertical />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => startCreating(directory.key)}>
                          新建条目到此目录
                        </DropdownMenuItem>
                        {!directory.isSystem && (
                          <>
                            <DropdownMenuItem
                              onClick={() => {
                                setDirectoryDialog({ mode: 'rename', directory });
                                setDirectoryNameDraft(directory.name);
                              }}
                            >
                              重命名目录
                            </DropdownMenuItem>
                            <DropdownMenuItem variant="destructive" onClick={() => removeDirectory(directory)}>
                              删除空目录
                            </DropdownMenuItem>
                          </>
                        )}
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                </div>
              ))}
            </div>
            {deleteDirectoryMutation.isError && (
              <p className="mt-2 text-xs text-destructive" role="alert">
                目录删除失败：{deleteDirectoryMutation.error.message}
              </p>
            )}
          </aside>
        )}

        {isDetailMode ? (
          <div className="min-w-0 rounded-xl border bg-card p-5">
            <div className="mb-4 flex items-center justify-between">
              <strong className="font-serif text-lg">
                {creatingEntry ? '新建知识条目' : selectedEntry?.title || '知识条目'}
              </strong>
              <div className="flex items-center gap-2">
                {isEditingDetail ? (
                  <>
                    <Button
                      size="sm"
                      disabled={isEditFormPending || !editTitle.trim() || !editContent.trim()}
                      onClick={() => (creatingEntry ? createMutation.mutate() : updateMutation.mutate())}
                    >
                      {isEditFormPending ? '保存中...' : '保存'}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={isEditFormPending}
                      onClick={() => {
                        setCreatingEntry(false);
                        setEditingId(null);
                      }}
                    >
                      取消
                    </Button>
                  </>
                ) : (
                  selectedEntry && (
                    <>
                      <Button variant="outline" size="sm" onClick={() => startEditing(selectedEntry)}>
                        编辑
                      </Button>
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon-sm">
                            <MoreVertical />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem
                            onClick={() => {
                              setEntryDialog({ mode: 'rename', entry: selectedEntry });
                              setEntryTitleDraft(selectedEntry.title);
                            }}
                          >
                            重命名
                          </DropdownMenuItem>
                          <DropdownMenuItem onClick={() => setEntryDialog({ mode: 'archive', entry: selectedEntry })}>
                            归档
                          </DropdownMenuItem>
                          <DropdownMenuItem variant="destructive" onClick={() => setEntryDialog({ mode: 'delete', entry: selectedEntry })}>
                            删除
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                      <Button variant="ghost" size="sm" onClick={() => setSelectedId(null)}>
                        返回列表
                      </Button>
                    </>
                  )
                )}
              </div>
            </div>

            {isEditingDetail ? (
              <div className="space-y-4">
                <div className="space-y-1.5">
                  <Label htmlFor="entry-title">标题</Label>
                  <Input id="entry-title" value={editTitle} onChange={(event) => setEditTitle(event.target.value)} />
                </div>
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-1.5">
                    <Label htmlFor="entry-category">目录/类型</Label>
                    <Input
                      id="entry-category"
                      value={editCategory}
                      onChange={(event) => setEditCategory(event.target.value)}
                      placeholder="HardFact / TropePattern ..."
                      list="knowledge-directory-options"
                    />
                    <datalist id="knowledge-directory-options">
                      {directoryOptions.map((directory) => (
                        <option key={directory.key} value={directory.key}>
                          {directoryDisplayName(directory)}
                        </option>
                      ))}
                    </datalist>
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="entry-weight">权重（1-10）</Label>
                    <Input
                      id="entry-weight"
                      type="number"
                      min={1}
                      max={10}
                      value={editWeight}
                      onChange={(event) => setEditWeight(event.target.value)}
                    />
                  </div>
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="entry-tags">标签</Label>
                  <Input
                    id="entry-tags"
                    value={editTags}
                    onChange={(event) => setEditTags(event.target.value)}
                    placeholder="用逗号分隔"
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="entry-content">内容</Label>
                  <Textarea
                    id="entry-content"
                    rows={10}
                    value={editContent}
                    onChange={(event) => setEditContent(event.target.value)}
                  />
                </div>
                {createMutation.isError && (
                  <p className="text-sm text-destructive">{createMutation.error.message}</p>
                )}
              </div>
            ) : (
              selectedEntry && (
                <div className="space-y-4">
                  <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <span className="rounded-full px-2 py-0.5" style={{ background: `color-mix(in oklch, ${directoryColor(selectedEntry.category)} 14%, transparent)` }}>
                      {directoryDisplayName(selectedEntry.category)}
                    </span>
                    <span>权重 {selectedEntry.weight}/10</span>
                    <span>{usageProjectCountLabel(selectedEntry, currentProjectTitle)}</span>
                    <span>{usageLabel(selectedEntry.projectUsageStatus ?? undefined)}</span>
                  </div>
                  {selectedEntry.tags.length > 0 && (
                    <div className="flex flex-wrap gap-1.5">
                      {selectedEntry.tags.map((tag) => (
                        <span key={tag} className="rounded bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground">
                          {tag}
                        </span>
                      ))}
                    </div>
                  )}
                  <p className="text-sm whitespace-pre-wrap">{selectedEntry.content}</p>
                  {selectedEntry.constraintEvidence.length > 0 && (
                    <div className="rounded-lg border p-3">
                      <strong className="text-xs">约束证据</strong>
                      <ul className="mt-1.5 space-y-1">
                        {selectedEntry.constraintEvidence.slice(0, 5).map((evidence, index) => (
                          <li key={index} className="truncate text-[11px] text-muted-foreground">
                            {evidence.projectTitle} · {evidence.chapterId} · Gate {evidence.gateStatus || '-'}
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </div>
              )
            )}
          </div>
        ) : (
          <div className="min-w-0 space-y-2 overflow-y-auto">
            {isEntryListPending ? (
              <p className="p-6 text-sm text-muted-foreground">正在加载知识条目...</p>
            ) : entriesLoadError ? (
              <div className="p-6 text-sm">
                <p className="text-destructive">知识条目加载失败：{entriesLoadError.message}</p>
                <Button variant="outline" size="sm" className="mt-2" onClick={() => void refetchEntries()}>
                  重试
                </Button>
              </div>
            ) : filtered.length === 0 ? (
              <EmptyState
                title="暂无知识条目"
                description={projectId ? '通过「导入素材」上传文件，或直接新建条目。' : '选择项目后可查看与新建知识条目。'}
              />
            ) : (
              filtered.map((entry) => (
                <article
                  key={entry.id}
                  role="button"
                  tabIndex={0}
                  className={`group cursor-pointer rounded-xl border bg-card p-4 transition-shadow hover:shadow-sm ${
                    selectedId === entry.id ? 'border-primary/40' : ''
                  }`}
                  onClick={() => setSelectedId(entry.id)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') setSelectedId(entry.id);
                  }}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <i className="size-2 shrink-0 rounded-full" style={{ background: directoryColor(entry.category) }} />
                        <strong className="truncate text-sm">{entry.title}</strong>
                      </div>
                      <p className="mt-1.5 line-clamp-2 text-xs text-muted-foreground">{getShortContent(entry)}</p>
                      <div className="mt-2 flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
                        <span>{directoryDisplayName(entry.category)}</span>
                        <span>·</span>
                        <span>{usageProjectCountLabel(entry, currentProjectTitle)}</span>
                        {entry.tags.slice(0, 4).map((tag) => (
                          <span key={tag} className="rounded bg-muted px-1.5 py-0.5">{tag}</span>
                        ))}
                      </div>
                    </div>
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          className="opacity-0 group-hover:opacity-100"
                          onClick={(event) => event.stopPropagation()}
                        >
                          <MoreVertical />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" onClick={(event) => event.stopPropagation()}>
                        <DropdownMenuItem onClick={() => setSelectedId(entry.id)}>
                          查看详情
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => startEditing(entry)}>
                          编辑
                        </DropdownMenuItem>
                        <DropdownMenuSub>
                          <DropdownMenuSubTrigger>移动到目录</DropdownMenuSubTrigger>
                          <DropdownMenuSubContent>
                            {directoryOptions.map((directory) => (
                              <DropdownMenuItem
                                key={directory.key}
                                disabled={directory.key === entry.category || moveEntryMutation.isPending}
                                onClick={() => moveEntryMutation.mutate({ id: entry.id, directoryKey: directory.key })}
                              >
                                {directoryDisplayName(directory)}
                              </DropdownMenuItem>
                            ))}
                          </DropdownMenuSubContent>
                        </DropdownMenuSub>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          onClick={() => {
                            setEntryDialog({ mode: 'rename', entry });
                            setEntryTitleDraft(entry.title);
                          }}
                        >
                          重命名
                        </DropdownMenuItem>
                        <DropdownMenuItem onClick={() => setEntryDialog({ mode: 'archive', entry })}>
                          归档
                        </DropdownMenuItem>
                        <DropdownMenuItem variant="destructive" onClick={() => setEntryDialog({ mode: 'delete', entry })}>
                          删除
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </article>
              ))
            )}
          </div>
        )}
      </div>

      <Dialog open={!!directoryDialog} onOpenChange={(open) => !open && setDirectoryDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>{directoryDialog?.mode === 'create' ? '新建知识目录' : '重命名知识目录'}</DialogTitle>
            <DialogDescription>
              目录对应知识条目的类型分类，用于检索过滤与门禁校验。
            </DialogDescription>
          </DialogHeader>
          <Input
            autoFocus
            value={directoryNameDraft}
            placeholder="目录名称"
            onChange={(event) => setDirectoryNameDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') submitDirectoryDialog();
            }}
          />
          <DialogFooter>
            <Button variant="ghost" size="sm" onClick={() => setDirectoryDialog(null)}>
              取消
            </Button>
            <Button size="sm" onClick={submitDirectoryDialog} disabled={!directoryNameDraft.trim()}>
              确认
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!entryDialog} onOpenChange={(open) => !open && setEntryDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>
              {entryDialog?.mode === 'rename'
                ? '重命名知识条目'
                : entryDialog?.mode === 'archive'
                  ? '归档知识条目'
                  : '删除知识条目'}
            </DialogTitle>
            <DialogDescription>
              {entryDialog?.mode === 'archive'
                ? `归档「${entryDialog.entry.title}」后将不再参与检索，可随时恢复。`
                : entryDialog?.mode === 'delete'
                  ? `确认删除「${entryDialog.entry.title}」吗？该操作不可恢复。`
                  : undefined}
            </DialogDescription>
          </DialogHeader>
          {entryDialog?.mode === 'rename' && (
            <Input
              autoFocus
              value={entryTitleDraft}
              onChange={(event) => setEntryTitleDraft(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') submitEntryDialog();
              }}
            />
          )}
          <DialogFooter>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setEntryDialog(null);
                setEntryTitleDraft('');
              }}
              disabled={entryActionMutation.isPending || deleteMutation.isPending}
            >
              取消
            </Button>
            <Button
              variant={entryDialog?.mode === 'delete' ? 'destructive' : 'default'}
              size="sm"
              disabled={entryActionMutation.isPending || deleteMutation.isPending}
              onClick={submitEntryDialog}
            >
              {entryActionMutation.isPending || deleteMutation.isPending ? '处理中...' : '确认'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}
