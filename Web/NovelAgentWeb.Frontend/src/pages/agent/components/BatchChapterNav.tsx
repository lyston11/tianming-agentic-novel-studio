import type { GoalChapterSummaryView } from '../../../api/types';

interface BatchChapterNavProps {
  candidates: GoalChapterSummaryView[];
  selectedChapterNumber: number | null;
  onSelect: (chapterNumber: number) => void;
}

const statusLabel: Record<string, string> = {
  candidate: '候选',
  accepted: '已接受',
  merged: '已合并',
  needs_decision: '待决定',
};

export default function BatchChapterNav({
  candidates,
  selectedChapterNumber,
  onSelect,
}: BatchChapterNavProps) {
  return (
    <nav className="goal-chapter-nav" aria-label="批次章节">
      <header>
        <span>章节批次</span>
        <strong>{candidates.length}</strong>
      </header>
      <div className="goal-chapter-list">
        {candidates.map((candidate) => (
          <button
            type="button"
            key={`${candidate.id}:${candidate.version}`}
            className={candidate.chapterNumber === selectedChapterNumber ? 'active' : ''}
            onClick={() => onSelect(candidate.chapterNumber)}
          >
            <span className="goal-chapter-number">{String(candidate.chapterNumber).padStart(2, '0')}</span>
            <span>
              <strong>第 {candidate.chapterNumber} 章</strong>
              <small>v{candidate.version} · {statusLabel[candidate.status] ?? candidate.status}</small>
            </span>
            {candidate.isProtected && <i title="人工保护版本">保</i>}
          </button>
        ))}
        {candidates.length === 0 && <p className="goal-empty">尚无候选章节</p>}
      </div>
    </nav>
  );
}
