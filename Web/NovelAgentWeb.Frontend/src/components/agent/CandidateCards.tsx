import type { MacroStoryConceptCandidate, PlotCandidate } from '../../api/types';

interface MacroProps {
  candidates: MacroStoryConceptCandidate[];
  onSelect: (title: string) => void;
}

export function MacroCandidateCards({ candidates, onSelect }: MacroProps) {
  return (
    <div className="candidate-cards">
      {candidates.map((c, i) => (
        <div key={i} className="concept-card" onClick={() => onSelect(c.title)}>
          <div className="card-title">{c.title}</div>
          <div className="card-hook">{c.coreHook}</div>
          <div className="card-scores">
            <span className="score-tag">新颖 {c.noveltyScore}</span>
            <span className="score-tag">可持续 {c.sustainabilityScore}</span>
            <span className="score-tag">类型 {c.typeMatchScore}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

interface ChapterProps {
  candidates: PlotCandidate[];
  recommended: string;
  onSelect: (title: string) => void;
}

export function ChapterCandidateCards({ candidates, recommended, onSelect }: ChapterProps) {
  return (
    <div className="candidate-cards">
      {candidates.map((c, i) => (
        <div key={i} className={`candidate-card${c.title === recommended ? ' recommended' : ''}`} onClick={() => onSelect(c.title)}>
          {c.title === recommended && <div className="recommended-badge">推荐</div>}
          <div className="card-title">{c.title}</div>
          <div className="card-twist">{c.coreTwist}</div>
          <div className="card-detail">
            角色选择: {c.characterChoice} · 代价: {c.costOrConsequence}
          </div>
          <div className="card-scores">
            <span className="score-tag">新颖 {c.noveltyScore}</span>
            <span className="score-tag">一致 {c.consistencyScore}</span>
            <span className="score-tag">戏剧 {c.dramaScore}</span>
            <span className="score-tag">总分 {c.totalScore}</span>
          </div>
          {c.recommendationReason && (
            <div className="card-reason">{c.recommendationReason}</div>
          )}
        </div>
      ))}
    </div>
  );
}
