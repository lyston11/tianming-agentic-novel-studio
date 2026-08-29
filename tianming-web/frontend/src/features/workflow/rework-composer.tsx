import { useMemo, useRef, useState } from 'react';
import type { GoalChapterDetailView, GoalChapterReworkRequest } from '@/api/types';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { draftContent } from './goal-workflow-utils';

interface ReworkComposerProps {
  detail?: GoalChapterDetailView;
  sessionId: string;
  disabled: boolean;
  reworkGoalChapter: (input: { chapterNumber: number; request: GoalChapterReworkRequest }) => Promise<unknown>;
}

function newIdempotencyKey() {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? `rework-${crypto.randomUUID()}`
    : `rework-${Date.now().toString(36)}`;
}

export function ReworkComposer({ detail, sessionId, disabled, reworkGoalChapter }: ReworkComposerProps) {
  const manuscriptRef = useRef<HTMLTextAreaElement>(null);
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const content = useMemo(
    () => (detail ? draftContent(detail.draftArtifact.contentJson) : ''),
    [detail],
  );

  const submit = async () => {
    if (!detail || !description.trim() || submitting) return;
    const selectionStart = manuscriptRef.current?.selectionStart ?? 0;
    const selectionEnd = manuscriptRef.current?.selectionEnd ?? 0;
    const hasSelection = selectionEnd > selectionStart;
    setSubmitting(true);
    try {
      await reworkGoalChapter({
        chapterNumber: detail.candidate.chapterNumber,
        request: {
          candidateChapterId: detail.candidate.id,
          candidateVersion: detail.candidate.version,
          sessionId,
          userDescription: description.trim(),
          selectionStart: hasSelection ? selectionStart : null,
          selectionEnd: hasSelection ? selectionEnd : null,
          selectedText: hasSelection ? content.slice(selectionStart, selectionEnd) : '',
          idempotencyKey: newIdempotencyKey(),
        },
      });
      setDescription('');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="rounded-xl border bg-card p-4">
      <header className="mb-2 flex items-baseline justify-between text-xs text-muted-foreground">
        <strong className="text-foreground">定向返工</strong>
        <span>{detail ? `第 ${detail.candidate.chapterNumber} 章` : '-'}</span>
      </header>
      {/* Invisible-but-focusable mirror of the manuscript: selection ranges
          inside it map to character offsets in the candidate content. */}
      <textarea
        ref={manuscriptRef}
        className="h-24 w-full cursor-text rounded-lg border bg-muted/30 p-2 font-serif text-xs text-muted-foreground"
        value={content}
        readOnly
        aria-label="候选正文选区"
      />
      <Textarea
        className="mt-2"
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        placeholder="描述具体问题与期望效果"
        rows={3}
      />
      <Button
        className="mt-2"
        size="sm"
        onClick={() => void submit()}
        disabled={disabled || submitting || !detail || !description.trim()}
      >
        {submitting ? '提交中...' : '提交返工'}
      </Button>
    </section>
  );
}
