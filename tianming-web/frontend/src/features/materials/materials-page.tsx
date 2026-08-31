import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createMaterialFromText,
  deleteMaterialById,
  getKnowledgeTask,
  listMaterials,
  updateMaterialById,
  uploadKnowledgeFile,
} from '@/api';
import type { KnowledgeProcessingTaskResponse, MaterialResponse } from '@/api/types';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Progress } from '@/components/ui/progress';
import { Textarea } from '@/components/ui/textarea';
import { PageHeader } from '@/components/shared/page-header';
import { toast } from 'sonner';
import { useProjectSelection } from '@/lib/project-store';
import { KnowledgeBrowser } from './knowledge-browser';

const TERMINAL_KNOWLEDGE_TASK_STATUSES = new Set(['completed', 'failed']);

interface KnowledgeTaskMeta {
  taskId: string;
  projectId: string;
  fileName: string;
  status: string;
}

function knowledgeTaskPresentation(status: string) {
  switch (status) {
    case 'completed':
      return { label: '知识已就绪', description: '解析、知识抽取与向量索引均已完成。' };
    case 'failed':
      return { label: '处理失败', description: '后台处理未能完成，请根据错误信息修正文件后重试。' };
    case 'retryable_failed':
      return { label: '等待重试', description: '本次处理失败，后台将在退避后自动重试。' };
    case 'processing':
      return { label: '正在构建知识', description: '正在解析文档、抽取条目并建立语义索引。' };
    case 'claimed':
      return { label: '任务已领取', description: '后台工作器已领取任务，即将开始解析。' };
    default:
      return { label: '等待处理', description: '文件已安全入队，后台会自动开始处理。' };
  }
}

export default function MaterialsPage() {
  const queryClient = useQueryClient();
  const { currentProjectId } = useProjectSelection();
  const fileRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const [pasteContent, setPasteContent] = useState('');
  const [pasteTitle, setPasteTitle] = useState('');
  const [ingestOpen, setIngestOpen] = useState(false);
  const [editingMaterialId, setEditingMaterialId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editCategory, setEditCategory] = useState('');
  const [editTags, setEditTags] = useState('');
  const [knowledgeTasksByProject, setKnowledgeTasksByProject] = useState<Record<string, KnowledgeTaskMeta>>({});
  const handledKnowledgeTaskIdsRef = useRef(new Set<string>());
  const lastKnowledgeTask = currentProjectId ? knowledgeTasksByProject[currentProjectId] ?? null : null;

  const { data: materialsData } = useQuery({
    queryKey: ['materials', currentProjectId],
    queryFn: () =>
      currentProjectId
        ? listMaterials(currentProjectId)
        : Promise.resolve({ materials: [] as MaterialResponse[], totalCount: 0 }),
    enabled: !!currentProjectId,
  });

  const knowledgeTaskQuery = useQuery<KnowledgeProcessingTaskResponse>({
    queryKey: ['knowledgeTask', lastKnowledgeTask?.projectId, lastKnowledgeTask?.taskId],
    queryFn: () => getKnowledgeTask(lastKnowledgeTask!.taskId),
    enabled: !!lastKnowledgeTask?.taskId,
    refetchInterval: (query) => {
      if (query.state.status === 'error') return false;
      const status = query.state.data?.status;
      return status && TERMINAL_KNOWLEDGE_TASK_STATUSES.has(status) ? false : 1200;
    },
    retry: 2,
  });

  const uploadKnowledgeMutation = useMutation({
    mutationFn: ({ projectId, file }: { projectId: string; file: File }) =>
      uploadKnowledgeFile(projectId, file, file.name),
    onSuccess: (result, { projectId, file }) => {
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', projectId] });
      handledKnowledgeTaskIdsRef.current.delete(result.taskId);
      setKnowledgeTasksByProject((current) => ({
        ...current,
        [projectId]: {
          taskId: result.taskId,
          projectId,
          fileName: result.fileName || file.name,
          status: result.status,
        },
      }));
      toast.success(`知识文件已上传，taskId=${result.taskId}`);
    },
    onError: (error) => toast.error(`上传失败: ${error.message}`),
  });

  useEffect(() => {
    const task = knowledgeTaskQuery.data;
    if (!task || !lastKnowledgeTask) return;

    if (!TERMINAL_KNOWLEDGE_TASK_STATUSES.has(task.status) || handledKnowledgeTaskIdsRef.current.has(task.id)) {
      return;
    }

    handledKnowledgeTaskIdsRef.current.add(task.id);
    if (task.status === 'completed') {
      void queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', lastKnowledgeTask.projectId] });
      void queryClient.invalidateQueries({ queryKey: ['knowledgeDirectories'] });
      toast.success(`知识文件处理完成，已提取 ${task.extractedEntriesCount} 条知识`);
    } else {
      toast.error(`知识文件处理失败：${task.errorMessage || '未知错误'}`);
    }
  }, [knowledgeTaskQuery.data, lastKnowledgeTask, queryClient]);

  const createTextMutation = useMutation({
    mutationFn: () => {
      if (!currentProjectId) throw new Error('No project selected');
      const content = pasteContent.trim();
      if (!content) throw new Error('内容为空');
      return createMaterialFromText({
        projectId: currentProjectId,
        title: pasteTitle || `粘贴素材-${new Date().toISOString().slice(0, 10)}.txt`,
        content,
        category: 'Research',
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      toast.success('素材创建成功');
      setPasteContent('');
      setPasteTitle('');
    },
    onError: (error) => toast.error(`创建失败: ${error.message}`),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteMaterialById,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      toast.success('素材已删除');
    },
    onError: (error) => toast.error(`删除失败: ${error.message}`),
  });

  const updateMutation = useMutation({
    mutationFn: () => {
      if (!editingMaterialId) throw new Error('未选择素材');
      return updateMaterialById(editingMaterialId, {
        title: editTitle,
        category: editCategory,
        tags: editTags,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      toast.success('素材已更新');
      setEditingMaterialId(null);
    },
    onError: (error) => toast.error(`更新失败: ${error.message}`),
  });

  const currentKnowledgeTask = knowledgeTaskQuery.data;
  const currentKnowledgeStatus = currentKnowledgeTask?.status ?? lastKnowledgeTask?.status ?? 'pending';
  const hasActiveKnowledgeTask = !!lastKnowledgeTask && !TERMINAL_KNOWLEDGE_TASK_STATUSES.has(currentKnowledgeStatus);

  const handleFile = (file: File) => {
    if (!currentProjectId || uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask) return;
    uploadKnowledgeMutation.mutate({ projectId: currentProjectId, file });
  };

  const handleFileInput = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) handleFile(file);
    event.target.value = '';
  };

  const handleDrop = (event: React.DragEvent) => {
    event.preventDefault();
    setDragOver(false);
    const file = event.dataTransfer.files[0];
    if (file) handleFile(file);
  };

  const materials = materialsData?.materials ?? [];
  const currentKnowledgePresentation = knowledgeTaskPresentation(currentKnowledgeStatus);
  const currentKnowledgeProgress = Math.max(0, Math.min(100, currentKnowledgeTask?.progress ?? 0));

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <PageHeader
        title="创意知识库"
        description={currentProjectId ? '素材、知识与语义检索' : '先在书城选择一个项目'}
        actions={
          <Button onClick={() => setIngestOpen(true)} disabled={!currentProjectId}>
            导入素材
          </Button>
        }
      />

      <KnowledgeBrowser projectId={currentProjectId || undefined} />

      <Dialog open={ingestOpen} onOpenChange={setIngestOpen}>
        <DialogContent className="max-h-[85dvh] max-w-2xl overflow-y-auto">
          <DialogHeader>
            <DialogTitle>导入素材</DialogTitle>
          </DialogHeader>

          <div className="space-y-5">
            <section>
              <div className="mb-2 flex items-baseline justify-between">
                <span className="text-sm font-medium">摄取知识</span>
                <small className="text-xs text-muted-foreground">上传文件进入知识处理队列</small>
              </div>
              <div
                role="button"
                tabIndex={0}
                className={`grid cursor-pointer place-items-center rounded-xl border-2 border-dashed p-8 text-center transition-colors ${
                  dragOver ? 'border-primary bg-primary/5' : 'border-border'
                } ${uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask ? 'pointer-events-none opacity-50' : ''}`}
                onDragOver={(event) => {
                  event.preventDefault();
                  setDragOver(true);
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                onClick={() => {
                  if (!uploadKnowledgeMutation.isPending && !hasActiveKnowledgeTask) fileRef.current?.click();
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') fileRef.current?.click();
                }}
              >
                <input
                  ref={fileRef}
                  type="file"
                  accept=".txt,.epub,.pdf"
                  onChange={handleFileInput}
                  disabled={uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask}
                  className="hidden"
                />
                <div className="text-2xl text-muted-foreground">+</div>
                <div className="mt-1 text-sm">
                  {uploadKnowledgeMutation.isPending
                    ? '上传中...'
                    : hasActiveKnowledgeTask
                      ? '当前文件处理中'
                      : '选择知识文件'}
                </div>
                <div className="text-xs text-muted-foreground">.txt / .epub / .pdf</div>
              </div>

              {lastKnowledgeTask && (
                <div className="mt-3 rounded-xl border p-3" aria-live="polite">
                  <div className="flex items-baseline justify-between text-sm">
                    <span>{currentKnowledgePresentation.label}</span>
                    <strong className="truncate text-xs">{lastKnowledgeTask.fileName}</strong>
                  </div>
                  <Progress className="mt-2 h-1.5" value={currentKnowledgeProgress} aria-label={`处理进度 ${currentKnowledgeProgress}%`} />
                  <div className="mt-1.5 flex justify-between text-[11px] text-muted-foreground">
                    <span>{currentKnowledgeProgress}%</span>
                    <span>{currentKnowledgeTask?.extractedEntriesCount ?? 0} 条知识</span>
                  </div>
                  <p className="mt-1 text-[11px] text-muted-foreground">{currentKnowledgePresentation.description}</p>
                  {currentKnowledgeTask?.errorMessage && (
                    <p className="mt-1 text-[11px] text-destructive">{currentKnowledgeTask.errorMessage}</p>
                  )}
                  {knowledgeTaskQuery.isError && (
                    <p className="mt-1 text-[11px] text-destructive">
                      状态查询失败：{knowledgeTaskQuery.error.message}
                      <button
                        className="ml-2 underline"
                        onClick={() => void knowledgeTaskQuery.refetch()}
                      >
                        重新查询
                      </button>
                    </p>
                  )}
                  <code className="mt-1 block truncate text-[10px] text-muted-foreground" title="任务编号">
                    {lastKnowledgeTask.taskId}
                  </code>
                </div>
              )}

              {uploadKnowledgeMutation.isError && (
                <p className="mt-2 text-sm text-destructive" role="alert">
                  上传失败：{uploadKnowledgeMutation.error.message}
                </p>
              )}
            </section>

            <section>
              <div className="mb-2 flex items-center gap-2">
                <span className="text-sm font-medium">粘贴文本</span>
                <Input
                  className="h-7 flex-1 text-xs"
                  placeholder="素材名称"
                  value={pasteTitle}
                  onChange={(event) => setPasteTitle(event.target.value)}
                />
              </div>
              <Textarea
                rows={5}
                value={pasteContent}
                onChange={(event) => setPasteContent(event.target.value)}
                placeholder="小说片段、设定文档、评论、参考梗概..."
              />
              <Button
                className="mt-2"
                size="sm"
                onClick={() => {
                  if (!pasteContent.trim()) return;
                  createTextMutation.mutate();
                }}
                disabled={createTextMutation.isPending || !pasteContent.trim()}
              >
                {createTextMutation.isPending ? '创建中...' : '创建素材'}
              </Button>
            </section>

            <section>
              <div className="mb-2 flex items-baseline justify-between">
                <span className="text-sm font-medium">素材库</span>
                <small className="text-xs text-muted-foreground">{materials.length} 份</small>
              </div>
              {materials.length === 0 ? (
                <p className="rounded-lg border border-dashed p-4 text-center text-sm text-muted-foreground">
                  暂无素材
                </p>
              ) : (
                <div className="space-y-2">
                  {materials.map((material) => (
                    <div key={material.id} className="rounded-lg border p-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0 text-sm font-medium">{material.title}</div>
                        <div className="flex shrink-0 gap-1">
                          <Button
                            variant="ghost"
                            size="xs"
                            onClick={() => {
                              setEditingMaterialId(material.id);
                              setEditTitle(material.title);
                              setEditCategory(material.category || '');
                              setEditTags(material.tags || '');
                            }}
                          >
                            编辑
                          </Button>
                          <Button
                            variant="ghost"
                            size="xs"
                            className="text-destructive"
                            onClick={() => deleteMutation.mutate(material.id)}
                          >
                            删除
                          </Button>
                        </div>
                      </div>
                      {editingMaterialId === material.id ? (
                        <div className="mt-2 space-y-2">
                          <Input value={editTitle} onChange={(event) => setEditTitle(event.target.value)} placeholder="素材名称" />
                          <Input value={editCategory} onChange={(event) => setEditCategory(event.target.value)} placeholder="分类" />
                          <Input value={editTags} onChange={(event) => setEditTags(event.target.value)} placeholder="标签，用逗号分隔" />
                          <div className="flex gap-2">
                            <Button
                              size="xs"
                              onClick={() => updateMutation.mutate()}
                              disabled={updateMutation.isPending}
                            >
                              {updateMutation.isPending ? '保存中...' : '保存修改'}
                            </Button>
                            <Button variant="ghost" size="xs" onClick={() => setEditingMaterialId(null)}>
                              取消
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <>
                          <div className="mt-1 text-[11px] text-muted-foreground">
                            {material.category || 'Uncategorized'} · {material.vectorChunkCount} 个向量块
                          </div>
                          {material.tags && (
                            <div className="mt-1.5 flex flex-wrap gap-1">
                              {material.tags.split(',').slice(0, 5).map((tag, index) => (
                                <span key={index} className="rounded bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">
                                  {tag.trim()}
                                </span>
                              ))}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
