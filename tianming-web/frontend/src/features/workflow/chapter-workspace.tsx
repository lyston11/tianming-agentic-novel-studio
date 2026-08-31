import { useState } from 'react';
import type { GoalChapterDetailView, GoalChapterManualEditRequest } from '@/api/types';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { draftContent } from './goal-workflow-utils';

interface ChapterWorkspaceProps {
  detail?: GoalChapterDetailView;
  loading: boolean;
  onAccept: () => void;
  disabled: boolean;
  saveManualEdit: (input: { chapterNumber: number; request: GoalChapterManualEditRequest }) => Promise<unknown>;
}

export function ChapterWorkspace({ detail, loading, onAccept, disabled, saveManualEdit }: ChapterWorkspaceProps) {
  const content = detail ? draftContent(detail.draftArtifact.contentJson) : '';
  const [editDraft, setEditDraft] = useState<{ candidateId: string; content: string } | null>(null);

  if (loading) {
    return (
      <section className="rounded-xl border bg-card p-6 text-sm text-muted-foreground">加载章节...</section>
    );
  }
  if (!detail) {
    return (
      <section className="rounded-xl border border-dashed bg-card/50 p-6 text-sm text-muted-foreground">
        选择一个候选章节
      </section>
    );
  }

  const editing = editDraft?.candidateId === detail.candidate.id;
  const editedContent = editing ? editDraft.content : content;
  const save = async () => {
    if (!editing || !editedContent.trim() || editedContent === content) return;
    await saveManualEdit({
      chapterNumber: detail.candidate.chapterNumber,
      request: {
        candidateChapterId: detail.candidate.id,
        candidateVersion: detail.candidate.version,
        content: editedContent,
      },
    });
    setEditDraft(null);
  };

  return (
    <section className="rounded-xl border bg-card">
      <header className="flex flex-wrap items-center justify-between gap-2 border-b p-4">
        <div>
          <span className="font-mono text-[10px] tracking-wide text-muted-foreground uppercase">候选正文</span>
          <h2 className="font-serif text-lg font-bold">第 {detail.candidate.chapterNumber} 章</h2>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-muted-foreground">
            {detail.candidate.authorship === 'human' ? '人工版本' : 'Agent 版本'}
          </span>
          {editing ? (
            <>
              <Button variant="ghost" size="sm" onClick={() => setEditDraft(null)} disabled={disabled}>
                取消
              </Button>
              <Button
                size="sm"
                onClick={() => void save()}
                disabled={disabled || !editedContent.trim() || editedContent === content}
              >
                保存人工版本
              </Button>
            </>
          ) : (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setEditDraft({ candidateId: detail.candidate.id, content })}
              disabled={disabled}
            >
              编辑正文
            </Button>
          )}
          <Button size="sm" onClick={onAccept} disabled={disabled}>
            接受本章
          </Button>
        </div>
      </header>

      {editing ? (
        <div className="p-4">
          <Textarea
            className="min-h-[320px] font-serif"
            value={editedContent}
            onChange={(event) => setEditDraft({ candidateId: detail.candidate.id, content: event.target.value })}
            aria-label="人工编辑候选正文"
          />
        </div>
      ) : (
        <article className="whitespace-pre-wrap p-4 font-serif text-sm leading-relaxed" data-artifact-id={detail.draftArtifact.id}>
          {content}
        </article>
      )}

      <div className="grid gap-4 border-t p-4 sm:grid-cols-2">
        <section>
          <header className="mb-2 flex items-baseline justify-between text-xs text-muted-foreground">
            <strong className="text-foreground">审校证据</strong>
            <span>{detail.reviewArtifacts.length}</span>
          </header>
          {detail.reviewArtifacts.map((artifact) => (
            <details key={artifact.id} className="mb-1.5 rounded-lg border px-2.5 py-1.5">
              <summary className="cursor-pointer text-xs">{artifact.artifactType}</summary>
              <pre className="mt-1.5 max-h-40 overflow-auto rounded bg-muted/50 p-2 text-[10px]">{artifact.contentJson}</pre>
            </details>
          ))}
          {detail.reviewArtifacts.length === 0 && <p className="text-xs text-muted-foreground">暂无审校产物</p>}
        </section>
        <section>
          <header className="mb-2 flex items-baseline justify-between text-xs text-muted-foreground">
            <strong className="text-foreground">知识引用</strong>
            <span>{detail.citations.length}</span>
          </header>
          {detail.citations.map((citation) => (
            <div key={citation.id} className="mb-1.5 rounded-lg border px-2.5 py-1.5 text-xs">
              <strong>{citation.purpose}</strong>
              <span className="ml-1 text-muted-foreground">
                {citation.knowledgeEntryId} · v{citation.knowledgeVersion}
              </span>
            </div>
          ))}
          {detail.citations.length === 0 && <p className="text-xs text-muted-foreground">本章未使用知识条目</p>}
        </section>
      </div>
    </section>
  );
}
