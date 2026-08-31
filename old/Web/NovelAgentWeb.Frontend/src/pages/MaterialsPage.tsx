import { useEffect, useRef, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listMaterials,
  getKnowledgeTask,
  uploadKnowledgeFile,
  deleteMaterialById,
  updateMaterialById,
  createMaterialFromText,
} from '../api';
import type { KnowledgeProcessingTaskResponse, MaterialResponse } from '../api/types';
import { useAppStore } from '../stores/useAppStore';
import { useProjectStore } from '../stores/useProjectStore';
import Topbar from '../components/layout/Topbar';
import KnowledgeBaseBrowser from '../components/materials/KnowledgeBaseBrowser';
import '../styles/materials.css';

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
      return { label: '知识已就绪', tone: 'completed', description: '解析、知识抽取与向量索引均已完成。' };
    case 'failed':
      return { label: '处理失败', tone: 'failed', description: '后台处理未能完成，请根据错误信息修正文件后重试。' };
    case 'retryable_failed':
      return { label: '等待重试', tone: 'retrying', description: '本次处理失败，后台将在退避后自动重试。' };
    case 'processing':
      return { label: '正在构建知识', tone: 'processing', description: '正在解析文档、抽取条目并建立语义索引。' };
    case 'claimed':
      return { label: '任务已领取', tone: 'processing', description: '后台工作器已领取任务，即将开始解析。' };
    default:
      return { label: '等待处理', tone: 'pending', description: '文件已安全入队，后台会自动开始处理。' };
  }
}

export default function MaterialsPage() {
  const queryClient = useQueryClient();
  const addLog = useAppStore((s) => s.addLog);
  const currentProjectId = useProjectStore((s) => s.currentProjectId);
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
    queryFn: () => currentProjectId ? listMaterials(currentProjectId) : Promise.resolve({ materials: [], totalCount: 0 }),
    enabled: !!currentProjectId
  });

  const knowledgeTaskQuery = useQuery<KnowledgeProcessingTaskResponse, Error>({
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
    mutationFn: ({ projectId, file }: { projectId: string; file: File }) => {
      return uploadKnowledgeFile(projectId, file, file.name);
    },
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
      addLog(`知识文件已上传，taskId=${result.taskId}`);
    },
    onError: (err) => addLog(`上传失败: ${err}`),
  });

  useEffect(() => {
    const task = knowledgeTaskQuery.data;
    if (!task || !lastKnowledgeTask) {
      return;
    }

    if (!TERMINAL_KNOWLEDGE_TASK_STATUSES.has(task.status) || handledKnowledgeTaskIdsRef.current.has(task.id)) {
      return;
    }

    handledKnowledgeTaskIdsRef.current.add(task.id);
    if (task.status === 'completed') {
      void queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', lastKnowledgeTask.projectId] });
      void queryClient.invalidateQueries({ queryKey: ['knowledgeDirectories'] });
      addLog(`知识文件处理完成，已提取 ${task.extractedEntriesCount} 条知识`);
    } else {
      addLog(`知识文件处理失败：${task.errorMessage || '未知错误'}`);
    }
  }, [addLog, knowledgeTaskQuery.data, lastKnowledgeTask, queryClient]);

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
      addLog('素材创建成功');
      setPasteContent('');
      setPasteTitle('');
    },
    onError: (err) => addLog(`创建失败: ${err}`),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteMaterialById,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      addLog('素材已删除');
    },
    onError: (err) => addLog(`删除失败: ${err}`),
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
      addLog('素材已更新');
      setEditingMaterialId(null);
    },
    onError: (err) => addLog(`更新失败: ${err}`),
  });

  const currentKnowledgeTask = knowledgeTaskQuery.data;
  const currentKnowledgeStatus = currentKnowledgeTask?.status ?? lastKnowledgeTask?.status ?? 'pending';
  const hasActiveKnowledgeTask = !!lastKnowledgeTask && !TERMINAL_KNOWLEDGE_TASK_STATUSES.has(currentKnowledgeStatus);

  const handleFile = (file: File) => {
    if (!currentProjectId || uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask) return;
    uploadKnowledgeMutation.mutate({ projectId: currentProjectId, file });
  };

  const handleFileInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) handleFile(file);
    e.target.value = '';
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) handleFile(file);
  };

  const handlePasteSubmit = () => {
    const content = pasteContent.trim();
    if (!content) return;
    createTextMutation.mutate();
  };

  const openMaterialEditor = (material: MaterialResponse) => {
    setEditingMaterialId(material.id);
    setEditTitle(material.title);
    setEditCategory(material.category || '');
    setEditTags(material.tags || '');
  };

  const materials = materialsData?.materials ?? [];
  const currentKnowledgePresentation = knowledgeTaskPresentation(currentKnowledgeStatus);
  const currentKnowledgeProgress = Math.max(0, Math.min(100, currentKnowledgeTask?.progress ?? 0));
  return (
    <>
      <Topbar title="创意知识库" />

      <div className="materials-page">
        <main className="materials-knowledge">
          <KnowledgeBaseBrowser
            projectId={currentProjectId || undefined}
            actions={(
              <button className="ink-button" onClick={() => setIngestOpen(true)}>
                导入素材
              </button>
            )}
          />
        </main>

        {ingestOpen && (
          <div className="ingest-overlay" role="presentation" onClick={() => setIngestOpen(false)}>
            <aside
              className="materials-ingest"
              role="dialog"
              aria-modal="true"
              aria-label="导入素材"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="ingest-drawer-head">
                <div>
                  <div className="section-kicker">Materials Intelligence</div>
                  <h2>导入素材</h2>
                </div>
                <button className="ghost-button" onClick={() => setIngestOpen(false)}>
                  关闭
                </button>
              </div>

            <div className="ingest-panel">
              <div className="ingest-panel-head">
                <span>摄取知识</span>
                <small>上传文件进入知识处理队列</small>
              </div>

              <div
                className={`upload-zone ${dragOver ? 'drag-over' : ''} ${uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask ? 'disabled' : ''}`}
                aria-disabled={uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask}
                onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                onClick={() => {
                  if (!uploadKnowledgeMutation.isPending && !hasActiveKnowledgeTask) fileRef.current?.click();
                }}
              >
                <input
                  ref={fileRef}
                  type="file"
                  accept=".txt,.epub,.pdf"
                  onChange={handleFileInput}
                  disabled={uploadKnowledgeMutation.isPending || hasActiveKnowledgeTask}
                  style={{ display: 'none' }}
                />
                <div className="upload-icon">+</div>
                <div className="upload-text">
                  {uploadKnowledgeMutation.isPending
                    ? '上传中...'
                    : hasActiveKnowledgeTask
                      ? '当前文件处理中'
                      : '选择知识文件'}
                </div>
                <div className="upload-hint">.txt / .epub / .pdf</div>
              </div>

              {lastKnowledgeTask && (
                <div className={`knowledge-task-note ${currentKnowledgePresentation.tone}`} aria-live="polite">
                  <div className="knowledge-task-head">
                    <span>{currentKnowledgePresentation.label}</span>
                    <strong>{lastKnowledgeTask.fileName}</strong>
                  </div>
                  <div className="knowledge-task-progress" aria-label={`处理进度 ${currentKnowledgeProgress}%`}>
                    <i style={{ width: `${currentKnowledgeProgress}%` }} />
                  </div>
                  <div className="knowledge-task-stats">
                    <span>{currentKnowledgeProgress}%</span>
                    <span>{currentKnowledgeTask?.extractedEntriesCount ?? 0} 条知识</span>
                  </div>
                  <p>{currentKnowledgePresentation.description}</p>
                  {currentKnowledgeTask?.errorMessage && (
                    <p className="knowledge-task-error">{currentKnowledgeTask.errorMessage}</p>
                  )}
                  {knowledgeTaskQuery.isError && (
                    <p className="knowledge-task-error">
                      状态查询失败：{knowledgeTaskQuery.error.message}
                      <button className="ghost-button" onClick={() => void knowledgeTaskQuery.refetch()}>
                        重新查询
                      </button>
                    </p>
                  )}
                  <code title="任务编号">{lastKnowledgeTask.taskId}</code>
                </div>
              )}

              {uploadKnowledgeMutation.isError && (
                <p className="knowledge-upload-error" role="alert">
                  上传失败：{uploadKnowledgeMutation.error.message}
                </p>
              )}

              <div className="paste-area">
                <div className="paste-header">
                  <span>粘贴文本</span>
                  <input
                    placeholder="素材名称"
                    value={pasteTitle}
                    onChange={(e) => setPasteTitle(e.target.value)}
                  />
                </div>
                <textarea
                  value={pasteContent}
                  onChange={(e) => setPasteContent(e.target.value)}
                  rows={5}
                  placeholder="小说片段、设定文档、评论、参考梗概..."
                />
                <button
                  className="ink-button"
                  onClick={handlePasteSubmit}
                  disabled={createTextMutation.isPending || !pasteContent.trim()}
                >
                  {createTextMutation.isPending ? '创建中...' : '创建素材'}
                </button>
              </div>

            </div>

            <div className="material-list-section">
              <div className="material-list-head">
                <span>素材库</span>
                <small>{materials.length} 份</small>
              </div>
              {materials.length === 0 ? (
                <div className="empty">暂无素材</div>
              ) : (
                <div className="material-stack">
                  {materials.map((m) => (
                    <div key={m.id} className="material-card">
                      <div className="material-card-top">
                        <div className="material-name">{m.title}</div>
                        <div className="material-card-actions">
                          <button
                            className="ghost-button"
                            onClick={() => openMaterialEditor(m)}
                          >
                            编辑
                          </button>
                          <button
                            className="ghost-button"
                            onClick={() => deleteMutation.mutate(m.id)}
                          >
                            删除
                          </button>
                        </div>
                      </div>
                      {editingMaterialId === m.id ? (
                        <div className="material-edit-form">
                          <label>
                            <span>素材名称</span>
                            <input value={editTitle} onChange={(e) => setEditTitle(e.target.value)} />
                          </label>
                          <label>
                            <span>分类</span>
                            <input value={editCategory} onChange={(e) => setEditCategory(e.target.value)} />
                          </label>
                          <label>
                            <span>标签</span>
                            <input value={editTags} onChange={(e) => setEditTags(e.target.value)} placeholder="用逗号分隔" />
                          </label>
                          <div className="material-edit-actions">
                            <button
                              className="ink-button"
                              onClick={() => updateMutation.mutate()}
                              disabled={updateMutation.isPending}
                            >
                              {updateMutation.isPending ? '保存中...' : '保存修改'}
                            </button>
                            <button className="ghost-button" onClick={() => setEditingMaterialId(null)}>
                              取消
                            </button>
                          </div>
                        </div>
                      ) : (
                        <>
                          <div className="material-meta">
                            {m.category || 'Uncategorized'} · {m.vectorChunkCount} 个向量块
                          </div>
                          <div className="material-tags">
                            {m.tags ? m.tags.split(',').slice(0, 5).map((t, i) => (
                              <span key={i} className="tag">{t.trim()}</span>
                            )) : null}
                          </div>
                        </>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
            </aside>
          </div>
        )}
      </div>
    </>
  );
}
