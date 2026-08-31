import { useEffect, useMemo, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import {
  getStoryBibleByProject,
  getUserStats,
  listProjectChapters,
  listProjects,
  listVolumeArcs,
  toNovelProjectInfo,
} from '@/api';
import type { ChapterResponse, NovelBookView, NovelVolumeView } from '@/api/types';
import { useAuth } from '@/features/auth/auth-context-value';
import {
  ensureProjectSelected,
  setCurrentProject,
  useProjectSelection,
} from '@/lib/project-store';
import { PageHeader } from '@/components/shared/page-header';
import { BookShelf } from './book-shelf';
import { BookDetail } from './book-detail';
import { ChapterReader } from './chapter-reader';
import { isArchivedBook, toLibraryChapter } from './library-utils';

type LibraryMode = 'store' | 'detail' | 'reader';

export default function LibraryPage() {
  const { user } = useAuth();
  const { currentProjectId } = useProjectSelection();

  const { data: projects, isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: listProjects,
    refetchInterval: 10000,
  });
  const { data: userStats } = useQuery({
    queryKey: ['userStats'],
    queryFn: getUserStats,
    refetchInterval: 30000,
  });

  const [mode, setMode] = useState<LibraryMode>('store');
  const [selectedChapterId, setSelectedChapterId] = useState<string | null>(null);

  const projectsList = useMemo(() => projects ?? [], [projects]);
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

  const { data: storyBible } = useQuery({
    queryKey: ['storyBible', currentProjectId],
    queryFn: () => (currentProjectId ? getStoryBibleByProject(currentProjectId) : Promise.resolve(null)),
    enabled: !!currentProjectId,
    refetchInterval: 10000,
  });
  const { data: volumes, isLoading: volumesLoading } = useQuery({
    queryKey: ['volumeArcs', currentProjectId],
    queryFn: () => (currentProjectId ? listVolumeArcs(currentProjectId) : Promise.resolve([])),
    enabled: !!currentProjectId,
    refetchInterval: 10000,
  });

  // Project rows are projected into the book-card view used by the library.
  const books: NovelBookView[] = useMemo(() => {
    return projectsList.map((project) => {
      const committedChapters = chaptersByProject.get(project.id) ?? [];
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
        selectedChapter: committedChapters[0] ? toLibraryChapter(committedChapters[0]) : null,
      };
    });
  }, [chaptersByProject, projectsList]);

  const libraryBooks = useMemo(
    () =>
      books.filter(
        (book) =>
          !isArchivedBook(book) &&
          (book.generatedChapterCount > 0 ||
            book.selectedChapter?.visibleInLibrary ||
            !!book.selectedChapter?.content?.trim()),
      ),
    [books],
  );

  const selectedBook = useMemo(() => {
    if (!currentProjectId) return null;
    const baseBook = books.find((book) => book.projectId === currentProjectId);
    if (!baseBook) return null;

    const projectVolumes = volumes ?? [];
    const committedChapters = chaptersByProject.get(currentProjectId) ?? [];
    const generatedChapterCount = committedChapters.length;
    const plannedChapterCount =
      committedChapters.length ||
      projectVolumes.reduce(
        (sum, vol) => sum + (vol.targetChapters || vol.currentChapters || 0),
        0,
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

  const volumeViews: NovelVolumeView[] = useMemo(() => {
    const committedChapters = currentProjectId ? chaptersByProject.get(currentProjectId) ?? [] : [];
    if (committedChapters.length > 0) {
      return [
        {
          volumeId: 'committed',
          title: '已入库章节',
          status: 'committed',
          startChapterId: committedChapters[0].id,
          endChapterId: committedChapters[committedChapters.length - 1].id,
          expectedChapterCount: committedChapters.length,
          chapters: committedChapters.map(toLibraryChapter),
        },
      ];
    }

    if (!volumes) return [];
    return volumes.map((vol) => ({
      volumeId: vol.id,
      title: vol.volumeTitle,
      status: vol.status,
      startChapterId: '',
      endChapterId: '',
      expectedChapterCount: vol.targetChapters || 0,
      chapters: [
        {
          chapterId: `${vol.id}-placeholder`,
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
        } as NovelVolumeView['chapters'][number],
      ],
    }));
  }, [chaptersByProject, currentProjectId, volumes]);

  const filteredVolumes = useMemo(
    () =>
      volumeViews
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
    return (
      allChapters.find((chapter) => chapter.chapterId === selectedChapterId) ??
      allChapters[0]
    );
  }, [allChapters, selectedChapterId]);

  useEffect(() => {
    if (projects) {
      ensureProjectSelected(projectsList.map(toNovelProjectInfo));
    }
    if (projectsList.length === 0 && mode !== 'store') {
      setMode('store');
    }
  }, [projects, projectsList, mode]);

  const openBookDetail = (book: NovelBookView) => {
    const project = projectsList.find((item) => item.id === book.projectId);
    if (project) setCurrentProject(toNovelProjectInfo(project));
    setSelectedChapterId(null);
    setMode('detail');
  };

  const openReader = (chapter: NovelVolumeView['chapters'][number] | null) => {
    if (chapter) setSelectedChapterId(chapter.chapterId);
    setMode('reader');
  };

  return (
    <div>
      <PageHeader
        title="小说书城"
        actions={
          mode !== 'store' ? (
            <button
              type="button"
              onClick={() => setMode('store')}
              className="text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
            >
              ← 返回书城
            </button>
          ) : undefined
        }
      />

      {mode === 'store' ? (
        <BookShelf
          books={books}
          libraryBooks={libraryBooks}
          isLoading={isLoading}
          username={user?.username}
          userStats={userStats}
          onOpenDetail={openBookDetail}
        />
      ) : mode === 'detail' ? (
        <BookDetail
          selectedBook={selectedBook}
          filteredVolumes={filteredVolumes}
          isLoading={volumesLoading}
          onOpenReader={openReader}
        />
      ) : (
        <ChapterReader
          selectedBook={selectedBook}
          filteredVolumes={filteredVolumes}
          allChapters={allChapters}
          selectedChapter={selectedChapter}
          onSelectChapter={setSelectedChapterId}
          onBackToDetail={() => setMode('detail')}
        />
      )}
    </div>
  );
}
