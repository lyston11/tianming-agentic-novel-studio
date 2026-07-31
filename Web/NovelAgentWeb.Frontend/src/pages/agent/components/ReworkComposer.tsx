import { useMemo, useRef, useState } from 'react';
import type { GoalChapterDetailView, GoalChapterReworkRequest } from '../../../api/types';
import { draftContent } from '../goalWorkflowUtils';

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

export default function ReworkComposer({
  detail,
  sessionId,
  disabled,
  reworkGoalChapter,
}: ReworkComposerProps) {
  const manuscriptRef = useRef<HTMLTextAreaElement>(null);
  const [description, setDescription] = useState('');
  const content = useMemo(
    () => detail ? draftContent(detail.draftArtifact.contentJson) : '',
    [detail],
  );

  const submit = async () => {
    if (!detail || !description.trim()) return;
    const selectionStart = manuscriptRef.current?.selectionStart ?? 0;
    const selectionEnd = manuscriptRef.current?.selectionEnd ?? 0;
    const hasSelection = selectionEnd > selectionStart;
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
  };

  return (
    <section className="goal-rework-composer">
      <header><strong>定向返工</strong><span>{detail ? `第 ${detail.candidate.chapterNumber} 章` : '-'}</span></header>
      <textarea ref={manuscriptRef} className="goal-selection-source" value={content} readOnly aria-label="候选正文选区" />
      <textarea
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        placeholder="描述具体问题与期望效果"
        rows={4}
      />
      <button type="button" onClick={() => void submit()} disabled={disabled || !detail || !description.trim()}>
        提交返工
      </button>
    </section>
  );
}
