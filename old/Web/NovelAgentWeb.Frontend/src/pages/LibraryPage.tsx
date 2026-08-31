import { useEffect, useMemo, useState } from 'react';
import type { MouseEvent } from 'react';
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { compareChapterVersions, deleteNovelProject, updateNovelProject, getChapterById, getChapterVersions, getStoryBibleByProject, listProjectChapters, listVolumeArcs, rollbackChapterVersion } from '../api';
import type { ChapterResponse, ChapterVersionDiffBlock, ChapterVersionProductionAlignment, ChapterVersionResponse, NovelBookView, NovelChapterView, NovelVolumeView, WorkflowProductionChain } from '../api/types';
import { projectService, toNovelProjectInfo } from '../services/projectService';
import { useAuthStore } from '../stores/authStore';
import { useProjectStore } from '../stores/useProjectStore';
import Topbar from '../components/layout/Topbar';
import '../styles/library.css';

type LibraryMode = 'store' | 'detail' | 'reader';
type BookMenuState = {
  book: NovelBookView;
  x: number;
  y: number;
};
type BookDialogState = {
  mode: 'rename' | 'archive' | 'delete';
  book: NovelBookView;
};
type VersionRollbackDialogState = {
  chapterId: string;
  version: ChapterVersionResponse;
};

function chapterStatus(chapter: NovelChapterView) {
  if (chapter.needsRewrite) return '需修订';
  if (chapter.visibleInLibrary) return '已入库';
  return chapter.status || '成稿';
}

function chapterStatusClass(chapter: NovelChapterView) {
  if (chapter.needsRewrite) return 'danger';
  if (chapter.visibleInLibrary) return 'ready';
  return '';
}

function coverMark(title: string) {
  return (title || '命').trim().slice(0, 1);
}

function libraryProgressPercent(book: NovelBookView) {
  const total = book.plannedChapterCount || book.generatedChapterCount;
  if (total <= 0) return 0;
  return Math.min(100, Math.round((book.generatedChapterCount / total) * 100));
}

function isArchivedBook(book: NovelBookView) {
  return (book.status || '').toLowerCase() === 'archived';
}

function chapterTitle(chapter: ChapterResponse) {
  if (/^chapter-\d+$/i.test(chapter.title)) {
    return `第 ${chapter.chapterNumber} 章`;
  }

  return chapter.title || `第 ${chapter.chapterNumber} 章`;
}

function normalizeReaderLine(value: string) {
  return value
    .replace(/^#{1,6}\s*/, '')
    .replace(/\*\*/g, '')
    .trim();
}

function stripReaderTitlePrefix(value: string, title: string) {
  const line = normalizeReaderLine(value);
  const normalizedTitle = normalizeReaderLine(title);
  const chapterMatch = normalizedTitle.match(/^(第[一二三四五六七八九十百千万\d]+章)[：:\s、-]*(.*)$/);
  if (!chapterMatch) return line;

  const chapterLabel = chapterMatch[1];
  const titleCore = chapterMatch[2]?.trim();
  if (!line.startsWith(chapterLabel) || !titleCore) return line;

  const coreIndex = line.indexOf(titleCore);
  if (coreIndex < 0 || coreIndex > 16) return line;
  return line.slice(coreIndex + titleCore.length).replace(/^[：:\s、，。.-]+/, '').trim();
}

function splitLongReaderParagraph(value: string) {
  const text = value.trim();
  if (text.length <= 180) return [text];

  const parts: string[] = [];
  let rest = text;
  while (rest.length > 180) {
    const windowText = rest.slice(0, 240);
    const punctuationIndex = Math.max(
      windowText.lastIndexOf('。'),
      windowText.lastIndexOf('！'),
      windowText.lastIndexOf('？'),
      windowText.lastIndexOf('；'),
    );
    const splitAt = punctuationIndex >= 80 ? punctuationIndex + 1 : 180;
    parts.push(rest.slice(0, splitAt).trim());
    rest = rest.slice(splitAt).trim();
  }
  if (rest) parts.push(rest);
  return parts;
}

function chapterParagraphs(content: string, title: string) {
  const normalizedTitle = normalizeReaderLine(title);
  return content
    .replace(/\r\n/g, '\n')
    .split(/\n+/)
    .map((line, index) => index === 0 ? stripReaderTitlePrefix(line, title) : normalizeReaderLine(line))
    .filter(Boolean)
    .filter((line, index) => {
      if (index > 1) return true;
      return line !== normalizedTitle && !normalizedTitle.includes(line) && !line.includes(normalizedTitle);
    })
    .flatMap(splitLongReaderParagraph);
}

function versionLabel(version: ChapterVersionResponse) {
  const markers = [
    `v${version.versionNumber}`,
    version.isCurrent ? '当前' : '',
    version.status,
  ].filter(Boolean);
  return markers.join(' · ');
}

function diffLabel(block: ChapterVersionDiffBlock) {
  if (block.kind === 'added') return '新增';
  if (block.kind === 'removed') return '删除';
  if (block.kind === 'changed') return '修改';
  return '保留';
}

function diffText(block: ChapterVersionDiffBlock) {
  if (block.kind === 'added') return block.rightText;
  if (block.kind === 'removed') return block.leftText;
  if (block.kind === 'changed') return `旧：${block.leftText}｜新：${block.rightText}`;
  return block.rightText || block.leftText;
}

function alignmentChips(alignment?: ChapterVersionProductionAlignment | null) {
  if (!alignment) return [];
  const chips = [
    alignment.acceptedCreativeIntents.length > 0
      ? {
          key: 'creative',
          label: `创意 ${alignment.acceptedCreativeIntents.length}`,
          value: alignment.acceptedCreativeIntents[0].normalizedIntent,
          tone: 'creative',
        }
      : null,
    alignment.sourceRevisionPlans.length > 0
      ? {
          key: 'revision',
          label: `修订 ${alignment.sourceRevisionPlans.length}`,
          value: alignment.sourceRevisionPlans[0].recommendation || alignment.sourceRevisionPlans[0].status,
          tone: 'revision',
        }
      : null,
    alignment.agentReviewDecision || alignment.agentReviewChecks.length > 0
      ? {
          key: 'review',
          label: '审稿',
          value: alignment.agentReviewDecision || `${alignment.agentReviewChecks.length} 项检查`,
          tone: 'review',
        }
      : null,
    alignment.rebuiltFromPackageIds.length > 0
      ? {
          key: 'package',
          label: `替代包 ${alignment.rebuiltFromPackageIds.length}`,
          value: alignment.rebuiltFromPackageIds.slice(0, 2).join(' / '),
          tone: 'package',
        }
      : null,
  ].filter(Boolean);
  return chips as Array<{ key: string; label: string; value: string; tone: string }>;
}

function productionStatusLabel(status: string) {
  const normalized = (status || '').toLowerCase();
  if (normalized === 'completed') return '已完成';
  if (normalized === 'running') return '执行中';
  if (normalized === 'blocked') return '需处理';
  if (normalized === 'failed') return '失败';
  if (normalized === 'pending') return '等待中';
  return status || '未知';
}

function productionStatusClass(status: string) {
  const normalized = (status || '').toLowerCase();
  if (normalized === 'completed') return 'completed';
  if (normalized === 'blocked' || normalized === 'failed') return 'blocked';
  if (normalized === 'running') return 'running';
  return '';
}

function latestProductionChain(chains: WorkflowProductionChain[]) {
  return [...chains].sort((a, b) => (b.updatedAt || '').localeCompare(a.updatedAt || ''))[0] ?? null;
}

function toLibraryChapter(chapter: ChapterResponse): NovelChapterView {
  return {
    chapterId: chapter.id,
    volumeId: chapter.volumeId ?? 'committed',
    volumeTitle: '已入库章节',
    title: chapterTitle(chapter),
    beatIndex: chapter.chapterNumber,
    beatRole: '',
    goal: '',
    turn: '',
    cost: '',
    status: '已入库',
    runId: '',
    intent: '',
    updatedAt: chapter.updatedAt,
    hasGeneratedContent: true,
    needsRewrite: false,
    wordCount: chapter.wordCount,
    summary: `${chapterTitle(chapter)}已提交到小说书城。`,
    content: chapter.content ?? '',
    selectedCandidateTitle: '',
    qualityScore: 0,
    rewriteAttemptCount: 0,
    reviewChecks: [],
    nextSuggestions: [],
    writingStatus: 'committed',
    contextPackageStatus: '',
    draftArtifactStatus: 'committed',
    gateStatus: 'validated',
    changesProtocolPassed: true,
    factSnapshotPassed: true,
    blueprintPassed: true,
    longDistanceRagPassed: true,
    ragRecallCount: 0,
    repairAttemptCount: 0,
    gateIssues: [],
    repairHints: [],
    dependencyWarnings: [],
    contextWarnings: [],
    visibleInWorkflow: false,
    visibleInLibrary: true,
    userVisibleStatus: '成稿已进入书城',
    artifactStatus: 'committed',
    draftArtifactId: '',
    gateReportId: '',
    qualityReportId: '',
  };
}

export default function LibraryPage() {
  const queryClient = useQueryClient();
  const { user } = useAuthStore();
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
  const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
  const ensureProjectSelected = useProjectStore((s) => s.ensureProjectSelected);
  const { data: projects, isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: () => projectService.listProjects(),
    refetchInterval: 10000,
  });
  const { data: userStats } = useQuery({
    queryKey: ['userStats'],
    queryFn: () => projectService.getUserStats(),
    refetchInterval: 30000,
  });
  const [mode, setMode] = useState<LibraryMode>('store');
  const [selectedChapterId, setSelectedChapterId] = useState<string | null>(null);
  const [isCoverEditorOpen, setIsCoverEditorOpen] = useState(false);
  const [coverDraftUrl, setCoverDraftUrl] = useState('');
  const [bookMenu, setBookMenu] = useState<BookMenuState | null>(null);
  const [bookDialog, setBookDialog] = useState<BookDialogState | null>(null);
  const [bookTitleDraft, setBookTitleDraft] = useState('');
  const [readerFontSize, setReaderFontSize] = useState(20);
  const [leftVersionId, setLeftVersionId] = useState('');
  const [rightVersionId, setRightVersionId] = useState('');
  const [rollbackDialog, setRollbackDialog] = useState<VersionRollbackDialogState | null>(null);

  const projectsList = useMemo(() => projects ?? [], [projects]);
  const projectInfos = useMemo(
    () => projectsList.map(toNovelProjectInfo),
    [projectsList],
  );
  const chapterQueries = useQueries({
    queries: projectsList.map((project) => ({
      queryKey: ['projectChapters', project.id],
      queryFn: () => listProjectChapters(project.id),
      refetchInterval: 10000,
      staleTime: 5000,
    })),
  });
  const chaptersByProject = useMemo(() => {
    const map = new Map<string, ChapterResponse[]>();
    projectsList.forEach((project, index) => {
      const chapters = chapterQueries[index]?.data ?? [];
      map.set(
        project.id,
        chapters
          .filter((chapter) => chapter.status.toLowerCase() === 'committed')
          .sort((a, b) => a.chapterNumber - b.chapterNumber),
      );
    });
    return map;
  }, [chapterQueries, projectsList]);

  const { data: storyBible, isLoading: bibleLoading } = useQuery({
    queryKey: ['storyBible', currentProjectId],
    queryFn: () => currentProjectId ? getStoryBibleByProject(currentProjectId) : Promise.resolve(null),
    enabled: !!currentProjectId,
    refetchInterval: 10000,
  });

  const { data: volumes, isLoading: volumesLoading } = useQuery({
    queryKey: ['volumeArcs', currentProjectId],
    queryFn: () => currentProjectId ? listVolumeArcs(currentProjectId) : Promise.resolve([]),
    enabled: !!currentProjectId,
    refetchInterval: 10000,
  });

  // Project rows are projected into the book-card view used by the library.
  const books: NovelBookView[] = useMemo(() => {
    return projectsList.map(project => {
      const committedChapters = chaptersByProject.get(project.id) ?? [];
      const selectedChapter = committedChapters[0]
        ? toLibraryChapter(committedChapters[0])
        : null;
      return {
        projectId: project.id,
        title: project.title,
        genre: project.genre || '',
        subGenre: project.subGenre || '',
        coreHook: project.coreHook || '',
        readerPromise: '',
        status: project.status,
        coverImageUrl: project.coverImageUrl,
        isActive: true,
        volumeCount: committedChapters.length > 0 ? 1 : 0,
        generatedChapterCount: committedChapters.length,
        plannedChapterCount: committedChapters.length,
        needsRewriteCount: 0,
        updatedAt: project.updatedAt,
        selectedChapter,
      };
    });
  }, [chaptersByProject, projectsList]);

  const libraryBooks = useMemo(
    () => books.filter((book) =>
      !isArchivedBook(book) && (
        book.generatedChapterCount > 0 ||
        book.selectedChapter?.visibleInLibrary ||
        !!book.selectedChapter?.content?.trim()
      )),
    [books],
  );

  // Enhance the selected book with StoryBible and volume data
  const selectedBook = useMemo(() => {
    if (!currentProjectId) return null;
    const baseBook = books.find((book) => book.projectId === currentProjectId);
    if (!baseBook) return null;

    const projectVolumes = volumes ?? [];
    const committedChapters = chaptersByProject.get(currentProjectId) ?? [];
    const generatedChapterCount = committedChapters.length;
    const plannedChapterCount = committedChapters.length || projectVolumes.reduce((sum, vol) =>
      sum + (vol.targetChapters || vol.currentChapters || 0), 0
    );

    return {
      ...baseBook,
      coreHook: storyBible?.constitution?.coreHook || baseBook.coreHook,
      readerPromise: storyBible?.constitution?.readerPromise || '',
      genre: storyBible?.constitution?.genre || baseBook.genre,
      volumeCount: committedChapters.length > 0 ? 1 : projectVolumes.length,
      generatedChapterCount,
      plannedChapterCount,
      selectedChapter: committedChapters[0] ? toLibraryChapter(committedChapters[0]) : baseBook.selectedChapter,
    };
  }, [books, chaptersByProject, currentProjectId, storyBible, volumes]);

  // Convert VolumeArcResponse[] to NovelVolumeView[] format
  const volumeViews: NovelVolumeView[] = useMemo(() => {
    const committedChapters = currentProjectId ? chaptersByProject.get(currentProjectId) ?? [] : [];
    if (committedChapters.length > 0) {
      return [{
        volumeId: 'committed',
        title: '已入库章节',
        status: 'committed',
        startChapterId: committedChapters[0].id,
        endChapterId: committedChapters[committedChapters.length - 1].id,
        expectedChapterCount: committedChapters.length,
        chapters: committedChapters.map(toLibraryChapter),
      }];
    }

    if (!volumes) return [];
    return volumes.map(vol => ({
      volumeId: vol.id,
      title: vol.volumeTitle,
      status: vol.status,
      startChapterId: '',
      endChapterId: '',
      expectedChapterCount: vol.targetChapters || 0,
      chapters: [
        {
          chapterId: vol.id + '-placeholder',
          volumeId: vol.id,
          volumeTitle: vol.volumeTitle || '待生成',
          title: vol.volumeTitle || '待生成',
          beatIndex: 0,
          beatRole: '',
          goal: '',
          turn: '',
          cost: '',
          status: 'placeholder',
          runId: '',
          intent: '',
          updatedAt: '',
          hasGeneratedContent: false,
          needsRewrite: false,
          wordCount: 0,
          summary: '',
          content: '',
          selectedCandidateTitle: '',
          qualityScore: 0,
          rewriteAttemptCount: 0,
          reviewChecks: [],
          nextSuggestions: [],
          writingStatus: '',
          contextPackageStatus: '',
          draftArtifactStatus: '',
          gateStatus: '',
          changesProtocolPassed: false,
          factSnapshotPassed: false,
          blueprintPassed: false,
          longDistanceRagPassed: false,
          ragRecallCount: 0,
          repairAttemptCount: 0,
          gateIssues: [],
          repairHints: [],
          dependencyWarnings: [],
          contextWarnings: [],
          visibleInWorkflow: true,
          visibleInLibrary: false,
          userVisibleStatus: '',
          artifactStatus: '',
          draftArtifactId: '',
          gateReportId: '',
          qualityReportId: '',
        } as NovelChapterView,
      ],
    }));
  }, [chaptersByProject, currentProjectId, volumes]);

  const filteredVolumes = useMemo(
    () => volumeViews
      .map((volume) => ({
        ...volume,
        chapters: volume.chapters.filter((chapter) => chapter.visibleInLibrary),
      }))
      .filter((volume) => volume.chapters.length > 0),
    [volumeViews],
  );
  const allChapters = useMemo(() => filteredVolumes.flatMap((vol) => vol.chapters), [filteredVolumes]);
  const selectedChapter = useMemo(() => {
    if (allChapters.length === 0) return null;
    const baseChapter = (
      allChapters.find((chapter) => chapter.chapterId === selectedChapterId) ??
      allChapters[0]
    );
    return baseChapter;
  }, [allChapters, selectedChapterId]);
  const selectedChapterContent = useQuery({
    queryKey: ['chapterContent', selectedChapter?.chapterId],
    queryFn: () => getChapterById(selectedChapter!.chapterId),
    enabled: !!selectedChapter?.chapterId && selectedChapter.visibleInLibrary,
    staleTime: 10000,
  });
  const chapterVersionsQuery = useQuery({
    queryKey: ['chapterVersions', selectedChapter?.chapterId],
    queryFn: () => getChapterVersions(selectedChapter!.chapterId),
    enabled: mode === 'reader' && !!selectedChapter?.chapterId && selectedChapter.visibleInLibrary,
    staleTime: 15000,
  });
  const readerChapter = useMemo(() => {
    if (!selectedChapter) return null;
    const detail = selectedChapterContent.data;
    if (!detail || detail.id !== selectedChapter.chapterId) return selectedChapter;

    return {
      ...selectedChapter,
      title: chapterTitle(detail),
      content: detail.content ?? selectedChapter.content,
      wordCount: detail.wordCount,
      updatedAt: detail.updatedAt,
    };
  }, [selectedChapter, selectedChapterContent.data]);
  const readerChapterIndex = useMemo(() => {
    if (!readerChapter) return -1;
    return allChapters.findIndex((chapter) => chapter.chapterId === readerChapter.chapterId);
  }, [allChapters, readerChapter]);
  const previousChapter = readerChapterIndex > 0 ? allChapters[readerChapterIndex - 1] : null;
  const nextChapter = readerChapterIndex >= 0 && readerChapterIndex < allChapters.length - 1
    ? allChapters[readerChapterIndex + 1]
    : null;
  const readerParagraphs = readerChapter?.content
    ? chapterParagraphs(readerChapter.content, readerChapter.title)
    : [];
  const readerProductionChains = useMemo(
    () => selectedChapterContent.data?.productionChains ?? [],
    [selectedChapterContent.data?.productionChains],
  );
  const currentProductionChain = useMemo(
    () => latestProductionChain(readerProductionChains),
    [readerProductionChains],
  );
  const readerProductionEvidence = selectedChapterContent.data?.productionEvidence;
  const chapterVersions = useMemo(() => chapterVersionsQuery.data ?? [], [chapterVersionsQuery.data]);
  const orderedVersions = useMemo(
    () => [...chapterVersions].sort((a, b) => b.versionNumber - a.versionNumber),
    [chapterVersions],
  );
  const leftVersion = orderedVersions.find((version) => version.id === leftVersionId) ?? null;
  const rightVersion = orderedVersions.find((version) => version.id === rightVersionId) ?? null;
  const compareQuery = useQuery({
    queryKey: ['chapterVersionCompare', selectedChapter?.chapterId, leftVersionId, rightVersionId],
    queryFn: () => compareChapterVersions(selectedChapter!.chapterId, leftVersionId, rightVersionId),
    enabled: mode === 'reader' &&
      !!selectedChapter?.chapterId &&
      !!leftVersionId &&
      !!rightVersionId &&
      leftVersionId !== rightVersionId,
    staleTime: 15000,
  });
  const visibleDiffBlocks = (compareQuery.data?.diffBlocks ?? [])
    .filter((block) => block.kind !== 'unchanged')
    .slice(0, 6);
  const readyChapters = selectedBook?.generatedChapterCount ?? 0;
  const plannedChapters = selectedBook?.plannedChapterCount ?? 0;

  useEffect(() => {
    const timer = window.setTimeout(() => {
      if (projects) ensureProjectSelected(projectInfos);
      if (projectsList.length === 0 && mode !== 'store') setMode('store');
    }, 0);
    return () => window.clearTimeout(timer);
  }, [ensureProjectSelected, projectInfos, projects, projectsList.length, mode]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setCoverDraftUrl(selectedBook?.coverImageUrl ?? '');
      setIsCoverEditorOpen(false);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [selectedBook?.projectId, selectedBook?.coverImageUrl]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setLeftVersionId('');
      setRightVersionId('');
    }, 0);
    return () => window.clearTimeout(timer);
  }, [selectedChapter?.chapterId]);

  useEffect(() => {
    if (orderedVersions.length < 2 || leftVersionId || rightVersionId) return;
    const current = orderedVersions.find((version) => version.isCurrent) ?? orderedVersions[0];
    const baseline = orderedVersions.find((version) => version.id !== current.id) ?? orderedVersions[1];
    const timer = window.setTimeout(() => {
      setLeftVersionId(baseline.id);
      setRightVersionId(current.id);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [leftVersionId, orderedVersions, rightVersionId]);

  useEffect(() => {
    if (!bookMenu) return;
    const closeMenu = () => setBookMenu(null);
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeMenu();
    };
    window.addEventListener('click', closeMenu);
    window.addEventListener('contextmenu', closeMenu);
    window.addEventListener('keydown', closeOnEscape);
    window.addEventListener('resize', closeMenu);
    return () => {
      window.removeEventListener('click', closeMenu);
      window.removeEventListener('contextmenu', closeMenu);
      window.removeEventListener('keydown', closeOnEscape);
      window.removeEventListener('resize', closeMenu);
    };
  }, [bookMenu]);

  const deleteMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: ['storyBible'] }),
        queryClient.invalidateQueries({ queryKey: ['volumeArcs'] }),
      ]);
      setCurrentProject(null);
      setSelectedChapterId(null);
      setMode('store');
    },
  });

  const renameMutation = useMutation({
    mutationFn: ({ projectId, title }: { projectId: string; title: string }) =>
      updateNovelProject(projectId, { title }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
    },
  });

  const archiveMutation = useMutation({
    mutationFn: (projectId: string) => updateNovelProject(projectId, { status: 'archived' }),
    onSuccess: async (_result, projectId) => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
      if (currentProjectId === projectId) {
        setCurrentProject(null);
        setSelectedChapterId(null);
        setMode('store');
      }
    },
  });

  const coverMutation = useMutation({
    mutationFn: ({ projectId, coverImageUrl }: { projectId: string; coverImageUrl: string }) =>
      updateNovelProject(projectId, { coverImageUrl }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
    },
  });

  const rollbackMutation = useMutation({
    mutationFn: ({ chapterId, version }: VersionRollbackDialogState) =>
      rollbackChapterVersion(chapterId, version.id, `用户在书城确认回滚到 v${version.versionNumber}。`),
    onSuccess: async (_result, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['chapterContent', variables.chapterId] }),
        queryClient.invalidateQueries({ queryKey: ['chapterVersions', variables.chapterId] }),
        queryClient.invalidateQueries({ queryKey: ['chapterVersionCompare'] }),
        queryClient.invalidateQueries({ queryKey: ['projectChapters'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      ]);
      setRollbackDialog(null);
      setLeftVersionId('');
      setRightVersionId('');
    },
  });

  const openBookDetail = (book: NovelBookView) => {
    const project = projectsList.find((item) => item.id === book.projectId);
    if (project) setCurrentProject(toNovelProjectInfo(project));
    setSelectedChapterId(null);
    setMode('detail');
  };

  const openReader = (chapter?: NovelChapterView | null) => {
    if (chapter) setSelectedChapterId(chapter.chapterId);
    setMode('reader');
  };

  const openBookMenu = (event: MouseEvent<HTMLElement>, book: NovelBookView) => {
    event.preventDefault();
    event.stopPropagation();
    setBookMenu({ book, x: event.clientX, y: event.clientY });
  };

  const openBookDialog = (mode: BookDialogState['mode'], book: NovelBookView) => {
    setBookMenu(null);
    setBookDialog({ mode, book });
    setBookTitleDraft(mode === 'rename' ? book.title : '');
  };

  const closeBookDialog = () => {
    if (renameMutation.isPending || archiveMutation.isPending || deleteMutation.isPending) return;
    setBookDialog(null);
    setBookTitleDraft('');
  };

  const submitBookDialog = async () => {
    if (!bookDialog) return;
    const { book, mode } = bookDialog;

    if (mode === 'rename') {
      const title = bookTitleDraft.trim();
      if (!title || title === book.title) {
        closeBookDialog();
        return;
      }
      await renameMutation.mutateAsync({ projectId: book.projectId, title });
    } else if (mode === 'archive') {
      await archiveMutation.mutateAsync(book.projectId);
    } else {
      await deleteMutation.mutateAsync(book.projectId);
    }

    setBookDialog(null);
    setBookTitleDraft('');
  };

  const saveCover = async () => {
    if (!selectedBook) return;
    const coverImageUrl = coverDraftUrl.trim();
    if (coverImageUrl === (selectedBook.coverImageUrl ?? '')) {
      setIsCoverEditorOpen(false);
      return;
    }

    await coverMutation.mutateAsync({ projectId: selectedBook.projectId, coverImageUrl });
    setIsCoverEditorOpen(false);
  };

  return (
    <>
      <Topbar
        className={mode === 'reader' ? 'reader-library-topbar' : undefined}
        title={mode === 'reader' ? undefined : '小说书城'}
        center={mode === 'reader' && readerChapter ? (
          <div className="reader-topbar-title">
            {readerChapter.title}
          </div>
        ) : undefined}
        actions={
          mode !== 'store' ? (
            <>
              {mode === 'reader' && readerChapter && (
                <div className="reader-font-controls reader-topbar-controls" aria-label="字号控制">
                  <button
                    type="button"
                    aria-label="缩小字号"
                    disabled={readerFontSize <= 16}
                    onClick={() => setReaderFontSize((value) => Math.max(16, value - 1))}
                  >
                    -
                  </button>
                  <span>字号 {readerFontSize}</span>
                  <button
                    type="button"
                    aria-label="放大字号"
                    disabled={readerFontSize >= 26}
                    onClick={() => setReaderFontSize((value) => Math.min(26, value + 1))}
                  >
                    +
                  </button>
                </div>
              )}
              <button className="ghost-button reader-return-button" onClick={() => setMode('store')}>
                返回书城
              </button>
            </>
          ) : null
        }
      />

      <div className="library-page magazine-library">
        {mode === 'store' ? (
          <section className="magazine-shelf">
            <header className="magazine-shelf-head">
              <div className="magazine-shelf-meta">
                {user && (
                  <div className="user-info-stats">
                    <strong>{user.username}</strong>
                    <span> · </span>
                    <span>{userStats?.projectCount ?? books.length} 个项目</span>
                    {userStats && userStats.storageUsedMb > 0 && (
                      <>
                        <span> · </span>
                        <span>{userStats.storageUsedMb.toFixed(2)} MB 已使用</span>
                      </>
                    )}
                  </div>
                )}
                <span>{libraryBooks.length} 本成稿</span>
              </div>
            </header>

            {isLoading ? (
              <div className="empty shelf-empty">正在加载小说库...</div>
            ) : books.length === 0 ? (
              <div className="empty shelf-empty">
                <div className="empty-state-icon">📚</div>
                <h3>您还没有创建任何项目</h3>
                <p>开始您的创作之旅，让 AI Agent 帮助您创作第一部小说。</p>
                <p className="empty-hint">前往 Agent 对话页面，告诉 Agent 您的创意想法即可开始。</p>
              </div>
            ) : libraryBooks.length === 0 ? (
              <div className="empty shelf-empty">还没有已入库成稿。生成并确认提交章节后会出现在这里。</div>
            ) : (
              <div className="magazine-grid">
                {libraryBooks.map((book) => (
                  <article
                    key={book.projectId}
                    className="magazine-book"
                    role="button"
                    tabIndex={0}
                    title={`查看《${book.title}》档案`}
                    onClick={() => openBookDetail(book)}
                    onContextMenu={(event) => openBookMenu(event, book)}
                    onKeyDown={(event) => {
                      if (event.key !== 'Enter' && event.key !== ' ') return;
                      event.preventDefault();
                      openBookDetail(book);
                    }}
                  >
                    <div
                      className="magazine-cover"
                      aria-hidden="true"
                      style={book.coverImageUrl ? { backgroundImage: `linear-gradient(180deg, rgba(6, 4, 3, 0.08), rgba(6, 4, 3, 0.76)), url("${book.coverImageUrl}")` } : undefined}
                    >
                      <span>{book.genre || book.status}</span>
                      <strong>{book.title}</strong>
                      <em>{coverMark(book.title)}</em>
                    </div>
                    <div className="magazine-book-copy">
                      <div className="magazine-book-meta">
                        <span>{book.generatedChapterCount} / {book.plannedChapterCount || book.generatedChapterCount} 章入库</span>
                        <em>{book.status || 'Writing'}</em>
                      </div>
                      <h3>{book.title}</h3>
                      <p>{book.readerPromise || book.coreHook || book.selectedChapter?.summary || '这本书已经有确认入库的章节。'}</p>
                      <i className="magazine-book-progress"><b style={{ width: `${libraryProgressPercent(book)}%` }} /></i>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        ) : mode === 'detail' ? (
          <section className="magazine-profile">
            <aside className="profile-toc library-toc-panel">
              <div className="reader-book-head">
                <div className="mini-cover">{coverMark(selectedBook?.title || '')}</div>
                <div>
                  <strong>{selectedBook?.title || '未命名小说'}</strong>
                  <small>{readyChapters}/{plannedChapters} 章已入库</small>
                </div>
              </div>

              <div className="volume-list">
                {filteredVolumes.length === 0 ? (
                  <p>暂无已入库章节。</p>
                ) : filteredVolumes.map((volume) => (
                  <div key={volume.volumeId} className="volume-group">
                    <div className="volume-title">
                      <strong>{volume.title}</strong>
                      <span>{volume.chapters.length} 章</span>
                    </div>
                    <div className="chapter-list">
                      {volume.chapters.map((chapter) => (
                        <button key={chapter.chapterId} className="chapter-row" type="button" onClick={() => openReader(chapter)}>
                          <span className="chapter-index">{chapter.beatIndex || '-'}</span>
                          <span>
                            <strong>{chapter.title}</strong>
                            <small>{chapter.chapterId} · {chapterStatus(chapter)}</small>
                          </span>
                          <em className={chapterStatusClass(chapter)}>读</em>
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </aside>
            <main
              className="profile-hero"
              style={selectedBook?.coverImageUrl ? { backgroundImage: `linear-gradient(90deg, rgba(5, 4, 3, 0.86), rgba(5, 4, 3, 0.54) 48%, rgba(5, 4, 3, 0.18)), url("${selectedBook.coverImageUrl}")` } : undefined}
            >
              <div className="profile-hero-copy">
                <span>{selectedBook?.genre || 'Novel Profile'}</span>
                <h2>{selectedBook?.title || '未命名小说'}</h2>
                <p>{selectedBook?.readerPromise || selectedBook?.coreHook || '这本书的阅读承诺会在 Story Bible 固化后展示。'}</p>
                <div className="profile-stats">
                  <strong>{filteredVolumes.length}<small>卷</small></strong>
                  <strong>{readyChapters}<small>入库章节</small></strong>
                  <strong>{plannedChapters}<small>规划章节</small></strong>
                </div>
                <div className="profile-actions">
                  <button className="ink-button" onClick={() => openReader(selectedChapter)} disabled={bibleLoading || volumesLoading || allChapters.length === 0}>
                    进入阅读
                  </button>
                  {selectedBook && (
                    <button
                      className="ghost-button"
                      type="button"
                      onClick={() => setIsCoverEditorOpen((value) => !value)}
                      disabled={coverMutation.isPending}
                    >
                      更换封面
                    </button>
                  )}
                </div>
                {selectedBook && isCoverEditorOpen && (
                  <form
                    className="cover-edit-panel"
                    onSubmit={(event) => {
                      event.preventDefault();
                      void saveCover();
                    }}
                  >
                    <input
                      value={coverDraftUrl}
                      onChange={(event) => setCoverDraftUrl(event.target.value)}
                      placeholder="粘贴封面图片 URL"
                      aria-label="封面图片 URL"
                    />
                    <button className="ink-button" type="submit" disabled={coverMutation.isPending}>
                      {coverMutation.isPending ? '保存中...' : '保存'}
                    </button>
                    <button
                      className="ghost-button"
                      type="button"
                      onClick={() => {
                        setCoverDraftUrl(selectedBook.coverImageUrl ?? '');
                        setIsCoverEditorOpen(false);
                      }}
                      disabled={coverMutation.isPending}
                    >
                      取消
                    </button>
                  </form>
                )}
              </div>
              <em aria-hidden="true">{coverMark(selectedBook?.title || '')}</em>
            </main>
          </section>
        ) : (
          <section className="magazine-reader">
            <aside className="reader-index library-toc-panel">
              <div className="reader-book-head">
                <div className="mini-cover">{coverMark(selectedBook?.title || '')}</div>
                <div>
                  <strong>{selectedBook?.title || '未命名小说'}</strong>
                  <small>{readyChapters}/{plannedChapters} 章已入库</small>
                </div>
              </div>

              <div className="volume-list">
                {filteredVolumes.map((volume) => (
                  <div key={volume.volumeId} className="volume-group">
                    <div className="volume-title">
                      <strong>{volume.title}</strong>
                      <span>{volume.chapters.length} 章</span>
                    </div>
                    <div className="chapter-list">
                      {volume.chapters.map((chapter) => (
                        <button
                          key={chapter.chapterId}
                          className={`chapter-row ${selectedChapter?.chapterId === chapter.chapterId ? 'active' : ''}`}
                          type="button"
                          onClick={() => setSelectedChapterId(chapter.chapterId)}
                        >
                          <span className="chapter-index">{chapter.beatIndex || '-'}</span>
                          <span>
                            <strong>{chapter.title}</strong>
                            <small>{chapter.chapterId} · {chapterStatus(chapter)}</small>
                          </span>
                          <em className={chapterStatusClass(chapter)}>读</em>
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </aside>

            <main className="magazine-paper">
              {readerChapter ? (
                <>
                  <article className="paper-content" style={{ fontSize: `${readerFontSize}px` }}>
                    {selectedChapterContent.isLoading && !readerChapter.content ? (
                      <p>正在加载章节正文...</p>
                    ) : readerParagraphs.length > 0 ? (
                      readerParagraphs.map((paragraph, index) => (
                        <p key={index}>{paragraph}</p>
                      ))
                    ) : (
                      <p>正文暂未读取到，请稍后刷新。</p>
                    )}
                  </article>

                  <section className="reader-production-panel" aria-label="章节生产链路">
                    <header>
                      <div>
                        <strong>生产链路</strong>
                        <span>
                          {selectedChapterContent.isLoading
                            ? '读取中'
                            : currentProductionChain
                              ? productionStatusLabel(currentProductionChain.status)
                              : '暂无记录'}
                        </span>
                      </div>
                      {currentProductionChain && (
                        <em>{currentProductionChain.summary || currentProductionChain.packageId || currentProductionChain.runtimeRunId}</em>
                      )}
                    </header>

                    {currentProductionChain ? (
                      <>
                        <div className="production-evidence-grid">
                          <span>
                            <strong>版本</strong>
                            <em>{currentProductionChain.chapterVersionNumber > 0 ? `v${currentProductionChain.chapterVersionNumber}` : '未记录'}</em>
                          </span>
                          <span>
                            <strong>事实快照</strong>
                            <em>{currentProductionChain.factSnapshotVersion > 0 ? `v${currentProductionChain.factSnapshotVersion}` : '未记录'}</em>
                          </span>
                          <span>
                            <strong>修订计划</strong>
                            <em>{currentProductionChain.revisionPlanIds.length || 0} 个</em>
                          </span>
                          <span>
                            <strong>步骤</strong>
                            <em>{currentProductionChain.steps.length} 步</em>
                          </span>
                        </div>
                        <details className="production-step-details">
                          <summary>查看步骤</summary>
                          <div className="production-step-list">
                            {currentProductionChain.steps.map((step) => (
                              <div key={step.eventId || `${step.key}-${step.createdAt}`} className="production-step-row">
                                <span className={productionStatusClass(step.status)}>{productionStatusLabel(step.status)}</span>
                                <strong>{step.label || step.key}</strong>
                                <em>{step.message || step.artifactId || step.stage}</em>
                              </div>
                            ))}
                          </div>
                        </details>
                        {readerProductionEvidence && (
                          <div className="production-proof-list" aria-label="章节生产证据">
                            <div className="production-proof-card">
                              <span>修订</span>
                              <strong>{readerProductionEvidence.revisionPlans.length} 个计划</strong>
                              <em>
                                {readerProductionEvidence.revisionPlans[0]?.recommendation ||
                                  readerProductionEvidence.revisionPlans[0]?.status ||
                                  '无关联修订计划'}
                              </em>
                            </div>
                            <div className="production-proof-card">
                              <span>事实</span>
                              <strong>
                                {readerProductionEvidence.latestFactSnapshot
                                  ? `v${readerProductionEvidence.latestFactSnapshot.versionNumber}`
                                  : '未沉淀'}
                              </strong>
                              <em>
                                {readerProductionEvidence.latestFactSnapshot?.snapshotPreview || '暂无 FactSnapshot'}
                              </em>
                            </div>
                            <div className="production-proof-card">
                              <span>后台</span>
                              <strong>{readerProductionEvidence.outboxEvents.length} 个任务</strong>
                              <em>
                                {readerProductionEvidence.outboxEvents[0]
                                  ? `${productionStatusLabel(readerProductionEvidence.outboxEvents[0].status)} · ${readerProductionEvidence.outboxEvents[0].eventType}`
                                  : '暂无后台任务'}
                              </em>
                            </div>
                          </div>
                        )}
                      </>
                    ) : (
                      <p className="version-empty">当前章节还没有关联到天命生产链路。</p>
                    )}
                  </section>

                  <section className="reader-version-panel" aria-label="章节版本历史">
                    <header>
                      <div>
                        <strong>版本历史</strong>
                        <span>{chapterVersionsQuery.isError ? '读取失败' : chapterVersionsQuery.isLoading ? '读取中' : `${orderedVersions.length} 个版本`}</span>
                      </div>
                      {compareQuery.data && (
                        <em>{compareQuery.data.summary}</em>
                      )}
                    </header>

                    {chapterVersionsQuery.isError ? (
                      <p className="version-empty">版本记录读取失败，请确认后端已更新并稍后刷新。</p>
                    ) : orderedVersions.length === 0 && chapterVersionsQuery.isLoading ? (
                      <p className="version-empty">正在读取版本记录...</p>
                    ) : orderedVersions.length < 2 ? (
                      <p className="version-empty">当前章节还没有可对比的历史版本。</p>
                    ) : (
                      <>
                        <div className="version-rail" role="list" aria-label="选择对比版本">
                          {orderedVersions.map((version) => {
                            const isLeft = version.id === leftVersionId;
                            const isRight = version.id === rightVersionId;
                            return (
                              <button
                                key={version.id}
                                type="button"
                                className={`version-chip ${isLeft ? 'left' : ''} ${isRight ? 'right' : ''}`}
                                title={`${version.title} · ${version.contentPreview}`}
                                onClick={() => {
                                  if (isRight) {
                                    setRightVersionId('');
                                    return;
                                  }
                                  if (!rightVersionId || version.versionNumber >= (orderedVersions.find((item) => item.id === rightVersionId)?.versionNumber ?? 0)) {
                                    setRightVersionId(version.id);
                                  } else {
                                    setLeftVersionId(version.id);
                                  }
                                }}
                              >
                                <strong>{versionLabel(version)}</strong>
                                <span>{version.wordCount} 字</span>
                              </button>
                            );
                          })}
                        </div>

                        <div className="version-compare-controls">
                          <span>基准：{leftVersion ? `v${leftVersion.versionNumber}` : '未选'}</span>
                          <span>目标：{rightVersion ? `v${rightVersion.versionNumber}` : '未选'}</span>
                          <button
                            type="button"
                            className="ghost-button"
                            disabled={!leftVersionId || !rightVersionId || leftVersionId === rightVersionId}
                            onClick={() => {
                              setLeftVersionId(rightVersionId);
                              setRightVersionId(leftVersionId);
                            }}
                          >
                            交换
                          </button>
                          <button
                            type="button"
                            className="ghost-button danger"
                            disabled={!readerChapter || !leftVersion || leftVersion.isCurrent || rollbackMutation.isPending}
                            onClick={() => {
                              if (!readerChapter || !leftVersion || leftVersion.isCurrent) return;
                              setRollbackDialog({ chapterId: readerChapter.chapterId, version: leftVersion });
                            }}
                          >
                            回滚到基准版本
                          </button>
                        </div>

                        {alignmentChips(compareQuery.data?.productionAlignment).length > 0 && (
                          <div className="version-alignment-strip" aria-label="生产对照证据">
                            {alignmentChips(compareQuery.data?.productionAlignment).map((chip) => (
                              <span key={chip.key} className={chip.tone} title={chip.value}>
                                <strong>{chip.label}</strong>
                                <em>{chip.value}</em>
                              </span>
                            ))}
                          </div>
                        )}

                        <div className="version-diff-list">
                          {compareQuery.isLoading ? (
                            <p className="version-empty">正在对比版本差异...</p>
                          ) : compareQuery.isError ? (
                            <p className="version-empty">版本差异读取失败，请稍后重试。</p>
                          ) : visibleDiffBlocks.length > 0 ? (
                            visibleDiffBlocks.map((block, index) => (
                              <div key={`${block.kind}-${index}`} className={`version-diff ${block.kind}`}>
                                <span>{diffLabel(block)}</span>
                                <p>{diffText(block)}</p>
                              </div>
                            ))
                          ) : (
                            <p className="version-empty">两个版本正文没有明显段落差异。</p>
                          )}
                        </div>
                      </>
                    )}
                  </section>

                  <footer className="reader-bottom-bar">
                    <nav className="reader-chapter-nav" aria-label="章节导航">
                      <button
                        type="button"
                        className="ghost-button"
                        disabled={!previousChapter}
                        onClick={() => previousChapter && setSelectedChapterId(previousChapter.chapterId)}
                      >
                        <span>上一章</span>
                        <strong>{previousChapter?.title ?? '已经是第一章'}</strong>
                      </button>
                      <button type="button" className="ghost-button" onClick={() => setMode('detail')}>
                        <span>返回目录</span>
                        <strong>{selectedBook?.title || '小说档案'}</strong>
                      </button>
                      <button
                        type="button"
                        className="ghost-button"
                        disabled={!nextChapter}
                        onClick={() => nextChapter && setSelectedChapterId(nextChapter.chapterId)}
                      >
                        <span>下一章</span>
                        <strong>{nextChapter?.title ?? '已经是最后一章'}</strong>
                      </button>
                    </nav>
                  </footer>

                  {(readerChapter.reviewChecks.length > 0 || readerChapter.nextSuggestions.length > 0) && (
                    <aside className="paper-notes">
                      {readerChapter.reviewChecks.length > 0 && (
                        <div>
                          <h3>审稿检查</h3>
                          {readerChapter.reviewChecks.map((check, index) => <p key={index}>{check}</p>)}
                        </div>
                      )}
                      {readerChapter.nextSuggestions.length > 0 && (
                        <div>
                          <h3>下一章提示</h3>
                          {readerChapter.nextSuggestions.map((suggestion, index) => <p key={index}>{suggestion}</p>)}
                        </div>
                      )}
                    </aside>
                  )}
                </>
              ) : (
                <div className="empty shelf-empty">选择章节后阅读正文</div>
              )}
            </main>
          </section>
        )}
      </div>

      {bookMenu && (
        <div
          className="magazine-context-menu"
          style={{ left: bookMenu.x, top: bookMenu.y }}
          onClick={(event) => event.stopPropagation()}
          onContextMenu={(event) => {
            event.preventDefault();
            event.stopPropagation();
          }}
        >
          <button type="button" onClick={() => openBookDialog('rename', bookMenu.book)}>
            改名
          </button>
          <button
            type="button"
            onClick={() => openBookDialog('archive', bookMenu.book)}
            disabled={archiveMutation.isPending}
          >
            归档
          </button>
          <button
            type="button"
            className="danger"
            onClick={() => openBookDialog('delete', bookMenu.book)}
            disabled={deleteMutation.isPending || books.length <= 1}
          >
            删除
          </button>
        </div>
      )}

      {bookDialog && (
        <div
          className="magazine-dialog-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) closeBookDialog();
          }}
        >
          <section
            className="magazine-action-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="magazine-action-dialog-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="magazine-action-dialog-head">
              <span>{bookDialog.mode === 'rename' ? 'RENAME BOOK' : bookDialog.mode === 'archive' ? 'ARCHIVE BOOK' : 'DELETE BOOK'}</span>
              <h3 id="magazine-action-dialog-title">
                {bookDialog.mode === 'rename' ? '重命名小说' : bookDialog.mode === 'archive' ? '归档小说' : '删除小说'}
              </h3>
            </div>

            {bookDialog.mode === 'rename' ? (
              <label className="magazine-action-field">
                <span>书名</span>
                <input
                  autoFocus
                  value={bookTitleDraft}
                  placeholder="输入新的书名"
                  onChange={(event) => setBookTitleDraft(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void submitBookDialog();
                    if (event.key === 'Escape') closeBookDialog();
                  }}
                />
              </label>
            ) : (
              <p className="magazine-action-copy">
                {bookDialog.mode === 'archive'
                  ? `归档「${bookDialog.book.title}」后，它会从当前书城列表隐藏，可在工作流归档入口恢复。`
                  : `确认从书架移除「${bookDialog.book.title}」吗？本地工程文件会保留。`}
              </p>
            )}

            <div className="magazine-action-dialog-actions">
              <button
                className="ghost-button compact"
                type="button"
                onClick={closeBookDialog}
                disabled={renameMutation.isPending || archiveMutation.isPending || deleteMutation.isPending}
              >
                取消
              </button>
              <button
                className={bookDialog.mode === 'delete' ? 'danger-button compact' : 'ink-button'}
                type="button"
                onClick={() => void submitBookDialog()}
                disabled={
                  renameMutation.isPending ||
                  archiveMutation.isPending ||
                  deleteMutation.isPending ||
                  (bookDialog.mode === 'rename' && !bookTitleDraft.trim())
                }
              >
                {renameMutation.isPending || archiveMutation.isPending || deleteMutation.isPending ? '处理中...' : '确认'}
              </button>
            </div>
          </section>
        </div>
      )}

      {rollbackDialog && (
        <div
          className="magazine-dialog-backdrop"
          role="presentation"
          onMouseDown={(event) => {
            if (event.target === event.currentTarget && !rollbackMutation.isPending) {
              setRollbackDialog(null);
            }
          }}
        >
          <section
            className="magazine-action-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="version-rollback-dialog-title"
            onMouseDown={(event) => event.stopPropagation()}
          >
            <div className="magazine-action-dialog-head">
              <span>VERSION ROLLBACK</span>
              <h3 id="version-rollback-dialog-title">确认回滚章节版本</h3>
            </div>
            <p className="magazine-action-copy">
              将当前书城正文切换到 v{rollbackDialog.version.versionNumber}「{rollbackDialog.version.title}」。
              当前章及下游章节的生产包会被标记为过期，并排队重建索引和事实快照。
            </p>
            <div className="version-rollback-summary">
              <span>{rollbackDialog.version.wordCount} 字</span>
              <span>{rollbackDialog.version.status}</span>
              {rollbackDialog.version.packageId && <span>{rollbackDialog.version.packageId}</span>}
            </div>
            {rollbackMutation.isError && (
              <p className="version-rollback-error">回滚失败，请确认后端已更新并稍后重试。</p>
            )}
            <div className="magazine-action-dialog-actions">
              <button
                className="ghost-button compact"
                type="button"
                onClick={() => setRollbackDialog(null)}
                disabled={rollbackMutation.isPending}
              >
                取消
              </button>
              <button
                className="danger-button compact"
                type="button"
                onClick={() => void rollbackMutation.mutateAsync(rollbackDialog)}
                disabled={rollbackMutation.isPending}
              >
                {rollbackMutation.isPending ? '回滚中...' : '确认回滚'}
              </button>
            </div>
          </section>
        </div>
      )}
    </>
  );
}
