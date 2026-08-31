import { cn } from '@/lib/utils';

interface EmptyStateProps {
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}

export function EmptyState({ title, description, action, className }: EmptyStateProps) {
  return (
    <div
      className={cn(
        'grid min-h-[320px] flex-1 place-items-center rounded-xl border border-dashed bg-card/50 p-10',
        className,
      )}
    >
      <div className="max-w-sm text-center">
        <div className="font-serif text-lg font-semibold text-foreground">{title}</div>
        {description && (
          <p className="mt-2 text-sm text-muted-foreground">{description}</p>
        )}
        {action && <div className="mt-4">{action}</div>}
      </div>
    </div>
  );
}
