import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  compareChapterVersions,
  getChapterById,
  getChapterVersions,
  rollbackChapterVersion,
} from '@/api';
import type {
  ChapterVersionResponse,
  NovelBookView,
  NovelVolumeView,
  WorkflowProductionChain,
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
import { EmptyState } from '@/components/shared/empty-state';
import {
  chapterParagraphs,
  chapterStatus,
  chapterStatusClass,
  coverMark,
  diffLabel,
  diffText,
  alignmentChips,
  latestProductionChain,
  productionStatusClass,
  productionStatusLabel,
  versionLabel,
} from './library-utils';

type LibraryChapter = NovelVolumeView['chapters'][number];

interface ChapterReaderProps {
  selectedBook: NovelBookView | null;
  filteredVolumes: NovelVolumeView[];
  allChapters: LibraryChapter[];
  selectedChapter: LibraryChapter | null;
  onSelectChapter: (chapterId: string) => void;
  onBackToDetail: () => void;
}

function productionStatusBadgeClass(status: string) {
  const normalized = productionStatusClass(status);
  if (normalized === 'completed') return 'text-emerald-600';
  if (normalized === 'blocked') return 'text-destructive';
  if (normalized === 'running') return 'text-primary';
  return 'text-muted-foreground';
}

export function ChapterReader({
  selectedBook,
  filteredVolumes,
  allChapters,
  selectedChapter,
  onSelectChapter,
  onBackToDetail,
}: ChapterReaderProps) {
  const queryClient = useQueryClient();
  const [readerFontSize, setReaderFontSize] = useState(19);
  const [leftVersionId, setLeftVersionId] = useState('');
  const [rightVersionId, setRightVersionId] = useState('');
  const [rollbackTarget, setRollbackTarget] = useState<ChapterVersionResponse | null>(null);

  const contentQuery = useQuery({
    queryKey: ['chapterContent', selectedChapter?.chapterId],
    queryFn: () => getChapterById(selectedChapter!.chapterId),
    enabled: !!selectedChapter?.chapterId && selectedChapter.visibleInLibrary,
    staleTime: 10000,
  });

  const versionsQuery = useQuery({
    queryKey: ['chapterVersions', selectedChapter?.chapterId],
    queryFn: () => getChapterVersions(selectedChapter!.chapterId),
    enabled: !!selectedChapter?.chapterId && selectedChapter.visibleInLibrary,
    staleTime: 15000,
  });

  const readerChapter = useMemo(() => {
    if (!selectedChapter) return null;
    const detail = contentQuery.data;
    if (!detail || detail.id !== selectedChapter.chapterId) return selectedChapter;
    return {
      ...selectedChapter,
      title: detail.title.startsWith('chapter-') ? `第 ${detail.chapterNumber} 章` : detail.title || selectedChapter.title,
      content: detail.content ?? selectedChapter.content,
      wordCount: detail.wordCount,
    };
  }, [selectedChapter, contentQuery.data]);

  const readerChapterIndex = readerChapter
    ? allChapters.findIndex((chapter) => chapter.chapterId === readerChapter.chapterId)
    : -1;
  const previousChapter = readerChapterIndex > 0 ? allChapters[readerChapterIndex - 1] : null;
  const nextChapter =
    readerChapterIndex >= 0 && readerChapterIndex < allChapters.length - 1
      ? allChapters[readerChapterIndex + 1]
      : null;
  const readerParagraphs = readerChapter?.content
    ? chapterParagraphs(readerChapter.content, readerChapter.title)
    : [];

  const productionChains: WorkflowProductionChain[] = contentQuery.data?.productionChains ?? [];
  const currentChain = latestProductionChain(productionChains);
  const evidence = contentQuery.data?.productionEvidence;

  const orderedVersions = useMemo(
    () => [...(versionsQuery.data ?? [])].sort((a, b) => b.versionNumber - a.versionNumber),
    [versionsQuery.data],
  );

  const canCompare =
    !!selectedChapter?.chapterId && !!leftVersionId && !!rightVersionId && leftVersionId !== rightVersionId;
  const compareQuery = useQuery({
    queryKey: ['chapterVersionCompare', selectedChapter?.chapterId, leftVersionId, rightVersionId],
    queryFn: () => compareChapterVersions(selectedChapter!.chapterId, leftVersionId, rightVersionId),
    enabled: canCompare,
    staleTime: 15000,
  });
  const visibleDiffBlocks = (compareQuery.data?.diffBlocks ?? [])
    .filter((block) => block.kind !== 'unchanged')
    .slice(0, 6);
  const chips = alignmentChips(compareQuery.data?.productionAlignment);

  const rollbackMutation = useMutation({
    mutationFn: (version: ChapterVersionResponse) =>
      rollbackChapterVersion(selectedChapter!.chapterId, version.id, `用户在书城确认回滚到 v${version.versionNumber}。`),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['chapterContent'] }),
        queryClient.invalidateQueries({ queryKey: ['chapterVersions'] }),
        queryClient.invalidateQueries({ queryKey: ['chapterVersionCompare'] }),
        queryClient.invalidateQueries({ queryKey: ['projectChapters'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      ]);
      setRollbackTarget(null);
      setLeftVersionId('');
      setRightVersionId('');
    },
  });

  const selectCompareVersion = (version: ChapterVersionResponse) => {
    const isLeft = version.id === leftVersionId;
    const isRight = version.id === rightVersionId;
    if (isLeft) {
      setLeftVersionId('');
      return;
    }
    if (isRight) {
      setRightVersionId('');
      return;
    }
    const rightNumber = orderedVersions.find((item) => item.id === rightVersionId)?.versionNumber ?? 0;
    if (!rightVersionId || version.versionNumber >= rightNumber) {
      setRightVersionId(version.id);
    } else {
      setLeftVersionId(version.id);
    }
  };

  const leftVersion = orderedVersions.find((version) => version.id === leftVersionId) ?? null;

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[300px_1fr]">
      <aside className="max-h-[calc(100dvh-140px)] overflow-y-auto rounded-xl border bg-card p-4">
        <div className="flex items-center gap-3">
          <div className="grid size-12 shrink-0 place-items-center rounded-lg bg-primary font-serif text-2xl text-primary-foreground">
            {coverMark(selectedBook?.title || '')}
          </div>
          <div className="min-w-0">
            <strong className="block truncate font-serif">{selectedBook?.title || '未命名小说'}</strong>
            <small className="text-xs text-muted-foreground">
              {selectedBook?.generatedChapterCount ?? 0}/{selectedBook?.plannedChapterCount ?? 0} 章已入库
            </small>
          </div>
        </div>

        <div className="mt-4 space-y-4">
          {filteredVolumes.map((volume) => (
            <div key={volume.volumeId}>
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <strong className="text-foreground">{volume.title}</strong>
                <span>{volume.chapters.length} 章</span>
              </div>
              <div className="mt-1.5 space-y-0.5">
                {volume.chapters.map((chapter) => (
                  <button
                    key={chapter.chapterId}
                    type="button"
                    className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm transition-colors ${
                      selectedChapter?.chapterId === chapter.chapterId ? 'bg-primary/10' : 'hover:bg-muted'
                    }`}
                    onClick={() => onSelectChapter(chapter.chapterId)}
                  >
                    <span className="w-8 shrink-0 font-mono text-xs text-muted-foreground">
                      {chapter.beatIndex || '-'}
                    </span>
                    <span className="min-w-0 flex-1">
                      <strong className="block truncate">{chapter.title}</strong>
                      <small className="text-[11px] text-muted-foreground">{chapterStatus(chapter)}</small>
                    </span>
                    <em className={`text-[11px] not-italic ${chapterStatusClass(chapter) === 'danger' ? 'text-destructive' : 'text-primary'}`}>
                      读
                    </em>
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      </aside>

      <main className="min-w-0 space-y-5">
        {readerChapter ? (
          <>
            <section className="rounded-xl border bg-card p-6 sm:p-10">
              <article
                className="mx-auto max-w-[760px]"
                style={{ fontSize: `${readerFontSize}px`, lineHeight: 1.9 }}
              >
                {contentQuery.isLoading && !readerChapter.content ? (
                  <p className="text-sm text-muted-foreground">正在加载章节正文...</p>
                ) : readerParagraphs.length > 0 ? (
                  readerParagraphs.map((paragraph, index) => (
                    <p key={index} className="mb-4 indent-8 font-serif">
                      {paragraph}
                    </p>
                  ))
                ) : (
                  <p className="text-sm text-muted-foreground">正文暂未读取到，请稍后刷新。</p>
                )}
              </article>
            </section>

            <section className="rounded-xl border bg-card p-5" aria-label="章节生产链路">
              <header className="flex items-baseline justify-between">
                <div>
                  <strong className="font-serif">生产链路</strong>
                  <span className="ml-2 text-xs text-muted-foreground">
                    {contentQuery.isLoading
                      ? '读取中'
                      : currentChain
                        ? productionStatusLabel(currentChain.status)
                        : '暂无记录'}
                  </span>
                </div>
                {currentChain && (
                  <em className="truncate text-xs text-muted-foreground not-italic">
                    {currentChain.summary || currentChain.packageId || currentChain.runtimeRunId}
                  </em>
                )}
              </header>

              {currentChain ? (
                <>
                  <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
                    {[
                      { label: '版本', value: currentChain.chapterVersionNumber > 0 ? `v${currentChain.chapterVersionNumber}` : '未记录' },
                      { label: '事实快照', value: currentChain.factSnapshotVersion > 0 ? `v${currentChain.factSnapshotVersion}` : '未记录' },
                      { label: '修订计划', value: `${currentChain.revisionPlanIds.length} 个` },
                      { label: '步骤', value: `${currentChain.steps.length} 步` },
                    ].map((item) => (
                      <div key={item.label} className="rounded-lg bg-muted/50 px-3 py-2">
                        <div className="text-[11px] text-muted-foreground">{item.label}</div>
                        <div className="text-sm font-medium">{item.value}</div>
                      </div>
                    ))}
                  </div>
                  <details className="mt-3">
                    <summary className="cursor-pointer text-xs text-muted-foreground">查看步骤</summary>
                    <div className="mt-2 space-y-1">
                      {currentChain.steps.map((step) => (
                        <div
                          key={step.eventId || `${step.key}-${step.createdAt}`}
                          className="flex items-center gap-2 text-xs"
                        >
                          <span className={productionStatusBadgeClass(step.status)}>
                            {productionStatusLabel(step.status)}
                          </span>
                          <strong>{step.label || step.key}</strong>
                          <em className="truncate text-muted-foreground not-italic">
                            {step.message || step.artifactId || step.stage}
                          </em>
                        </div>
                      ))}
                    </div>
                  </details>
                  {evidence && (
                    <div className="mt-3 grid gap-2 sm:grid-cols-3">
                      {[
                        {
                          label: '修订',
                          value: `${evidence.revisionPlans.length} 个计划`,
                          detail: evidence.revisionPlans[0]?.recommendation || evidence.revisionPlans[0]?.status || '无关联修订计划',
                        },
                        {
                          label: '事实',
                          value: evidence.latestFactSnapshot ? `v${evidence.latestFactSnapshot.versionNumber}` : '未沉淀',
                          detail: evidence.latestFactSnapshot?.snapshotPreview || '暂无 FactSnapshot',
                        },
                        {
                          label: '后台',
                          value: `${evidence.outboxEvents.length} 个任务`,
                          detail: evidence.outboxEvents[0]
                            ? `${productionStatusLabel(evidence.outboxEvents[0].status)} · ${evidence.outboxEvents[0].eventType}`
                            : '暂无后台任务',
                        },
                      ].map((card) => (
                        <div key={card.label} className="rounded-lg border p-3">
                          <div className="text-[11px] text-muted-foreground">{card.label}</div>
                          <div className="text-sm font-medium">{card.value}</div>
                          <div className="truncate text-[11px] text-muted-foreground">{card.detail}</div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              ) : (
                <p className="mt-2 text-sm text-muted-foreground">当前章节还没有关联到天命生产链路。</p>
              )}
            </section>

            <section className="rounded-xl border bg-card p-5" aria-label="章节版本历史">
              <header className="flex items-baseline justify-between">
                <div>
                  <strong className="font-serif">版本历史</strong>
                  <span className="ml-2 text-xs text-muted-foreground">
                    {versionsQuery.isError
                      ? '读取失败'
                      : versionsQuery.isLoading
                        ? '读取中'
                        : `${orderedVersions.length} 个版本`}
                  </span>
                </div>
                {compareQuery.data && (
                  <em className="truncate text-xs text-muted-foreground not-italic">{compareQuery.data.summary}</em>
                )}
              </header>

              {versionsQuery.isError ? (
                <p className="mt-2 text-sm text-muted-foreground">版本记录读取失败，请确认后端已更新并稍后刷新。</p>
              ) : orderedVersions.length < 2 ? (
                <p className="mt-2 text-sm text-muted-foreground">当前章节还没有可对比的历史版本。</p>
              ) : (
                <>
                  <div className="mt-3 flex flex-wrap gap-2" role="list" aria-label="选择对比版本">
                    {orderedVersions.map((version) => {
                      const isLeft = version.id === leftVersionId;
                      const isRight = version.id === rightVersionId;
                      return (
                        <button
                          key={version.id}
                          type="button"
                          role="listitem"
                          className={`rounded-lg border px-3 py-1.5 text-left text-xs transition-colors ${
                            isLeft
                              ? 'border-blue-500/50 bg-blue-500/10'
                              : isRight
                                ? 'border-primary/50 bg-primary/10'
                                : 'hover:bg-muted'
                          }`}
                          title={`${version.title} · ${version.contentPreview}`}
                          onClick={() => selectCompareVersion(version)}
                        >
                          <strong className="block">{versionLabel(version)}</strong>
                          <span className="text-muted-foreground">{version.wordCount} 字</span>
                        </button>
                      );
                    })}
                  </div>

                  <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                    <span>基准：{leftVersion ? `v${leftVersion.versionNumber}` : '未选'}</span>
                    <span>目标：{orderedVersions.find((v) => v.id === rightVersionId) ? `v${orderedVersions.find((v) => v.id === rightVersionId)!.versionNumber}` : '未选'}</span>
                    <Button
                      variant="ghost"
                      size="xs"
                      disabled={!leftVersionId || !rightVersionId || leftVersionId === rightVersionId}
                      onClick={() => {
                        setLeftVersionId(rightVersionId);
                        setRightVersionId(leftVersionId);
                      }}
                    >
                      交换
                    </Button>
                    <Button
                      variant="destructive"
                      size="xs"
                      disabled={!leftVersion || leftVersion.isCurrent || rollbackMutation.isPending}
                      onClick={() => leftVersion && setRollbackTarget(leftVersion)}
                    >
                      回滚到基准版本
                    </Button>
                  </div>

                  {chips.length > 0 && (
                    <div className="mt-3 flex flex-wrap gap-2" aria-label="生产对照证据">
                      {chips.map((chip) => (
                        <span
                          key={chip.key}
                          title={chip.value}
                          className="rounded-lg bg-muted/60 px-2.5 py-1 text-[11px]"
                        >
                          <strong>{chip.label}</strong>
                          <em className="ml-1 text-muted-foreground not-italic">{chip.value}</em>
                        </span>
                      ))}
                    </div>
                  )}

                  <div className="mt-3 space-y-2">
                    {compareQuery.isLoading ? (
                      <p className="text-sm text-muted-foreground">正在对比版本差异...</p>
                    ) : compareQuery.isError ? (
                      <p className="text-sm text-muted-foreground">版本差异读取失败，请稍后重试。</p>
                    ) : visibleDiffBlocks.length > 0 ? (
                      visibleDiffBlocks.map((block, index) => (
                        <div
                          key={`${block.kind}-${index}`}
                          className={`rounded-lg border px-3 py-2 text-sm ${
                            block.kind === 'added'
                              ? 'border-emerald-500/30 bg-emerald-500/5'
                              : block.kind === 'removed'
                                ? 'border-destructive/30 bg-destructive/5'
                                : 'border-chart-3/40 bg-chart-3/5'
                          }`}
                        >
                          <span className="text-[11px] text-muted-foreground">{diffLabel(block)}</span>
                          <p className="mt-1">{diffText(block)}</p>
                        </div>
                      ))
                    ) : (
                      <p className="text-sm text-muted-foreground">两个版本正文没有明显段落差异。</p>
                    )}
                  </div>
                </>
              )}
            </section>

            <nav className="grid grid-cols-3 gap-3" aria-label="章节导航">
              <Button
                variant="outline"
                className="h-auto flex-col items-start gap-0.5 py-2"
                disabled={!previousChapter}
                onClick={() => previousChapter && onSelectChapter(previousChapter.chapterId)}
              >
                <span className="text-[11px] text-muted-foreground">上一章</span>
                <strong className="w-full truncate text-left text-sm">{previousChapter?.title ?? '已经是第一章'}</strong>
              </Button>
              <Button variant="outline" className="h-auto flex-col items-start gap-0.5 py-2" onClick={onBackToDetail}>
                <span className="text-[11px] text-muted-foreground">返回目录</span>
                <strong className="w-full truncate text-left text-sm">{selectedBook?.title || '小说档案'}</strong>
              </Button>
              <Button
                variant="outline"
                className="h-auto flex-col items-start gap-0.5 py-2"
                disabled={!nextChapter}
                onClick={() => nextChapter && onSelectChapter(nextChapter.chapterId)}
              >
                <span className="text-[11px] text-muted-foreground">下一章</span>
                <strong className="w-full truncate text-left text-sm">{nextChapter?.title ?? '已经是最后一章'}</strong>
              </Button>
            </nav>

            <div className="flex items-center justify-center gap-3 text-xs text-muted-foreground">
              <Button
                variant="ghost"
                size="xs"
                disabled={readerFontSize <= 16}
                onClick={() => setReaderFontSize((value) => Math.max(16, value - 1))}
              >
                缩小字号
              </Button>
              <span>字号 {readerFontSize}</span>
              <Button
                variant="ghost"
                size="xs"
                disabled={readerFontSize >= 26}
                onClick={() => setReaderFontSize((value) => Math.min(26, value + 1))}
              >
                放大字号
              </Button>
            </div>

            {(readerChapter.reviewChecks.length > 0 || readerChapter.nextSuggestions.length > 0) && (
              <aside className="grid gap-4 sm:grid-cols-2">
                {readerChapter.reviewChecks.length > 0 && (
                  <div className="rounded-xl border bg-card p-4">
                    <h3 className="text-sm font-medium">审稿检查</h3>
                    {readerChapter.reviewChecks.map((check, index) => (
                      <p key={index} className="mt-1 text-xs text-muted-foreground">{check}</p>
                    ))}
                  </div>
                )}
                {readerChapter.nextSuggestions.length > 0 && (
                  <div className="rounded-xl border bg-card p-4">
                    <h3 className="text-sm font-medium">下一章提示</h3>
                    {readerChapter.nextSuggestions.map((suggestion, index) => (
                      <p key={index} className="mt-1 text-xs text-muted-foreground">{suggestion}</p>
                    ))}
                  </div>
                )}
              </aside>
            )}
          </>
        ) : (
          <EmptyState title="选择章节后阅读正文" />
        )}
      </main>

      <Dialog open={!!rollbackTarget} onOpenChange={(open) => !open && setRollbackTarget(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>确认回滚章节版本</DialogTitle>
            <DialogDescription>
              将当前书城正文切换到 v{rollbackTarget?.versionNumber}「{rollbackTarget?.title}」。
              当前章及下游章节的生产包会被标记为过期，并排队重建索引和事实快照。
            </DialogDescription>
          </DialogHeader>
          {rollbackTarget && (
            <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
              <span className="rounded bg-muted px-2 py-0.5">{rollbackTarget.wordCount} 字</span>
              <span className="rounded bg-muted px-2 py-0.5">{rollbackTarget.status}</span>
              {rollbackTarget.packageId && (
                <span className="truncate rounded bg-muted px-2 py-0.5">{rollbackTarget.packageId}</span>
              )}
            </div>
          )}
          {rollbackMutation.isError && (
            <p className="text-sm text-destructive">回滚失败，请确认后端已更新并稍后重试。</p>
          )}
          <DialogFooter>
            <Button variant="ghost" size="sm" onClick={() => setRollbackTarget(null)} disabled={rollbackMutation.isPending}>
              取消
            </Button>
            <Button
              variant="destructive"
              size="sm"
              disabled={rollbackMutation.isPending}
              onClick={() => rollbackTarget && void rollbackMutation.mutateAsync(rollbackTarget)}
            >
              {rollbackMutation.isPending ? '回滚中...' : '确认回滚'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
