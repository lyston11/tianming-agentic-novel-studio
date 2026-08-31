import type {
  ChapterResponse,
  ChapterVersionDiffBlock,
  ChapterVersionProductionAlignment,
  ChapterVersionResponse,
  NovelBookView,
  NovelChapterView,
} from '@/api/types';

export function chapterStatus(chapter: NovelChapterView) {
  if (chapter.needsRewrite) return '需修订';
  if (chapter.visibleInLibrary) return '已入库';
  return chapter.status || '成稿';
}

export function chapterStatusClass(chapter: NovelChapterView) {
  if (chapter.needsRewrite) return 'danger';
  if (chapter.visibleInLibrary) return 'ready';
  return '';
}

export function coverMark(title: string) {
  return (title || '命').trim().slice(0, 1);
}

export function libraryProgressPercent(book: NovelBookView) {
  const total = book.plannedChapterCount || book.generatedChapterCount;
  if (total <= 0) return 0;
  return Math.min(100, Math.round((book.generatedChapterCount / total) * 100));
}

export function isArchivedBook(book: NovelBookView) {
  return (book.status || '').toLowerCase() === 'archived';
}

export function chapterTitle(chapter: ChapterResponse) {
  if (/^chapter-\d+$/i.test(chapter.title)) {
    return `第 ${chapter.chapterNumber} 章`;
  }

  return chapter.title || `第 ${chapter.chapterNumber} 章`;
}

function normalizeReaderLine(value: string) {
  return value
    .replace(/^#{1,6}\s*/, '')
    .replace(/\*\*/g, '')
    .trim();
}

function stripReaderTitlePrefix(value: string, title: string) {
  const line = normalizeReaderLine(value);
  const normalizedTitle = normalizeReaderLine(title);
  const chapterMatch = normalizedTitle.match(/^(第[一二三四五六七八九十百千万\d]+章)[：:\s、-]*(.*)$/);
  if (!chapterMatch) return line;

  const chapterLabel = chapterMatch[1];
  const titleCore = chapterMatch[2]?.trim();
  if (!line.startsWith(chapterLabel) || !titleCore) return line;

  const coreIndex = line.indexOf(titleCore);
  if (coreIndex < 0 || coreIndex > 16) return line;
  return line.slice(coreIndex + titleCore.length).replace(/^[：:\s、，。.-]+/, '').trim();
}

function splitLongReaderParagraph(value: string) {
  const text = value.trim();
  if (text.length <= 180) return [text];

  const parts: string[] = [];
  let rest = text;
  while (rest.length > 180) {
    const windowText = rest.slice(0, 240);
    const punctuationIndex = Math.max(
      windowText.lastIndexOf('。'),
      windowText.lastIndexOf('！'),
      windowText.lastIndexOf('？'),
      windowText.lastIndexOf('；'),
    );
    const splitAt = punctuationIndex >= 80 ? punctuationIndex + 1 : 180;
    parts.push(rest.slice(0, splitAt).trim());
    rest = rest.slice(splitAt).trim();
  }
  if (rest) parts.push(rest);
  return parts;
}

/** Reader paragraph layout: strips markdown decorations and title duplication. */
export function chapterParagraphs(content: string, title: string) {
  const normalizedTitle = normalizeReaderLine(title);
  return content
    .replace(/\r\n/g, '\n')
    .split(/\n+/)
    .map((line, index) => (index === 0 ? stripReaderTitlePrefix(line, title) : normalizeReaderLine(line)))
    .filter(Boolean)
    .filter((line, index) => {
      if (index > 1) return true;
      return line !== normalizedTitle && !normalizedTitle.includes(line) && !line.includes(normalizedTitle);
    })
    .flatMap(splitLongReaderParagraph);
}

export function versionLabel(version: ChapterVersionResponse) {
  const markers = [
    `v${version.versionNumber}`,
    version.isCurrent ? '当前' : '',
    version.status,
  ].filter(Boolean);
  return markers.join(' · ');
}

export function diffLabel(block: ChapterVersionDiffBlock) {
  if (block.kind === 'added') return '新增';
  if (block.kind === 'removed') return '删除';
  if (block.kind === 'changed') return '修改';
  return '保留';
}

export function diffText(block: ChapterVersionDiffBlock) {
  if (block.kind === 'added') return block.rightText;
  if (block.kind === 'removed') return block.leftText;
  if (block.kind === 'changed') return `旧：${block.leftText}｜新：${block.rightText}`;
  return block.rightText || block.leftText;
}

export interface AlignmentChip {
  key: string;
  label: string;
  value: string;
  tone: string;
}

export function alignmentChips(alignment?: ChapterVersionProductionAlignment | null): AlignmentChip[] {
  if (!alignment) return [];
  const chips = [
    alignment.acceptedCreativeIntents.length > 0
      ? {
          key: 'creative',
          label: `创意 ${alignment.acceptedCreativeIntents.length}`,
          value: alignment.acceptedCreativeIntents[0].normalizedIntent,
          tone: 'creative',
        }
      : null,
    alignment.sourceRevisionPlans.length > 0
      ? {
          key: 'revision',
          label: `修订 ${alignment.sourceRevisionPlans.length}`,
          value: alignment.sourceRevisionPlans[0].recommendation || alignment.sourceRevisionPlans[0].status,
          tone: 'revision',
        }
      : null,
    alignment.agentReviewDecision || alignment.agentReviewChecks.length > 0
      ? {
          key: 'review',
          label: '审稿',
          value: alignment.agentReviewDecision || `${alignment.agentReviewChecks.length} 项检查`,
          tone: 'review',
        }
      : null,
    alignment.rebuiltFromPackageIds.length > 0
      ? {
          key: 'package',
          label: `替代包 ${alignment.rebuiltFromPackageIds.length}`,
          value: alignment.rebuiltFromPackageIds.slice(0, 2).join(' / '),
          tone: 'package',
        }
      : null,
  ].filter(Boolean);
  return chips as AlignmentChip[];
}

export function productionStatusLabel(status: string) {
  const normalized = (status || '').toLowerCase();
  if (normalized === 'completed') return '已完成';
  if (normalized === 'running') return '执行中';
  if (normalized === 'blocked') return '需处理';
  if (normalized === 'failed') return '失败';
  if (normalized === 'pending') return '等待中';
  return status || '未知';
}

export function productionStatusClass(status: string) {
  const normalized = (status || '').toLowerCase();
  if (normalized === 'completed') return 'completed';
  if (normalized === 'blocked' || normalized === 'failed') return 'blocked';
  if (normalized === 'running') return 'running';
  return '';
}

export function latestProductionChain<T extends { updatedAt?: string }>(chains: T[]) {
  return [...chains].sort((a, b) => (b.updatedAt || '').localeCompare(a.updatedAt || ''))[0] ?? null;
}

/** Projects a committed ChapterResponse into the library's chapter view model. */
export function toLibraryChapter(chapter: ChapterResponse): NovelChapterView {
  return {
    chapterId: chapter.id,
    volumeId: chapter.volumeId ?? 'committed',
    volumeTitle: '已入库章节',
    title: chapterTitle(chapter),
    beatIndex: chapter.chapterNumber,
    beatRole: '',
    goal: '',
    turn: '',
    cost: '',
    status: '已入库',
    runId: '',
    intent: '',
    updatedAt: chapter.updatedAt,
    hasGeneratedContent: true,
    needsRewrite: false,
    wordCount: chapter.wordCount,
    summary: `${chapterTitle(chapter)}已提交到小说书城。`,
    content: chapter.content ?? '',
    selectedCandidateTitle: '',
    qualityScore: 0,
    rewriteAttemptCount: 0,
    reviewChecks: [],
    nextSuggestions: [],
    writingStatus: 'committed',
    contextPackageStatus: '',
    draftArtifactStatus: 'committed',
    gateStatus: 'validated',
    changesProtocolPassed: true,
    factSnapshotPassed: true,
    blueprintPassed: true,
    longDistanceRagPassed: true,
    ragRecallCount: 0,
    repairAttemptCount: 0,
    gateIssues: [],
    repairHints: [],
    dependencyWarnings: [],
    contextWarnings: [],
    visibleInWorkflow: false,
    visibleInLibrary: true,
    userVisibleStatus: '成稿已进入书城',
    artifactStatus: 'committed',
    draftArtifactId: '',
    gateReportId: '',
    qualityReportId: '',
  };
}
