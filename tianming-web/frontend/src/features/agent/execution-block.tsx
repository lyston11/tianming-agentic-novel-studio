import { Fragment } from 'react';
import {
  executionBlockDestination,
  executionBlockOutputLabel,
  executionVisibleEvents,
  formatExecutionDuration,
  productionExecutionSummary,
  productionStageStatusLabel,
  productionStageTrackGroups,
} from '@/lib/runtime-events';
import type { ExecutionBlockView } from '@/lib/runtime-events';

interface ExecutionBlockProps {
  block: ExecutionBlockView;
  clockNow: number;
  onToggle: (id: string) => void;
}

function formatEventTime(value: Date) {
  return value.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
}

const STATUS_DOT_CLASS: Record<ExecutionBlockView['status'], string> = {
  running: 'bg-primary animate-pulse',
  done: 'bg-emerald-500',
  failed: 'bg-destructive',
};

/** One collapsible "后台执行" block under a chat turn. */
export function ExecutionBlockCard({ block, clockNow, onToggle }: ExecutionBlockProps) {
  const productionTracks = productionStageTrackGroups(block.events);
  const visibleEvents = block.expanded ? executionVisibleEvents(block) : [];
  const visiblePreviews = block.expanded ? block.previews : block.previews.slice(0, 1);
  const detailCount = block.events.length + block.previews.length;
  const productionSummary = productionExecutionSummary(block.events);
  const duration = formatExecutionDuration(
    block.startedAt,
    block.status === 'running' ? clockNow : block.updatedAt,
  );
  const outputLabel = executionBlockOutputLabel(block);
  const destination = executionBlockDestination(block);

  return (
    <section className="rounded-xl border bg-card/70" key={block.id}>
      <button
        type="button"
        className="flex w-full items-center gap-2 px-3.5 py-2.5 text-left text-xs"
        onClick={() => onToggle(block.id)}
        aria-expanded={block.expanded}
      >
        <span
          className={`inline-block size-2 shrink-0 rounded-full ${STATUS_DOT_CLASS[block.status]}`}
          aria-hidden="true"
        />
        <strong className="shrink-0">{block.title}</strong>
        <span className="text-muted-foreground">{outputLabel}</span>
        <span className="ml-auto shrink-0 font-mono text-[10px] text-muted-foreground">{duration}</span>
        <span className="shrink-0 text-muted-foreground">{block.expanded ? '⌃' : '›'}</span>
      </button>

      {block.expanded && detailCount > 0 && (
        <div className="space-y-3 border-t px-3.5 py-3">
          <div className="flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
            <span>{productionSummary || block.summary || '后台执行状态已更新。'}</span>
            <span className="rounded bg-muted px-1.5 py-0.5">{destination}</span>
            {block.runId && <span className="font-mono">{block.runId.slice(0, 8)}</span>}
          </div>

          {productionTracks.length > 0 && (
            <div className="space-y-2" aria-label="章节生产闭环阶段">
              {productionTracks.map((track) => (
                <section key={track.key} className="rounded-lg bg-muted/40 p-2.5">
                  <header className="flex items-baseline justify-between text-[11px]">
                    <strong>{track.label}</strong>
                    <span className="text-muted-foreground">{track.summary}</span>
                  </header>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {track.items.map((stage) => (
                      <div
                        key={stage.key}
                        title={stage.detail}
                        className={`rounded-md border px-1.5 py-1 text-[10px] ${
                          stage.status === 'done'
                            ? 'border-emerald-500/30 bg-emerald-500/5'
                            : stage.status === 'running'
                              ? 'border-primary/40 bg-primary/5'
                              : stage.status === 'failed'
                                ? 'border-destructive/40 bg-destructive/5 text-destructive'
                                : 'border-border text-muted-foreground'
                        }`}
                      >
                        <span>{stage.label}</span>
                        <small className="ml-1 text-muted-foreground">
                          {productionStageStatusLabel(stage.status)}
                          {(stage.repeatCount ?? 1) > 1 ? ` · ${stage.repeatCount} 次` : ''}
                        </small>
                      </div>
                    ))}
                  </div>
                </section>
              ))}
            </div>
          )}

          {visibleEvents.length > 0 && (
            <div className="space-y-1.5">
              {visibleEvents.map((event) => (
                <div key={event.id} className="flex items-start gap-2 text-[11px]">
                  <i
                    className={`mt-1 inline-block size-1.5 shrink-0 rounded-full ${
                      event.status === 'failed'
                        ? 'bg-destructive'
                        : event.status === 'done'
                          ? 'bg-emerald-500'
                          : 'bg-primary/60'
                    }`}
                    aria-hidden="true"
                  />
                  <div className="min-w-0">
                    <strong>{event.title}</strong>
                    <span className="text-muted-foreground">
                      {' '}
                      {event.detail || '已收到运行事件。'}
                      {(event.repeatCount ?? 1) > 1 ? `（同阶段更新 ${event.repeatCount} 次）` : ''}
                    </span>
                    <small className="ml-1 font-mono text-[10px] text-muted-foreground/70">
                      {formatEventTime(event.timestamp)}
                    </small>
                  </div>
                </div>
              ))}
            </div>
          )}

          {visiblePreviews.length > 0 && (
            <div className="space-y-2">
              {visiblePreviews.map((preview) => (
                <Fragment key={`${preview.title}-${preview.createdAt}`}>
                  <article className="rounded-lg border p-2.5">
                    <div className="flex items-baseline justify-between gap-2">
                      <strong className="text-xs">{preview.title}</strong>
                      <span className="text-[10px] text-muted-foreground">{preview.resultLocation}</span>
                    </div>
                    <p className="mt-1 text-[11px] text-muted-foreground">{preview.summary}</p>
                    {preview.items.length > 0 && (
                      <ul className="mt-1.5 list-disc space-y-0.5 pl-4 text-[11px] text-muted-foreground">
                        {preview.items.slice(0, block.expanded ? 4 : 2).map((previewItem, index) => (
                          <li key={`${preview.title}-${index}`}>{previewItem}</li>
                        ))}
                      </ul>
                    )}
                  </article>
                </Fragment>
              ))}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
