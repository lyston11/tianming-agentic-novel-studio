import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listKnowledgeEntries, deleteKnowledgeEntryById, updateKnowledgeEntryById } from '../../api';
import type { CreativeKnowledgeCategory, CreativeKnowledgeEntry } from '../../api/types';

const CATEGORIES: { key: CreativeKnowledgeCategory | 'All'; label: string }[] = [
  { key: 'All', label: '全部' },
  { key: 'GenrePrinciple', label: '题材原则' },
  { key: 'TropePattern', label: '套路模式' },
  { key: 'AntiTropeStrategy', label: '反套路' },
  { key: 'ReaderPromise', label: '读者承诺' },
  { key: 'ThemeDepth', label: '主题深度' },
  { key: 'EmotionArc', label: '情绪线' },
  { key: 'RelationshipDynamic', label: '关系动态' },
  { key: 'ProjectUsedPattern', label: '项目记忆' },
];

const CATEGORY_COLORS: Record<string, string> = {
  GenrePrinciple: 'var(--gold)',
  TropePattern: 'var(--red)',
  AntiTropeStrategy: 'var(--jade)',
  ReaderPromise: 'var(--blue)',
  ThemeDepth: 'var(--gold)',
  EmotionArc: 'var(--red)',
  RelationshipDynamic: 'var(--jade)',
  ProjectUsedPattern: 'var(--muted)',
};

const CATEGORY_DESCRIPTIONS: Record<string, string> = {
  All: '全库视图',
  GenrePrinciple: '类型承诺和读者预期',
  TropePattern: '高频套路和风险桥段',
  AntiTropeStrategy: '变体、反转和规避策略',
  ReaderPromise: '爽点、情绪和持续期待',
  ThemeDepth: '主题母题和深层表达',
  EmotionArc: '情绪推进和转折节奏',
  RelationshipDynamic: '人物关系张力',
  ProjectUsedPattern: '项目已用桥段记忆',
};

function getCategoryLabel(category: CreativeKnowledgeCategory | 'All') {
  return CATEGORIES.find((c) => c.key === category)?.label ?? category;
}

function getShortContent(entry: CreativeKnowledgeEntry) {
  if (entry.content.length <= 160) return entry.content;
  return `${entry.content.slice(0, 160)}...`;
}

function usageLabel(status?: string) {
  if (status === 'referenced') return '本项目已引用';
  if (status === 'imported') return '本项目已导入';
  return '未用于本项目';
}

function formatUsageTime(value?: string | null) {
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

interface KnowledgeBaseBrowserProps {
  projectId: string;
  actions?: ReactNode;
}

export default function KnowledgeBaseBrowser({ projectId, actions }: KnowledgeBaseBrowserProps) {
  const queryClient = useQueryClient();
  const [selectedCategory, setSelectedCategory] = useState<CreativeKnowledgeCategory | 'All'>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editContent, setEditContent] = useState('');

  const { data: rawEntries = [] } = useQuery({
    queryKey: ['knowledgeEntries', projectId],
    queryFn: () => listKnowledgeEntries(projectId),
    enabled: !!projectId,
  });

  // Map backend KnowledgeResponse to frontend CreativeKnowledgeEntry
  const entries: CreativeKnowledgeEntry[] = useMemo(
    () => rawEntries.map(e => {
      // Validate category with fallback
      const validCategories: CreativeKnowledgeCategory[] = [
        'GenrePrinciple', 'TropePattern', 'AntiTropeStrategy', 'ReaderPromise',
        'ThemeDepth', 'EmotionArc', 'RelationshipDynamic', 'ProjectUsedPattern'
      ];
      const category = validCategories.includes(e.entryType as CreativeKnowledgeCategory)
        ? (e.entryType as CreativeKnowledgeCategory)
        : 'GenrePrinciple';

      return {
        id: e.id,
        category,
        title: e.title,
        content: e.content,
        genre: '',
        subGenre: '',
        tags: [],
        weight: 5,
        source: '',
        usageCount: e.usageCount,
        createdAt: e.createdAt,
        projectUsageStatus: e.projectUsageStatus,
        projectUsageCount: e.projectUsageCount,
        projectLastUsedAt: e.projectLastUsedAt,
      };
    }),
    [rawEntries]
  );

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteKnowledgeEntryById(id),
    onSuccess: () => {
      setEditingId(null);
      setSelectedId(null);
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', projectId] });
    },
  });

  const updateMutation = useMutation({
    mutationFn: () => {
      const base = entries.find((entry) => entry.id === editingId);
      if (!base) throw new Error('未选择知识条目');
      return updateKnowledgeEntryById(base.id, {
        title: editTitle,
        content: editContent,
      });
    },
    onSuccess: () => {
      setEditingId(null);
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', projectId] });
    },
  });

  const startEditing = (entry: CreativeKnowledgeEntry) => {
    setSelectedId(entry.id);
    setEditingId(entry.id);
    setEditTitle(entry.title);
    setEditContent(entry.content);
  };

  const categoryCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const entry of entries) {
      counts.set(entry.category, (counts.get(entry.category) ?? 0) + 1);
    }
    return counts;
  }, [entries]);

  const filtered = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return entries
      .filter((entry) => {
        if (selectedCategory !== 'All' && entry.category !== selectedCategory) return false;
        if (!q) return true;
        return (
          entry.title.toLowerCase().includes(q) ||
          entry.content.toLowerCase().includes(q)
        );
      })
      .sort((a, b) => a.title.localeCompare(b.title, 'zh-Hans-CN'));
  }, [entries, searchQuery, selectedCategory]);

  const selectedEntry = useMemo(() => {
    if (filtered.length === 0) return null;
    return filtered.find((entry) => entry.id === selectedId) ?? filtered[0];
  }, [filtered, selectedId]);

  return (
    <section className="knowledge-browser">
      <div className="knowledge-command">
        <div>
          <div className="section-kicker">Creative Knowledge Base</div>
          <h2>创意知识库</h2>
        </div>
        <div className="knowledge-command-side">
          <div className="knowledge-command-stats" aria-label="知识库统计">
            <span><strong>{entries.length}</strong> 条目</span>
            <span><strong>{filtered.length}</strong> 当前</span>
          </div>
          {actions && <div className="knowledge-command-actions">{actions}</div>}
        </div>
      </div>

      <div className="knowledge-search-row">
        <input
          className="knowledge-search"
          placeholder="搜索标题或内容"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
        />
        <div className="knowledge-scope">
          {getCategoryLabel(selectedCategory)}
        </div>
      </div>

      <div className="knowledge-workspace">
        <aside className="knowledge-category-rail" aria-label="知识分类">
          {CATEGORIES.map((cat) => {
            const count = cat.key === 'All' ? entries.length : categoryCounts.get(cat.key) ?? 0;
            return (
              <button
                key={cat.key}
                className={`knowledge-category${selectedCategory === cat.key ? ' active' : ''}`}
                onClick={() => {
                  setSelectedCategory(cat.key);
                  setSelectedId(null);
                }}
              >
                <span>
                  <strong>{cat.label}</strong>
                  <small>{CATEGORY_DESCRIPTIONS[cat.key]}</small>
                </span>
                <em>{count}</em>
              </button>
            );
          })}
        </aside>

        <div className="knowledge-results">
          {filtered.length === 0 ? (
            <div className="empty knowledge-empty">暂无知识条目</div>
          ) : (
            <div className="knowledge-list">
              {filtered.map((entry) => (
                <article
                  key={entry.id}
                  className={`knowledge-card${selectedEntry?.id === entry.id ? ' active' : ''}`}
                  onClick={() => setSelectedId(entry.id)}
                >
                  <div className="knowledge-card-header">
                    <span
                      className="category-badge"
                      style={{ color: CATEGORY_COLORS[entry.category] ?? 'var(--muted)' }}
                    >
                      {getCategoryLabel(entry.category)}
                    </span>
                    <span className={`knowledge-usage ${entry.projectUsageStatus ?? 'none'}`}>
                      {usageLabel(entry.projectUsageStatus)}
                    </span>
                  </div>
                  <div className="knowledge-title">{entry.title}</div>
                  <div className="knowledge-content">{getShortContent(entry)}</div>
                </article>
              ))}
            </div>
          )}
        </div>

        <aside className="knowledge-detail">
          {selectedEntry ? (
            <>
              <div className="knowledge-detail-head">
                <span
                  className="category-badge"
                  style={{ color: CATEGORY_COLORS[selectedEntry.category] ?? 'var(--muted)' }}
                >
                  {getCategoryLabel(selectedEntry.category)}
                </span>
                <span className={`knowledge-usage ${selectedEntry.projectUsageStatus ?? 'none'}`}>
                  {usageLabel(selectedEntry.projectUsageStatus)}
                </span>
              </div>
              {editingId === selectedEntry.id ? (
                <div className="knowledge-edit-form">
                  <label>
                    <span>标题</span>
                    <input value={editTitle} onChange={(e) => setEditTitle(e.target.value)} />
                  </label>
                  <label>
                    <span>内容</span>
                    <textarea value={editContent} onChange={(e) => setEditContent(e.target.value)} rows={10} />
                  </label>
                  <div className="knowledge-detail-actions">
                    <button
                      className="ink-button"
                      onClick={() => updateMutation.mutate()}
                      disabled={updateMutation.isPending || !editTitle.trim() || !editContent.trim()}
                    >
                      {updateMutation.isPending ? '保存中...' : '保存条目'}
                    </button>
                    <button className="ghost-button" onClick={() => setEditingId(null)}>取消</button>
                    {updateMutation.error && (
                      <span className="knowledge-edit-error">{updateMutation.error.message}</span>
                    )}
                  </div>
                </div>
              ) : (
                <>
                  <h3>{selectedEntry.title}</h3>
                  <p>{selectedEntry.content}</p>
                  <dl className="knowledge-detail-meta">
                    <div>
                      <dt>项目状态</dt>
                      <dd>{usageLabel(selectedEntry.projectUsageStatus)}</dd>
                    </div>
                    <div>
                      <dt>本项目引用次数</dt>
                      <dd>{selectedEntry.projectUsageCount ?? 0}</dd>
                    </div>
                    <div>
                      <dt>最近引用</dt>
                      <dd>{formatUsageTime(selectedEntry.projectLastUsedAt)}</dd>
                    </div>
                  </dl>

                  <div className="knowledge-detail-actions">
                    <button className="ink-button" onClick={() => startEditing(selectedEntry)}>
                      编辑条目
                    </button>
                    <button
                      className="danger-button knowledge-delete"
                      onClick={() => deleteMutation.mutate(selectedEntry.id)}
                      disabled={deleteMutation.isPending}
                    >
                      {deleteMutation.isPending ? '删除中...' : '删除条目'}
                    </button>
                  </div>
                </>
              )}
            </>
          ) : (
            <div className="empty knowledge-empty">选择条目查看详情</div>
          )}
        </aside>
      </div>
    </section>
  );
}
