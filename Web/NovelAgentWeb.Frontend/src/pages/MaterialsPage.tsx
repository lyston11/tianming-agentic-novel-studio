import { useState, useRef, useCallback, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listMaterials,
  uploadMaterial,
  analyzeMaterial,
  deleteMaterialById,
  updateMaterialById,
  getMaterialContent,
} from '../api';
import { projectService } from '../services/projectService';
import type { MaterialReference } from '../api/types';
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
  const [pasteFileName, setPasteFileName] = useState('');
  const [ingestOpen, setIngestOpen] = useState(false);
  const [editingMaterialId, setEditingMaterialId] = useState<string | null>(null);
  const [loadingEditId, setLoadingEditId] = useState<string | null>(null);
  const [editFileName, setEditFileName] = useState('');
  const [editSummary, setEditSummary] = useState('');
  const [editTags, setEditTags] = useState('');
  const [editContent, setEditContent] = useState('');
  const [originalEditContent, setOriginalEditContent] = useState('');
  const [currentProjectId, setCurrentProjectId] = useState<string | null>(null);

  useEffect(() => {
    projectService.getCurrentProject().then((project) => {
      if (project) setCurrentProjectId(project.id);
    });
  }, []);

  const { isAnalyzing, analysisStages, currentStage, startAnalysis, updateProgress, completeAnalysis } =
    useMaterialStore();

  const { data: materialsData } = useQuery({
    queryKey: ['materials', currentProjectId],
    queryFn: () => currentProjectId ? listMaterials(currentProjectId) : Promise.resolve({ materials: [], totalCount: 0 }),
    enabled: !!currentProjectId
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => {
      if (!currentProjectId) throw new Error('No project selected');
      return uploadMaterial(currentProjectId, file, 'Reference', '');
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      addLog(`文件已上传: ${data.title}`);
      addLog(`正在自动拆解...`);
      startAnalysis(data.id);
      connectSSE(data.id);
    },
    onError: (err) => addLog(`上传失败: ${err}`),
  });

  const analyzeMutation = useMutation({
    mutationFn: analyzeMaterial,
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
      queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
      addLog(`拆解完成: ${data.fileName}，提取 ${data.totalEntriesCreated} 条知识`);
      completeAnalysis();
    },
    onError: (err) => { addLog(`分析失败: ${err}`); completeAnalysis(); },
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
        fileName: editFileName,
        summary: editSummary,
        tags: editTags,
        content: editContent !== originalEditContent ? editContent : '',
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

  const connectSSE = useCallback((materialId: string) => {
    const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';
    const es = new EventSource(`${BASE_URL}/materials/analyze/${materialId}/stream`);

    es.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        if (data.status === 'done' || data.status === 'failed') {
          es.close();
          completeAnalysis();
          queryClient.invalidateQueries({ queryKey: ['materials', currentProjectId] });
          queryClient.invalidateQueries({ queryKey: ['knowledgeEntries', currentProjectId] });
          if (data.status === 'done') addLog(data.message);
        } else {
          updateProgress(data);
        }
      } catch { /* ignore parse errors */ }
    };

    es.onerror = () => {
      es.close();
      completeAnalysis();
    };
  }, [addLog, completeAnalysis, queryClient, updateProgress, currentProjectId]);

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
    startAnalysis('paste');
    analyzeMutation.mutate({
      fileName: pasteFileName || `粘贴素材-${new Date().toISOString().slice(0, 10)}.txt`,
      content,
      sourceType: 'Paste',
    });
    setPasteContent('');
    setPasteFileName('');
  };

  const openMaterialEditor = async (material: MaterialReference) => {
    setEditingMaterialId(material.id);
    setEditFileName(material.fileName);
    setEditSummary(material.summary);
    setEditTags(material.tags.join('，'));
    setEditContent('');
    setOriginalEditContent('');
    setLoadingEditId(material.id);
    try {
      const result = await getMaterialContent(material.id);
      setEditContent(result.content);
      setOriginalEditContent(result.content);
    } catch (err) {
      addLog(`读取素材原文失败: ${err}`);
    } finally {
      setLoadingEditId(null);
    }
  };

  const handleReanalyzeMaterial = (materialId: string) => {
    startAnalysis(materialId);
    addLog('正在重新拆解素材...');
    connectSSE(materialId);
  };

  const materials = materialsData?.materials ?? [];
  return (
    <>
      <Topbar title="创意知识库" />

      <div className="materials-page">
        <main className="materials-knowledge">
          <KnowledgeBaseBrowser
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
                    value={pasteFileName}
                    onChange={(e) => setPasteFileName(e.target.value)}
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
                  disabled={analyzeMutation.isPending || !pasteContent.trim()}
                >
                  {analyzeMutation.isPending ? '拆解中...' : '拆解入库'}
                </button>
              </div>

              <AnalysisProgress
                stages={analysisStages}
                currentStage={currentStage}
                isAnalyzing={isAnalyzing}
              />
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
                        <div className="material-name">{m.fileName}</div>
                        <div className="material-card-actions">
                          <button
                            className="ghost-button"
                            onClick={() => openMaterialEditor(m)}
                          >
                            编辑
                          </button>
                          <button
                            className="ghost-button"
                            onClick={() => handleReanalyzeMaterial(m.id)}
                            disabled={isAnalyzing}
                          >
                            拆解
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
                            <input value={editFileName} onChange={(e) => setEditFileName(e.target.value)} />
                          </label>
                          <label>
                            <span>摘要</span>
                            <textarea value={editSummary} onChange={(e) => setEditSummary(e.target.value)} rows={3} />
                          </label>
                          <label>
                            <span>标签</span>
                            <input value={editTags} onChange={(e) => setEditTags(e.target.value)} placeholder="用逗号分隔" />
                          </label>
                          <label>
                            <span>原文</span>
                            <textarea
                              value={loadingEditId === m.id ? '正在读取素材原文...' : editContent}
                              onChange={(e) => setEditContent(e.target.value)}
                              rows={7}
                              disabled={loadingEditId === m.id}
                            />
                          </label>
                          <div className="material-edit-hint">
                            修改原文后，这份素材会标记为未拆解，需要重新拆解入库。
                          </div>
                          <div className="material-edit-actions">
                            <button
                              className="ink-button"
                              onClick={() => updateMutation.mutate()}
                              disabled={updateMutation.isPending || loadingEditId === m.id}
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
                            {m.sourceType} · {m.characterCount} 字
                            {m.isAnalyzed ? <span className="tag jade">已拆解</span> : <span className="tag">待拆解</span>}
                          </div>
                          <div className="material-summary">{m.summary}</div>
                          <div className="material-tags">
                            {m.tags.slice(0, 5).map((t, i) => (
                              <span key={i} className="tag">{t}</span>
                            ))}
                          </div>
                          {m.isAnalyzed && m.analysisResults.length > 0 && (
                            <div className="analysis-summary">
                              {m.analysisResults.map((r, i) => (
                                <span key={i} className="analysis-badge">
                                  {r.stageLabel}: {r.entriesCreated}
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
            </div>
            </aside>
          </div>
        )}
      </div>
    </>
  );
}
