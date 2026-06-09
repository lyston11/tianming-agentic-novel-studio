import { useState, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listMaterials,
  uploadMaterial,
  deleteMaterialById,
  updateMaterialById,
  createMaterialFromText,
} from '../api';
import type { MaterialResponse } from '../api/types';
import { projectService } from '../services/projectService';
import { useMaterialStore } from '../stores/useMaterialStore';
import { useAppStore } from '../stores/useAppStore';
import Topbar from '../components/layout/Topbar';
import AnalysisProgress from '../components/materials/AnalysisProgress';
import KnowledgeBaseBrowser from '../components/materials/KnowledgeBaseBrowser';
import '../styles/materials.css';

export default function MaterialsPage() {
  const queryClient = useQueryClient();
  const addLog = useAppStore((s) => s.addLog);
  const fileRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const [pasteContent, setPasteContent] = useState('');
  const [pasteTitle, setPasteTitle] = useState('');
  const [ingestOpen, setIngestOpen] = useState(false);
  const [editingMaterialId, setEditingMaterialId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editCategory, setEditCategory] = useState('');
  const [editTags, setEditTags] = useState('');

  const { data: currentProject } = useQuery({
    queryKey: ['currentProject'],
    queryFn: () => projectService.getCurrentProject(),
    staleTime: 5 * 60 * 1000,
  });
  const currentProjectId = currentProject?.id ?? null;

  const { isAnalyzing, analysisStages, currentStage } = useMaterialStore();

  const { data: materialsData } = useQuery({
    queryKey: ['materials', currentProjectId],
    queryFn: () => currentProjectId ? listMaterials(currentProjectId) : Promise.resolve({ materials: [], totalCount: 0 }),
    enabled: !!currentProjectId
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => {
      if (!currentProjectId) throw new Error('No project selected');
      return uploadMaterial(currentProjectId, file, 'Research');
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      addLog('素材上传成功');
    },
    onError: (err) => addLog(`上传失败: ${err}`),
  });

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

  const handleFile = async (file: File) => {
    uploadMutation.mutate(file);
  };

  const handleFileInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) handleFile(file);
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
  return (
    <>
      <Topbar title="创意知识库" />

      <div className="materials-page">
        <main className="materials-knowledge">
          <KnowledgeBaseBrowser
            projectId={currentProjectId || ''}
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
                <span>摄取素材</span>
                <small>上传或粘贴后自动拆解</small>
              </div>

              <div
                className={`upload-zone ${dragOver ? 'drag-over' : ''}`}
                onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                onClick={() => fileRef.current?.click()}
              >
                <input
                  ref={fileRef}
                  type="file"
                  accept=".txt,.epub,.pdf"
                  onChange={handleFileInput}
                  style={{ display: 'none' }}
                />
                <div className="upload-icon">+</div>
                <div className="upload-text">
                  {uploadMutation.isPending ? '上传中...' : '选择文件'}
                </div>
                <div className="upload-hint">.txt / .epub / .pdf</div>
              </div>

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

              {isAnalyzing && (
                <AnalysisProgress
                  stages={analysisStages}
                  currentStage={currentStage}
                  isAnalyzing={isAnalyzing}
                />
              )}
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
