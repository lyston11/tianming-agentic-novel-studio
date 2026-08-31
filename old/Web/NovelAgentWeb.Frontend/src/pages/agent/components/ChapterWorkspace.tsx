import { useState } from 'react';
import type { GoalChapterDetailView, GoalChapterManualEditRequest } from '../../../api/types';
import { draftContent } from '../goalWorkflowUtils';

interface ChapterWorkspaceProps {
  detail?: GoalChapterDetailView;
  loading: boolean;
  onAccept: () => void;
  disabled: boolean;
  saveManualEdit: (input: { chapterNumber: number; request: GoalChapterManualEditRequest }) => Promise<unknown>;
}

export default function ChapterWorkspace({
  detail,
  loading,
  onAccept,
  disabled,
  saveManualEdit,
}: ChapterWorkspaceProps) {
  const content = detail ? draftContent(detail.draftArtifact.contentJson) : '';
  const [editDraft, setEditDraft] = useState<{ candidateId: string; content: string } | null>(null);

  if (loading) return <section className="goal-chapter-workspace goal-loading">加载章节...</section>;
  if (!detail) return <section className="goal-chapter-workspace goal-empty">选择一个候选章节</section>;

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
    <section className="goal-chapter-workspace">
      <header className="goal-workspace-header">
        <div>
          <span>候选正文</span>
          <h2>第 {detail.candidate.chapterNumber} 章</h2>
        </div>
        <div className="goal-workspace-actions">
          <span>{detail.candidate.authorship === 'human' ? '人工版本' : 'Agent 版本'}</span>
          {editing ? (
            <>
              <button type="button" onClick={() => setEditDraft(null)} disabled={disabled}>取消</button>
              <button type="button" onClick={() => void save()} disabled={disabled || !editedContent.trim() || editedContent === content}>保存人工版本</button>
            </>
          ) : (
            <button
              type="button"
              onClick={() => setEditDraft({ candidateId: detail.candidate.id, content })}
              disabled={disabled}
            >
              编辑正文
            </button>
          )}
          <button type="button" onClick={onAccept} disabled={disabled}>接受本章</button>
        </div>
      </header>
      {editing ? (
        <textarea
          className="goal-manuscript goal-manuscript-editor"
          value={editedContent}
          onChange={(event) => setEditDraft({
            candidateId: detail.candidate.id,
            content: event.target.value,
          })}
          aria-label="人工编辑候选正文"
        />
      ) : (
        <article className="goal-manuscript" data-artifact-id={detail.draftArtifact.id}>
          {content}
        </article>
      )}
      <div className="goal-evidence-band">
        <section>
          <header><strong>审校证据</strong><span>{detail.reviewArtifacts.length}</span></header>
          {detail.reviewArtifacts.map((artifact) => (
            <details key={artifact.id}>
              <summary>{artifact.artifactType}</summary>
              <pre>{artifact.contentJson}</pre>
            </details>
          ))}
          {detail.reviewArtifacts.length === 0 && <p>暂无审校产物</p>}
        </section>
        <section>
          <header><strong>知识引用</strong><span>{detail.citations.length}</span></header>
          {detail.citations.map((citation) => (
            <div className="goal-citation" key={citation.id}>
              <strong>{citation.purpose}</strong>
              <span>{citation.knowledgeEntryId} · v{citation.knowledgeVersion}</span>
            </div>
          ))}
          {detail.citations.length === 0 && <p>本章未使用知识条目</p>}
        </section>
      </div>
    </section>
  );
}
