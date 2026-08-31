import type {
  KnowledgeConstraintEvidence,
  KnowledgeDirectoryResponse,
  KnowledgeProjectUsage,
} from '@/api/types';

export type KnowledgeEntryView = {
  id: string;
  category: string;
  title: string;
  content: string;
  tags: string[];
  weight: number;
  usageCount: number;
  createdAt: string;
  isArchived: boolean;
  sourceProjectId?: string | null;
  sourceProjectTitle?: string | null;
  sourceType?: string | null;
  sourceUploadTaskId?: string | null;
  chunkIndex?: number | null;
  extractionContext?: string | null;
  projectUsageStatus?: string | null;
  projectUsageCount?: number | null;
  projectLastUsedAt?: string | null;
  projectUsages: KnowledgeProjectUsage[];
  constraintEvidence: KnowledgeConstraintEvidence[];
};

export const ALL_DIRECTORY: KnowledgeDirectoryResponse = {
  key: 'All',
  name: '全部',
  description: '查看全部知识条目',
  isSystem: true,
  entryCount: 0,
};

export const SYSTEM_DIRECTORY_META: Record<string, { name: string; description: string }> = {
  GenrePrinciple: { name: '题材原则', description: '类型承诺和读者预期' },
  TropePattern: { name: '套路模式', description: '高频套路和风险桥段' },
  AntiTropeStrategy: { name: '反套路', description: '变体、反转和规避策略' },
  StyleExample: { name: '风格示例', description: '叙述风格、对话技巧和节奏样例' },
  HardFact: { name: '硬事实', description: '角色、道具、组织、地点和规则等连续性事实' },
  ReaderPromise: { name: '读者承诺', description: '爽点、情绪和持续期待' },
  ThemeDepth: { name: '主题深度', description: '主题母题和深层表达' },
  EmotionArc: { name: '情绪线', description: '情绪推进和转折节奏' },
  RelationshipDynamic: { name: '关系动态', description: '人物关系张力' },
  ProjectUsedPattern: { name: '项目记忆', description: '项目已用桥段记忆' },
  Uncategorized: { name: '未分类', description: '尚未归入目录的知识条目' },
};

const DIRECTORY_COLORS: Record<string, string> = {
  All: 'var(--tm-gold)',
  GenrePrinciple: 'var(--tm-gold)',
  TropePattern: 'var(--tm-red)',
  AntiTropeStrategy: 'var(--tm-jade)',
  StyleExample: 'var(--tm-blue)',
  HardFact: 'var(--tm-gold)',
  ReaderPromise: 'var(--tm-blue)',
  ThemeDepth: 'var(--tm-gold)',
  EmotionArc: 'var(--tm-red)',
  RelationshipDynamic: 'var(--tm-jade)',
  ProjectUsedPattern: 'var(--tm-muted)',
  Uncategorized: 'var(--tm-muted)',
};

const LEGACY_CATEGORY_ALIASES: Record<string, string> = {
  genre_rule: 'GenrePrinciple',
  trope_pattern: 'TropePattern',
  anti_trope: 'AntiTropeStrategy',
  style_example: 'StyleExample',
  hard_fact: 'HardFact',
  reader_promise: 'ReaderPromise',
  theme_depth: 'ThemeDepth',
  emotion_arc: 'EmotionArc',
  relationship_dynamic: 'RelationshipDynamic',
  project_pattern: 'ProjectUsedPattern',
  project_used_pattern: 'ProjectUsedPattern',
  uncategorized: 'Uncategorized',
};

export function normalizeCategory(value?: string | null) {
  const category = value?.trim();
  if (!category) return 'Uncategorized';
  return LEGACY_CATEGORY_ALIASES[category.toLowerCase()] ?? category;
}

export function getShortContent(entry: KnowledgeEntryView) {
  if (entry.content.length <= 180) return entry.content;
  return `${entry.content.slice(0, 180)}...`;
}

export function usageLabel(status?: string) {
  if (status === 'referenced') return '已调用';
  if (status === 'imported') return '已导入';
  return '未调用';
}

export function hasCurrentProjectUsage(entry: KnowledgeEntryView) {
  return entry.projectUsageStatus === 'referenced' || entry.projectUsageStatus === 'imported';
}

export function getProjectUsageRows(
  entry: KnowledgeEntryView,
  currentProjectTitle?: string,
): KnowledgeProjectUsage[] {
  const usages = entry.projectUsages ?? [];
  if (usages.length > 0 || !hasCurrentProjectUsage(entry)) return usages;

  return [{
    projectId: entry.sourceProjectId ?? 'current-project',
    projectTitle: currentProjectTitle || entry.sourceProjectTitle || '当前打开项目',
    status: entry.projectUsageStatus ?? 'referenced',
    usageCount: entry.projectUsageCount ?? 0,
    firstSeenAt: entry.projectLastUsedAt ?? entry.createdAt ?? '',
    lastUsedAt: entry.projectLastUsedAt ?? null,
  }];
}

export function usageProjectCountLabel(entry: KnowledgeEntryView, currentProjectTitle?: string) {
  const count = getProjectUsageRows(entry, currentProjectTitle).length;
  return `${count} 个项目调用`;
}

export function formatUsageTime(value?: string | null) {
  if (!value) return '暂无';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '暂无';
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function evidenceStatusLabel(status?: string | null) {
  const normalized = (status ?? '').toLowerCase();
  if (normalized === 'satisfied') return '已满足';
  if (normalized === 'violated') return '已违反';
  if (normalized === 'unknown') return '未知';
  return status || '未记录';
}

export function evidenceSummary(evidence: KnowledgeConstraintEvidence) {
  return [
    evidence.projectTitle,
    evidence.chapterId,
    evidence.constraintLevel || evidence.entryType,
    evidence.gateStatus ? `Gate ${evidence.gateStatus}` : '',
    evidence.factSnapshotVersion > 0 ? `事实 v${evidence.factSnapshotVersion}` : '',
  ].filter(Boolean).join(' · ');
}

export function parseTagInput(value: string) {
  return value
    .split(/[,，;；|\n\r\t]/)
    .map((tag) => tag.trim())
    .filter(Boolean)
    .filter((tag, index, arr) => arr.findIndex((item) => item.toLowerCase() === tag.toLowerCase()) === index);
}

export function formatTags(tags?: string[]) {
  return (tags ?? []).join('，');
}

export function normalizeWeight(value: string) {
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return 5;
  return Math.min(10, Math.max(1, parsed));
}

export function directoryColor(key: string) {
  return DIRECTORY_COLORS[key] ?? 'var(--tm-gold)';
}

export function isSystemDirectoryKey(key: string) {
  return key === ALL_DIRECTORY.key || !!SYSTEM_DIRECTORY_META[key];
}

export function directoryDisplayName(directoryOrKey: KnowledgeDirectoryResponse | string) {
  const key = typeof directoryOrKey === 'string' ? directoryOrKey : directoryOrKey.key;
  const name = typeof directoryOrKey === 'string' ? '' : directoryOrKey.name;
  return key === ALL_DIRECTORY.key
    ? ALL_DIRECTORY.name
    : (SYSTEM_DIRECTORY_META[key]?.name ?? (name || key));
}

export function directoryDescription(directory: KnowledgeDirectoryResponse) {
  return directory.key === ALL_DIRECTORY.key
    ? ALL_DIRECTORY.description
    : SYSTEM_DIRECTORY_META[directory.key]?.description ?? directory.description;
}
