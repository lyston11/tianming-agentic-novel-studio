import { useEffect, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { getProjectWorkflow, listProjects, toNovelProjectInfo } from '@/api';
import { ensureProjectSelected, setCurrentProject, setCurrentProjectId, useProjectSelection } from '@/lib/project-store';
import { PageHeader } from '@/components/shared/page-header';
import { EmptyState } from '@/components/shared/empty-state';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { GoalWorkbench } from './goal-workbench';

function chainStatusClass(status: string) {
  const normalized = status.toLowerCase();
  if (normalized === 'completed' || normalized === 'succeeded') return 'text-emerald-600';
  if (normalized === 'failed' || normalized === 'blocked' || normalized === 'retryable_failed') return 'text-destructive';
  if (normalized === 'running' || normalized === 'processing') return 'text-primary';
  return 'text-muted-foreground';
}

function chapterStatusTone(chapter: { needsRewrite: boolean; visibleInLibrary: boolean; status: string }) {
  if (chapter.needsRewrite) return 'border-destructive/40 bg-destructive/5';
  if (chapter.visibleInLibrary) return 'border-emerald-500/30 bg-emerald-500/5';
  return 'border-border';
}

export default function WorkflowPage() {
  const navigate = useNavigate();
  const { projectId: routeProjectId } = useParams();
  const { currentProjectId } = useProjectSelection();
  const projectId = routeProjectId || currentProjectId || '';

  const { data: projects = [] } = useQuery({
    queryKey: ['projects'],
    queryFn: listProjects,
    staleTime: 5 * 60 * 1000,
    enabled: !projectId,
  });

  useEffect(() => {
    if (projects.length > 0) {
      ensureProjectSelected(projects.map(toNovelProjectInfo));
    }
  }, [projects]);

  const workflowQuery = useQuery({
    queryKey: ['projectWorkflow', projectId],
    queryFn: () => getProjectWorkflow(projectId),
    enabled: Boolean(projectId),
    refetchInterval: 30_000,
  });

  const document = workflowQuery.data;
  const volumes = document?.library.volumes ?? [];
  const productionChains = useMemo(
    () => [...(document?.productionChains ?? [])]
      .sort((left, right) => (right.updatedAt || '').localeCompare(left.updatedAt || ''))
      .slice(0, 8),
    [document?.productionChains],
  );

  const selectProject = (nextProjectId: string) => {
    const project = projects.find((item) => item.id === nextProjectId);
    if (project) {
      setCurrentProject(toNovelProjectInfo(project));
    } else {
      setCurrentProjectId(nextProjectId);
    }
    navigate(`/workflow/${encodeURIComponent(nextProjectId)}`);
  };

  if (!projectId) {
    return (
      <div>
        <PageHeader title="创作工作流" description="选择一个项目进入生产控制台" />
        {projects.length === 0 ? (
          <EmptyState title="还没有项目" description="先在 Agent 对话页创建项目。" />
        ) : (
          <div className="grid max-w-xl gap-2">
            {projects.map((project) => (
              <button
                key={project.id}
                type="button"
                className="rounded-xl border bg-card p-4 text-left transition-shadow hover:shadow-sm"
                onClick={() => selectProject(project.id)}
              >
                <strong className="font-serif">{project.title}</strong>
                <span className="ml-2 text-xs text-muted-foreground">{project.genre}</span>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title="创作工作流"
        description={document?.project?.title}
        actions={
          <Select value={projectId} onValueChange={selectProject}>
            <SelectTrigger className="h-8 w-56 text-xs">
              <SelectValue placeholder="切换项目" />
            </SelectTrigger>
            <SelectContent>
              {(projects.length > 0
                ? projects.map((project) => ({ id: project.id, title: project.title }))
                : [{ id: projectId, title: document?.project?.title || projectId }]
              ).map((project) => (
                <SelectItem key={project.id} value={project.id}>
                  {project.title}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        }
      />

      {workflowQuery.isLoading && (
        <div className="space-y-3">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      )}
      {workflowQuery.isError && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
          工作流读取失败：{workflowQuery.error.message}
        </div>
      )}

      {document && (
        <>
          {document.projectionKind === 'goal' && document.latestGoalId ? (
            <GoalWorkbench key={document.latestGoalId} goalId={document.latestGoalId} />
          ) : (
            <EmptyState
              title="还没有进行中的 Goal"
              description="到 Agent 对话页描述你的创作目标，Agent 会生成 Goal 提案供确认。"
            />
          )}

          <section className="rounded-xl border bg-card p-4" aria-label="成稿卷章">
            <header className="mb-3 flex items-baseline justify-between">
              <strong className="font-serif">成稿卷章</strong>
              <span className="text-xs text-muted-foreground">
                {document.library.generatedChapterCount} / {document.library.plannedChapterCount} 章入库
                {document.library.needsRewriteCount > 0 && ` · ${document.library.needsRewriteCount} 章需修订`}
              </span>
            </header>
            {volumes.length === 0 ? (
              <p className="text-sm text-muted-foreground">还没有卷章数据。</p>
            ) : (
              <div className="space-y-4">
                {volumes.map((volume) => (
                  <div key={volume.volumeId}>
                    <div className="mb-1.5 flex items-baseline justify-between text-xs text-muted-foreground">
                      <strong className="text-foreground">{volume.title}</strong>
                      <span>
                        {volume.chapters.filter((chapter) => chapter.visibleInLibrary).length} 章
                      </span>
                    </div>
                    <div className="grid grid-cols-2 gap-1.5 sm:grid-cols-4 lg:grid-cols-6">
                      {volume.chapters.map((chapter) => (
                        <button
                          key={chapter.chapterId}
                          type="button"
                          className={`rounded-lg border px-2 py-1.5 text-left text-xs transition-colors hover:bg-muted ${chapterStatusTone(chapter)}`}
                          title={chapter.summary || chapter.title}
                          onClick={() => navigate('/library')}
                        >
                          <span className="block truncate font-medium">{chapter.title}</span>
                          <span className="text-[10px] text-muted-foreground">
                            {chapter.needsRewrite
                              ? '需修订'
                              : chapter.visibleInLibrary
                                ? `已入库 · ${chapter.wordCount} 字`
                                : chapter.userVisibleStatus || chapter.status}
                          </span>
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          <div className="grid gap-4 lg:grid-cols-2">
            <section className="rounded-xl border bg-card p-4" aria-label="最近生产链路">
              <header className="mb-2">
                <strong className="font-serif">最近生产链路</strong>
              </header>
              {productionChains.length === 0 ? (
                <p className="text-sm text-muted-foreground">还没有章节生产记录。</p>
              ) : (
                <div className="space-y-2">
                  {productionChains.map((chain) => (
                    <div key={chain.id} className="rounded-lg bg-muted/40 px-3 py-2 text-xs">
                      <div className="flex items-baseline justify-between gap-2">
                        <strong className="truncate">{chain.chapterDisplayName}</strong>
                        <em className={`shrink-0 not-italic ${chainStatusClass(chain.status)}`}>
                          {chain.status}
                        </em>
                      </div>
                      <div className="mt-0.5 flex flex-wrap gap-2 text-[10px] text-muted-foreground">
                        <span>v{chain.chapterVersionNumber || '-'}</span>
                        <span>事实 v{chain.factSnapshotVersion || '-'}</span>
                        <span>{chain.revisionPlanIds.length} 修订</span>
                        <span className="truncate">{chain.summary || chain.packageId}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>

            <section className="rounded-xl border bg-card p-4" aria-label="调度任务">
              <header className="mb-2">
                <strong className="font-serif">调度任务</strong>
              </header>
              {(document.schedulerTasks ?? []).length === 0 ? (
                <p className="text-sm text-muted-foreground">暂无计划任务。</p>
              ) : (
                <div className="space-y-1.5">
                  {(document.schedulerTasks ?? []).slice(0, 10).map((task) => (
                    <div key={task.taskId} className="flex items-center gap-2 text-xs">
                      <span
                        className={`inline-block size-1.5 rounded-full ${
                          task.status === 'running'
                            ? 'animate-pulse bg-primary'
                            : task.status === 'completed'
                              ? 'bg-emerald-500'
                              : task.status === 'failed'
                                ? 'bg-destructive'
                                : 'bg-muted-foreground/40'
                        }`}
                      />
                      <strong className="min-w-0 flex-1 truncate">{task.nextAction || task.taskType}</strong>
                      <span className="shrink-0 text-[10px] text-muted-foreground">{task.status}</span>
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>
        </>
      )}
    </div>
  );
}
