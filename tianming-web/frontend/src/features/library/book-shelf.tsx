import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MoreVertical } from 'lucide-react';
import { deleteNovelProject, updateNovelProject } from '@/api';
import { setCurrentProject } from '@/lib/project-store';
import type { NovelBookView } from '@/api/types';
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
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { EmptyState } from '@/components/shared/empty-state';
import {
  coverMark,
  libraryProgressPercent,
} from './library-utils';

type BookDialogState = {
  mode: 'rename' | 'archive' | 'delete';
  book: NovelBookView;
};

interface BookShelfProps {
  books: NovelBookView[];
  libraryBooks: NovelBookView[];
  isLoading: boolean;
  username?: string;
  userStats?: { projectCount: number; storageUsedMb: number };
  onOpenDetail: (book: NovelBookView) => void;
}

export function BookShelf({
  books,
  libraryBooks,
  isLoading,
  username,
  userStats,
  onOpenDetail,
}: BookShelfProps) {
  const queryClient = useQueryClient();
  const [bookDialog, setBookDialog] = useState<BookDialogState | null>(null);
  const [bookTitleDraft, setBookTitleDraft] = useState('');

  const deleteMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['projects'] }),
        queryClient.invalidateQueries({ queryKey: ['projectChapters'] }),
      ]);
      setCurrentProject(null);
      setBookDialog(null);
    },
  });

  const renameMutation = useMutation({
    mutationFn: ({ projectId, title }: { projectId: string; title: string }) =>
      updateNovelProject(projectId, { title }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
      setBookDialog(null);
    },
  });

  const archiveMutation = useMutation({
    mutationFn: (projectId: string) => updateNovelProject(projectId, { status: 'archived' }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['projects'] });
      setBookDialog(null);
    },
  });

  const openBookDialog = (mode: BookDialogState['mode'], book: NovelBookView) => {
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
    const { mode, book } = bookDialog;

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
  };

  return (
    <div>
      <div className="mb-4 text-sm text-muted-foreground">
        {username && <strong className="text-foreground">{username}</strong>}
        {username && ' · '}
        <span>{userStats?.projectCount ?? books.length} 个项目</span>
        {userStats && userStats.storageUsedMb > 0 && (
          <span> · {userStats.storageUsedMb.toFixed(2)} MB 已使用</span>
        )}
        <span className="mx-2 text-border">|</span>
        <span>{libraryBooks.length} 本成稿</span>
      </div>

      {isLoading ? (
        <div className="text-sm text-muted-foreground">正在加载小说库...</div>
      ) : books.length === 0 ? (
        <EmptyState
          title="您还没有创建任何项目"
          description="前往 Agent 对话页面，告诉 Agent 您的创意想法即可开始创作。"
        />
      ) : libraryBooks.length === 0 ? (
        <EmptyState
          title="还没有已入库成稿"
          description="生成并确认提交章节后会出现在这里。"
        />
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {libraryBooks.map((book) => (
            <article
              key={book.projectId}
              className="group overflow-hidden rounded-xl border bg-card shadow-sm transition-shadow hover:shadow-md"
              role="button"
              tabIndex={0}
              title={`查看《${book.title}》档案`}
              onClick={() => onOpenDetail(book)}
              onKeyDown={(event) => {
                if (event.key !== 'Enter' && event.key !== ' ') return;
                event.preventDefault();
                onOpenDetail(book);
              }}
            >
              <div
                className="relative grid h-40 place-items-center bg-sidebar"
                style={
                  book.coverImageUrl
                    ? {
                        backgroundImage: `linear-gradient(180deg, rgba(6,4,3,.08), rgba(6,4,3,.76)), url("${book.coverImageUrl}")`,
                      }
                    : undefined
                }
              >
                <span className="absolute top-2 left-3 text-[11px] text-muted-foreground">
                  {book.genre || book.status}
                </span>
                <strong className="px-4 text-center font-serif text-lg text-foreground">
                  {book.title}
                </strong>
                <em className="pointer-events-none absolute right-3 bottom-1 font-serif text-6xl font-bold text-foreground/10 not-italic">
                  {coverMark(book.title)}
                </em>
              </div>
              <div className="relative p-4">
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="absolute top-2.5 right-2.5 opacity-0 transition-opacity group-hover:opacity-100"
                      onClick={(event) => event.stopPropagation()}
                    >
                      <MoreVertical />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" onClick={(event) => event.stopPropagation()}>
                    <DropdownMenuItem onClick={() => openBookDialog('rename', book)}>
                      改名
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      disabled={archiveMutation.isPending}
                      onClick={() => openBookDialog('archive', book)}
                    >
                      归档
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      variant="destructive"
                      disabled={deleteMutation.isPending || books.length <= 1}
                      onClick={() => openBookDialog('delete', book)}
                    >
                      删除
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>

                <div className="flex items-center justify-between text-[11px] text-muted-foreground">
                  <span>
                    {book.generatedChapterCount} / {book.plannedChapterCount || book.generatedChapterCount} 章入库
                  </span>
                  <em className="not-italic">{book.status || 'Writing'}</em>
                </div>
                <h3 className="mt-1.5 truncate font-serif font-semibold">{book.title}</h3>
                <p className="mt-1 line-clamp-2 min-h-10 text-xs text-muted-foreground">
                  {book.readerPromise || book.coreHook || '这本书已经有确认入库的章节。'}
                </p>
                <div className="mt-2.5 h-1 overflow-hidden rounded-full bg-muted">
                  <div
                    className="h-full rounded-full bg-primary/70"
                    style={{ width: `${libraryProgressPercent(book)}%` }}
                  />
                </div>
              </div>
            </article>
          ))}
        </div>
      )}

      <Dialog open={!!bookDialog} onOpenChange={(open) => !open && closeBookDialog()}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>
              {bookDialog?.mode === 'rename'
                ? '重命名小说'
                : bookDialog?.mode === 'archive'
                  ? '归档小说'
                  : '删除小说'}
            </DialogTitle>
            <DialogDescription>
              {bookDialog?.mode === 'archive'
                ? `归档「${bookDialog.book.title}」后，它会从当前书城列表隐藏。`
                : bookDialog?.mode === 'delete'
                  ? `确认从书架移除「${bookDialog.book.title}」吗？本地工程文件会保留。`
                  : undefined}
            </DialogDescription>
          </DialogHeader>

          {bookDialog?.mode === 'rename' && (
            <Input
              autoFocus
              value={bookTitleDraft}
              placeholder="输入新的书名"
              onChange={(event) => setBookTitleDraft(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') void submitBookDialog();
                if (event.key === 'Escape') closeBookDialog();
              }}
            />
          )}

          <DialogFooter>
            <Button variant="ghost" size="sm" onClick={closeBookDialog}>
              取消
            </Button>
            <Button
              variant={bookDialog?.mode === 'delete' ? 'destructive' : 'default'}
              size="sm"
              disabled={
                renameMutation.isPending ||
                archiveMutation.isPending ||
                deleteMutation.isPending ||
                (bookDialog?.mode === 'rename' && !bookTitleDraft.trim())
              }
              onClick={() => void submitBookDialog()}
            >
              {renameMutation.isPending || archiveMutation.isPending || deleteMutation.isPending
                ? '处理中...'
                : '确认'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
