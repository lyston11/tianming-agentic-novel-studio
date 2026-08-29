import { cn } from '@/lib/utils';

/** 天命品牌印章：红底「命」字方印 + 品牌名。 */
export function BrandMark({ className }: { className?: string }) {
  return (
    <div className={cn('flex items-center gap-2.5', className)}>
      <div className="grid size-9 shrink-0 place-items-center rounded-lg bg-primary font-serif text-xl font-bold text-primary-foreground shadow-sm">
        命
      </div>
      <div className="leading-tight">
        <div className="font-serif text-base font-bold">天命</div>
        <div className="text-[11px] text-muted-foreground">Novel Agent</div>
      </div>
    </div>
  );
}
