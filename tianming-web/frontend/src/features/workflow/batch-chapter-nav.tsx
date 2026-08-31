import type { GoalChapterSummaryView } from '@/api/types';

interface BatchChapterNavProps {
  candidates: GoalChapterSummaryView[];
  selectedChapterNumber: number | null;
  onSelect: (chapterNumber: number) => void;
}

const STATUS_LABEL: Record<string, string> = {
  candidate: '候选',
  accepted: '已接受',
  merged: '已合并',
  needs_decision: '待决定',
};

export function BatchChapterNav({ candidates, selectedChapterNumber, onSelect }: BatchChapterNavProps) {
  return (
    <nav className="rounded-xl border bg-card p-3.5" aria-label="批次章节">
      <header className="mb-2 flex items-baseline justify-between text-xs text-muted-foreground">
        <span>章节批次</span>
        <strong className="text-foreground">{candidates.length}</strong>
      </header>
      <div className="space-y-0.5">
        {candidates.map((candidate) => (
          <button
            type="button"
            key={`${candidate.id}:${candidate.version}`}
            className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm transition-colors ${
              candidate.chapterNumber === selectedChapterNumber ? 'bg-primary/10' : 'hover:bg-muted'
            }`}
            onClick={() => onSelect(candidate.chapterNumber)}
          >
            <span className="font-mono text-xs text-muted-foreground">
              {String(candidate.chapterNumber).padStart(2, '0')}
            </span>
            <span className="min-w-0 flex-1">
              <strong className="block truncate text-sm">第 {candidate.chapterNumber} 章</strong>
              <small className="text-[11px] text-muted-foreground">
                v{candidate.version} · {STATUS_LABEL[candidate.status] ?? candidate.status}
              </small>
            </span>
            {candidate.isProtected && (
              <i
                title="人工保护版本"
                className="rounded bg-muted px-1 py-0.5 text-[10px] text-muted-foreground not-italic"
              >
                保
              </i>
            )}
          </button>
        ))}
        {candidates.length === 0 && <p className="p-2 text-xs text-muted-foreground">尚无候选章节</p>}
      </div>
    </nav>
  );
}
