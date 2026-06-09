import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { deleteNovelProject, updateNovelProject, getStoryBibleByProject, listVolumeArcs } from '../api';
import type { NovelBookView, NovelChapterView, NovelVolumeView } from '../api/types';
import { projectService } from '../services/projectService';
import { useAuthStore } from '../stores/authStore';
import Topbar from '../components/layout/Topbar';
import '../styles/library.css';

type LibraryMode = 'store' | 'detail' | 'reader';

function formatDate(value: string) {
  if (!value) return '尚未更新';
  return value;
}

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

export default function LibraryPage() {
  const queryClient = useQueryClient();
  const { user } = useAuthStore();
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
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);

  const projectsList = projects ?? [];
  const effectiveProjectId = selectedProjectId ?? projectsList[0]?.id ?? null;

  const { data: storyBible, isLoading: bibleLoading } = useQuery({
    queryKey: ['storyBible', effectiveProjectId],
    queryFn: () => effectiveProjectId ? getStoryBibleByProject(effectiveProjectId) : Promise.resolve(null),
    enabled: !!effectiveProjectId,
    refetchInterval: 10000,
  });

  const { data: volumes, isLoading: volumesLoading } = useQuery({
    queryKey: ['volumeArcs', effectiveProjectId],
    queryFn: () => effectiveProjectId ? listVolumeArcs(effectiveProjectId) : Promise.resolve([]),
    enabled: !!effectiveProjectId,
    refetchInterval: 10000,
  });

  // Convert project data to NovelBookView format for compatibility
  const books: NovelBookView[] = useMemo(() => {
    return projectsList.map(project => ({
      projectId: project.id,
      title: project.title,
      genre: project.genre || '',
      subGenre: project.subGenre || '',
      coreHook: project.coreHook || '',
      readerPromise: '',
      status: project.status,
      isActive: true,
      volumeCount: 0,
      generatedChapterCount: 0,
      plannedChapterCount: 0,
      needsRewriteCount: 0,
      updatedAt: project.updatedAt,
      selectedChapter: null,
    }));
  }, [projectsList]);

  // For now, show all projects in library (not just those with generated chapters)
  // TODO: We may want to fetch volume counts for all projects to filter properly
  const libraryBooks = useMemo(
    () => books,
    [books],
  );

  // Enhance the selected book with StoryBible and volume data
  const selectedBook = useMemo(() => {
    const baseBook = books.find((book) => book.projectId === effectiveProjectId);
    if (!baseBook) return null;

    const projectVolumes = volumes ?? [];
    const generatedChapterCount = projectVolumes.reduce((sum, vol) =>
      sum + (vol.currentChapters || 0), 0
    );
    const plannedChapterCount = projectVolumes.reduce((sum, vol) =>
      sum + (vol.targetChapters || vol.currentChapters || 0), 0
    );

    return {
      ...baseBook,
      coreHook: storyBible?.constitution?.coreHook || baseBook.coreHook,
      readerPromise: storyBible?.constitution?.readerPromise || '',
      genre: storyBible?.constitution?.genre || baseBook.genre,
      volumeCount: projectVolumes.length,
      generatedChapterCount,
      plannedChapterCount,
    };
  }, [books, effectiveProjectId, storyBible, volumes]);

  // Convert VolumeArcResponse[] to NovelVolumeView[] format
  const volumeViews: NovelVolumeView[] = useMemo(() => {
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
  }, [volumes]);

  // TODO: Once chapter API is available, filter by hasGeneratedContent
  const filteredVolumes = useMemo(() => volumeViews, [volumeViews]);
  const allChapters = useMemo(() => filteredVolumes.flatMap((vol) => vol.chapters), [filteredVolumes]);
  const selectedChapter = useMemo(() => {
    if (allChapters.length === 0) return null;
    return (
      allChapters.find((chapter) => chapter.chapterId === selectedChapterId) ??
      allChapters[0]
    );
  }, [allChapters, selectedChapterId]);
  const selectedVolume = filteredVolumes.find((volume) => volume.volumeId === selectedChapter?.volumeId) ?? filteredVolumes[0];
  const readyChapters = selectedBook?.generatedChapterCount ?? 0;
  const plannedChapters = selectedBook?.plannedChapterCount ?? 0;

  useEffect(() => {
    if (selectedProjectId && projectsList.some((project) => project.id === selectedProjectId)) return;
    setSelectedProjectId(projectsList[0]?.id ?? null);
    setSelectedChapterId(null);
    if (projectsList.length === 0 && mode !== 'store') setMode('store');
  }, [projectsList, mode, selectedProjectId]);

  const deleteMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: ['storyBible'] }),
        queryClient.invalidateQueries({ queryKey: ['volumeArcs'] }),
      ]);
      setSelectedProjectId(null);
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

  const openBookDetail = (book: NovelBookView) => {
    setSelectedProjectId(book.projectId);
    setSelectedChapterId(null);
    setMode('detail');
  };

  const openBookReader = (book: NovelBookView) => {
    setSelectedProjectId(book.projectId);
    setSelectedChapterId(book.selectedChapter?.chapterId ?? null);
    setMode('reader');
  };

  const openReader = (chapter?: NovelChapterView | null) => {
    if (chapter) setSelectedChapterId(chapter.chapterId);
    setMode('reader');
  };

  const deleteBook = async (book: NovelBookView) => {
    const ok = window.confirm(`确认从书架移除「${book.title}」吗？本地工程文件会保留。`);
    if (!ok) return;
    await deleteMutation.mutateAsync(book.projectId);
  };

  const renameBook = async (book: NovelBookView) => {
    const title = window.prompt('重命名小说', book.title)?.trim();
    if (!title || title === book.title) return;
    await renameMutation.mutateAsync({ projectId: book.projectId, title });
  };

  return (
    <>
      <Topbar
        title="小说书城"
        actions={
          mode !== 'store' ? (
            <button className="ghost-button" onClick={() => setMode('store')}>
              返回书城
            </button>
          ) : null
        }
      />

      <div className="library-page magazine-library">
        {mode === 'store' ? (
          <section className="magazine-shelf">
            <header className="magazine-shelf-head">
              <div>
                <span>Committed Library</span>
                <h2>小说书城</h2>
                <p>只展示确认入库的成稿；草稿和返工任务留在 Agent 工作台。</p>
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
              </div>
              <strong>{libraryBooks.length}<small>本成稿</small></strong>
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
                  <article key={book.projectId} className="magazine-book">
                    <button className="magazine-cover" type="button" onClick={() => openBookReader(book)}>
                      <span>{book.genre || book.status}</span>
                      <strong>{book.title}</strong>
                      <em>{coverMark(book.title)}</em>
                    </button>
                    <div className="magazine-book-copy">
                      <span>{book.generatedChapterCount} / {book.plannedChapterCount || book.generatedChapterCount} 章入库</span>
                      <h3>{book.title}</h3>
                      <p>{book.readerPromise || book.coreHook || book.selectedChapter?.summary || '这本书已经有确认入库的章节。'}</p>
                      <div className="magazine-actions">
                        <button className="ink-button" type="button" onClick={() => openBookReader(book)}>阅读</button>
                        <button className="ghost-button" type="button" onClick={() => openBookDetail(book)}>档案</button>
                        <button className="ghost-button" type="button" onClick={() => void renameBook(book)} disabled={renameMutation.isPending}>改名</button>
                        <button className="danger-button compact" type="button" onClick={() => void deleteBook(book)} disabled={deleteMutation.isPending || books.length <= 1}>删除</button>
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>
        ) : mode === 'detail' ? (
          <section className="magazine-profile">
            <div className="profile-cover">
              <span>{selectedBook?.genre || 'Novel'}</span>
              <strong>{selectedBook?.title || '未命名小说'}</strong>
              <em>{coverMark(selectedBook?.title || '')}</em>
            </div>
            <main className="profile-copy">
              <span>Novel Profile</span>
              <h2>{selectedBook?.title || '未命名小说'}</h2>
              <p>{selectedBook?.readerPromise || selectedBook?.coreHook || '这本书的阅读承诺会在 Story Bible 固化后展示。'}</p>
              <div className="profile-stats">
                <strong>{filteredVolumes.length}<small>卷</small></strong>
                <strong>{readyChapters}<small>入库章节</small></strong>
                <strong>{plannedChapters}<small>规划章节</small></strong>
              </div>
              <button className="ink-button" onClick={() => openReader(selectedChapter)} disabled={bibleLoading || volumesLoading || allChapters.length === 0}>
                进入阅读
              </button>
            </main>
            <aside className="profile-toc">
              <span>目录</span>
              {filteredVolumes.length === 0 ? (
                <p>暂无已入库章节。</p>
              ) : filteredVolumes.map((volume) => (
                <section key={volume.volumeId}>
                  <strong>{volume.title}</strong>
                  {volume.chapters.map((chapter) => (
                    <button key={chapter.chapterId} type="button" onClick={() => openReader(chapter)}>
                      <span>{chapter.beatIndex || '-'}</span>
                      {chapter.title}
                    </button>
                  ))}
                </section>
              ))}
            </aside>
          </section>
        ) : (
          <section className="magazine-reader">
            <aside className="reader-index">
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
              {selectedChapter ? (
                <>
                  <header className="paper-title">
                    <span>{selectedVolume?.title ?? selectedChapter.volumeTitle}</span>
                    <h1>{selectedChapter.title}</h1>
                    <div>
                      <em>{selectedChapter.chapterId}</em>
                      <em>{formatDate(selectedChapter.updatedAt)}</em>
                      <em>{selectedChapter.wordCount} 字</em>
                    </div>
                  </header>

                  <section className="paper-summary">
                    <strong>本章摘要</strong>
                    <p>{selectedChapter.summary}</p>
                  </section>

                  <article className="paper-content">
                    {selectedChapter.content.split(/\n{2,}/).map((paragraph, index) => (
                      <p key={index}>{paragraph}</p>
                    ))}
                  </article>

                  {(selectedChapter.reviewChecks.length > 0 || selectedChapter.nextSuggestions.length > 0) && (
                    <aside className="paper-notes">
                      {selectedChapter.reviewChecks.length > 0 && (
                        <div>
                          <h3>审稿检查</h3>
                          {selectedChapter.reviewChecks.map((check, index) => <p key={index}>{check}</p>)}
                        </div>
                      )}
                      {selectedChapter.nextSuggestions.length > 0 && (
                        <div>
                          <h3>下一章提示</h3>
                          {selectedChapter.nextSuggestions.map((suggestion, index) => <p key={index}>{suggestion}</p>)}
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
    </>
  );
}
