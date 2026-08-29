import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { updateNovelProject } from '@/api';
import type { NovelBookView, NovelVolumeView } from '@/api/types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  chapterStatus,
  chapterStatusClass,
  coverMark,
} from './library-utils';

interface BookDetailProps {
  selectedBook: NovelBookView | null;
  filteredVolumes: NovelVolumeView[];
  isLoading: boolean;
  onOpenReader: (chapter: NovelVolumeView['chapters'][number] | null) => void;
}

export function BookDetail({ selectedBook, filteredVolumes, isLoading, onOpenReader }: BookDetailProps) {
  const queryClient = useQueryClient();
  const [isCoverEditorOpen, setIsCoverEditorOpen] = useState(false);
  const [coverDraftUrl, setCoverDraftUrl] = useState('');

  const coverMutation = useMutation({
    mutationFn: ({ projectId, coverImageUrl }: { projectId: string; coverImageUrl: string }) =>
      updateNovelProject(projectId, { coverImageUrl }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
      setIsCoverEditorOpen(false);
    },
  });

  const saveCover = async () => {
    if (!selectedBook) return;
    const coverImageUrl = coverDraftUrl.trim();
    if (coverImageUrl === (selectedBook.coverImageUrl ?? '')) {
      setIsCoverEditorOpen(false);
      return;
    }
    await coverMutation.mutateAsync({ projectId: selectedBook.projectId, coverImageUrl });
  };

  const readyChapters = selectedBook?.generatedChapterCount ?? 0;
  const plannedChapters = selectedBook?.plannedChapterCount ?? 0;
  const firstChapter = filteredVolumes[0]?.chapters[0] ?? null;

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[300px_1fr]">
      <aside className="rounded-xl border bg-card p-4">
        <div className="flex items-center gap-3">
          <div className="grid size-12 shrink-0 place-items-center rounded-lg bg-primary font-serif text-2xl text-primary-foreground">
            {coverMark(selectedBook?.title || '')}
          </div>
          <div className="min-w-0">
            <strong className="block truncate font-serif">{selectedBook?.title || '未命名小说'}</strong>
            <small className="text-xs text-muted-foreground">{readyChapters}/{plannedChapters} 章已入库</small>
          </div>
        </div>

        <div className="mt-4 space-y-4">
          {filteredVolumes.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {isLoading ? '正在加载章节...' : '暂无已入库章节。'}
            </p>
          ) : (
            filteredVolumes.map((volume) => (
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
                      className="flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm transition-colors hover:bg-muted"
                      onClick={() => onOpenReader(chapter)}
                    >
                      <span className="w-8 shrink-0 font-mono text-xs text-muted-foreground">
                        {chapter.beatIndex || '-'}
                      </span>
                      <span className="min-w-0 flex-1">
                        <strong className="block truncate">{chapter.title}</strong>
                        <small className="text-[11px] text-muted-foreground">
                          {chapterStatus(chapter)}
                        </small>
                      </span>
                      <em className={`text-[11px] not-italic ${chapterStatusClass(chapter) === 'danger' ? 'text-destructive' : 'text-primary'}`}>
                        读
                      </em>
                    </button>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      </aside>

      <main
        className="relative flex min-h-[420px] flex-col justify-end overflow-hidden rounded-xl border bg-card p-8"
        style={
          selectedBook?.coverImageUrl
            ? {
                backgroundImage: `linear-gradient(90deg, rgba(5,4,3,.86), rgba(5,4,3,.54) 48%, rgba(5,4,3,.18)), url("${selectedBook.coverImageUrl}")`,
              }
            : undefined
        }
      >
        <div className="max-w-xl">
          <span className="text-xs tracking-wide text-muted-foreground uppercase">
            {selectedBook?.genre || 'Novel Profile'}
          </span>
          <h2 className="mt-1 font-serif text-3xl font-bold">{selectedBook?.title || '未命名小说'}</h2>
          <p className="mt-3 text-sm text-muted-foreground">
            {selectedBook?.readerPromise || selectedBook?.coreHook || '这本书的阅读承诺会在 Story Bible 固化后展示。'}
          </p>

          <div className="mt-5 flex gap-8">
            <div className="font-serif text-2xl font-bold">
              {filteredVolumes.length}<small className="ml-1 text-xs font-normal text-muted-foreground">卷</small>
            </div>
            <div className="font-serif text-2xl font-bold">
              {readyChapters}<small className="ml-1 text-xs font-normal text-muted-foreground">入库章节</small>
            </div>
            <div className="font-serif text-2xl font-bold">
              {plannedChapters}<small className="ml-1 text-xs font-normal text-muted-foreground">规划章节</small>
            </div>
          </div>

          <div className="mt-5 flex items-center gap-2">
            <Button
              onClick={() => onOpenReader(firstChapter)}
              disabled={isLoading || !firstChapter}
            >
              进入阅读
            </Button>
            {selectedBook && (
              <Button
                variant="ghost"
                onClick={() => setIsCoverEditorOpen((value) => !value)}
                disabled={coverMutation.isPending}
              >
                更换封面
              </Button>
            )}
          </div>

          {selectedBook && isCoverEditorOpen && (
            <form
              className="mt-4 flex max-w-md gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                void saveCover();
              }}
            >
              <Input
                value={coverDraftUrl}
                onChange={(event) => setCoverDraftUrl(event.target.value)}
                placeholder="粘贴封面图片 URL"
                aria-label="封面图片 URL"
              />
              <Button type="submit" size="sm" disabled={coverMutation.isPending}>
                {coverMutation.isPending ? '保存中...' : '保存'}
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => {
                  setCoverDraftUrl(selectedBook.coverImageUrl ?? '');
                  setIsCoverEditorOpen(false);
                }}
                disabled={coverMutation.isPending}
              >
                取消
              </Button>
            </form>
          )}
        </div>
        <em
          aria-hidden="true"
          className="pointer-events-none absolute right-8 bottom-2 font-serif text-[160px] leading-none font-bold text-foreground/5 not-italic"
        >
          {coverMark(selectedBook?.title || '')}
        </em>
      </main>
    </div>
  );
}
