import type { AgentKnowledgeContext } from '../../api/types';

interface KnowledgeContextCardProps {
  context: AgentKnowledgeContext;
}

export default function KnowledgeContextCard({ context }: KnowledgeContextCardProps) {
  const visibleDirectories = context.directories.filter((directory) => directory.count > 0).slice(0, 6);
  const visibleItems = context.items.slice(0, 6);

  return (
    <details className="agent-knowledge-context">
      <summary>
        <div>
          <span>Knowledge.Query</span>
          <strong>已读取当前知识库</strong>
        </div>
        <em>{context.totalCount} 条 · 本轮 {context.items.length} 条</em>
      </summary>
      <div className="agent-knowledge-context-body">
        <div className="agent-knowledge-context-meta">
          <span>版本 {context.knowledgeVersion}</span>
          <span>目录修订 {context.catalogRevision}</span>
          <span>{context.intent === 'retrieve' ? '相关内容检索' : '目录盘点'}</span>
        </div>
        {visibleDirectories.length > 0 ? (
          <div className="agent-knowledge-directory-grid">
            {visibleDirectories.map((directory) => (
              <div key={directory.key}>
                <span>{directory.name}</span>
                <strong>{directory.count}</strong>
                {directory.sampleTitles.length > 0 && <small>{directory.sampleTitles.join(' · ')}</small>}
              </div>
            ))}
          </div>
        ) : (
          <p className="agent-knowledge-empty">当前知识库还没有可用条目。</p>
        )}
        {visibleItems.length > 0 && (
          <div className="agent-knowledge-hit-list">
            {visibleItems.map((item) => (
              <article key={item.id}>
                <header>
                  <span>{item.entryType}</span>
                  <strong>{item.title}</strong>
                  {item.score != null && <em>{Math.round(item.score * 100)}%</em>}
                </header>
                <p>{item.excerpt}</p>
              </article>
            ))}
          </div>
        )}
      </div>
    </details>
  );
}
