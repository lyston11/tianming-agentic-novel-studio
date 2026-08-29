import type { AgentKnowledgeContext } from '@/api/types';

interface KnowledgeContextCardProps {
  context: AgentKnowledgeContext;
}

/** Collapsible card showing what the agent read from the knowledge base this turn. */
export function KnowledgeContextCard({ context }: KnowledgeContextCardProps) {
  const visibleDirectories = context.directories.filter((directory) => directory.count > 0).slice(0, 6);
  const visibleItems = context.items.slice(0, 6);

  return (
    <details className="mt-2 rounded-xl border bg-card/70 text-sm">
      <summary className="flex cursor-pointer items-center justify-between gap-2 px-3.5 py-2.5">
        <span>
          <span className="block font-mono text-[10px] tracking-wide text-muted-foreground uppercase">
            Knowledge.Query
          </span>
          <strong className="text-sm">已读取当前知识库</strong>
        </span>
        <em className="text-xs text-muted-foreground not-italic">
          {context.totalCount} 条 · 本轮 {context.items.length} 条
        </em>
      </summary>
      <div className="space-y-3 border-t px-3.5 py-3">
        <div className="flex flex-wrap gap-3 text-[11px] text-muted-foreground">
          <span>版本 {context.knowledgeVersion}</span>
          <span>目录修订 {context.catalogRevision}</span>
          <span>{context.intent === 'retrieve' ? '相关内容检索' : '目录盘点'}</span>
        </div>
        {visibleDirectories.length > 0 ? (
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {visibleDirectories.map((directory) => (
              <div key={directory.key} className="rounded-lg bg-muted/50 p-2.5">
                <span className="block text-[11px] text-muted-foreground">{directory.name}</span>
                <strong className="text-sm">{directory.count}</strong>
                {directory.sampleTitles.length > 0 && (
                  <small className="mt-0.5 block truncate text-[10px] text-muted-foreground">
                    {directory.sampleTitles.join(' · ')}
                  </small>
                )}
              </div>
            ))}
          </div>
        ) : (
          <p className="text-xs text-muted-foreground">当前知识库还没有可用条目。</p>
        )}
        {visibleItems.length > 0 && (
          <div className="space-y-2">
            {visibleItems.map((item) => (
              <article key={item.id} className="rounded-lg border p-2.5">
                <header className="flex items-baseline gap-2">
                  <span className="font-mono text-[10px] text-muted-foreground">{item.entryType}</span>
                  <strong className="min-w-0 flex-1 truncate text-xs">{item.title}</strong>
                  {item.score != null && (
                    <em className="text-[10px] text-primary not-italic">{Math.round(item.score * 100)}%</em>
                  )}
                </header>
                <p className="mt-1 line-clamp-2 text-[11px] text-muted-foreground">{item.excerpt}</p>
              </article>
            ))}
          </div>
        )}
      </div>
    </details>
  );
}
