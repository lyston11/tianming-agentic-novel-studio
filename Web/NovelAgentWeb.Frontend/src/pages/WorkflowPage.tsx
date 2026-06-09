import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  deleteNovelProject,
  listAgentSessions,
  sendChat,
} from '../api';
import type {
  AgentChapterTask,
  AgentMissionPlan,
  AgentScheduledTask,
  AgentSessionSummary,
  NovelAgentRun,
  NovelBookView,
  NovelChapterView,
  NovelVolumeView,
  PlotCandidate,
  WorkflowChapterArtifactSummary,
} from '../api/types';
import Topbar from '../components/layout/Topbar';
import '../styles/workflow.css';

type DraftChapter = NovelChapterView & {
  run: NovelAgentRun | null;
  artifact: WorkflowChapterArtifactSummary | null;
};

type PipelineStage = {
  key: string;
  label: string;
  statuses: string[];
};

type WorkbenchAction = {
  key: string;
  label: string;
  area: string;
  requiresChapter?: boolean;
};

const pipelineStages: PipelineStage[] = [
  { key: 'candidate', label: '候选', statuses: ['candidates_ready', 'candidate_selected', 'planned'] },
  { key: 'context', label: '上下文', statuses: ['context_ready'] },
  { key: 'draft', label: '草稿', statuses: ['draft_generated', 'repairing'] },
  { key: 'gate', label: '结构门禁', statuses: ['validated', 'gate_failed'] },
  { key: 'quality', label: '质量评审', statuses: ['quality_passed', 'quality_failed'] },
  { key: 'commit', label: '入库', statuses: ['committed'] },
];

const workbenchActions: WorkbenchAction[] = [
  { key: 'chapter_goal', label: '优化本章目标', area: '重新优化本章目标', requiresChapter: true },
  { key: 'candidates', label: '重做候选', area: '重新生成章节候选', requiresChapter: true },
  { key: 'context', label: '重建上下文', area: '重建章节上下文包', requiresChapter: true },
  { key: 'draft', label: '修复草稿', area: '修复当前草稿', requiresChapter: true },
  { key: 'quality', label: '重做评审', area: '重新执行门禁和质量评审', requiresChapter: true },
  { key: 'freeform', label: '按意见返工', area: '按用户意见返工', requiresChapter: false },
];

function statusLabel(chapter: DraftChapter) {
  if (chapter.userVisibleStatus) return chapter.userVisibleStatus;
  if (chapter.status === '卷草案') return '卷草案';
  const labels: Record<string, string> = {
    committed: '已入库',
    validated: '已校验',
    repairing: '修复中',
    gate_failed: '门禁失败',
    draft_generated: '草稿',
    context_ready: '上下文',
    candidate_selected: '已选',
    candidates_ready: '候选',
    planned: '已规划',
    unstarted: '待规划',
  };
  return labels[chapter.writingStatus] ?? chapter.status ?? '待规划';
}

function statusClass(chapter?: Partial<DraftChapter> | null) {
  const status = chapter?.artifactStatus || chapter?.writingStatus || chapter?.status || 'unstarted';
  if (status === 'committed' || status === 'quality_passed') return 'done';
  if (status === 'validated') return 'validated';
  if (status === 'gate_failed' || status === 'repairing' || status === 'quality_failed') return 'blocked';
  if (status === 'draft_generated') return 'drafting';
  if (status === 'context_ready') return 'context';
  if (status === 'candidate_selected') return 'selected';
  if (status === 'candidates_ready') return 'candidate';
  if (status === 'planned') return 'planned';
  return 'unstarted';
}

function progressPercent(book: NovelBookView) {
  if (book.plannedChapterCount <= 0) return 0;
  return Math.min(100, Math.round((book.generatedChapterCount / book.plannedChapterCount) * 100));
}

function missionChapters(plan?: AgentMissionPlan | null): AgentChapterTask[] {
  return plan?.bookTaskTree?.volumes.flatMap((volume) => volume.chapters) ?? [];
}

function missionStageLabel(plan?: AgentMissionPlan | null) {
  const value = plan?.status || plan?.stage || 'active';
  const labels: Record<string, string> = {
    foundation: '故事地基',
    volume_planning: '卷规划',
    chapter_work: '章节推进',
    awaiting_confirmation: '待确认',
    blocked: '阻塞',
    completed: '完成',
    active: '推进中',
    idle: '待启动',
  };
  return labels[value] ?? value;
}

function taskStatusLabel(status: string) {
  const labels: Record<string, string> = {
    queued: '排队',
    running: '运行中',
    blocked: '阻塞',
    waiting_confirmation: '待确认',
    paused: '暂停',
    done: '完成',
  };
  return labels[status] ?? status;
}

function firstCandidateTitle(candidates: PlotCandidate[]) {
  return candidates[0]?.title ?? '';
}

function flattenVolumes(
  volumes: NovelVolumeView[],
  runs: NovelAgentRun[],
  artifacts: WorkflowChapterArtifactSummary[],
) {
  const runByChapter = new Map<string, NovelAgentRun>();
  runs
    .filter((run) => run.targetChapterId)
    .forEach((run) => {
      const existing = runByChapter.get(run.targetChapterId);
      if (!existing || run.updatedAt > existing.updatedAt) runByChapter.set(run.targetChapterId, run);
    });

  const artifactByChapter = new Map<string, WorkflowChapterArtifactSummary>();
  artifacts.forEach((artifact) => {
    const existing = artifactByChapter.get(artifact.chapterId);
    if (!existing || artifact.updatedAt > existing.updatedAt) artifactByChapter.set(artifact.chapterId, artifact);
  });

  return volumes.map((volume) => ({
    ...volume,
    chapters: volume.chapters.map((chapter) => ({
      ...chapter,
      run: runByChapter.get(chapter.chapterId) ?? null,
      artifact: artifactByChapter.get(chapter.chapterId) ?? null,
    })),
  }));
}

function stageState(chapter: DraftChapter | null, stage: PipelineStage) {
  if (!chapter) return 'pending';
  const values = [
    chapter.writingStatus,
    chapter.artifactStatus,
    chapter.draftArtifactStatus,
    chapter.gateStatus,
    chapter.artifact?.draftStatus,
    chapter.artifact?.gateStatus,
    chapter.artifact?.qualityStatus,
  ].filter((value): value is string => !!value);
  if (stage.statuses.some((status) => values.some((value) => value.toLowerCase() === status.toLowerCase()))) {
    return values.some((value) => value.toLowerCase().includes('failed')) ? 'blocked' : 'done';
  }
  if (stage.key === 'quality' && (chapter.artifact?.qualityIssues.length ?? 0) > 0) return 'blocked';
  if (stage.key === 'commit' && chapter.visibleInLibrary) return 'done';
  return 'pending';
}

function buildMissionOnlyCards(sessions: AgentSessionSummary[], books: NovelBookView[]) {
  return sessions
    .map((session) => {
      const plan = session.memory?.missionPlan;
      const projectId = plan?.projectId || session.activeProjectId;
      if (!plan || !projectId || books.some((book) => book.projectId === projectId)) return null;
      return { session, plan, projectId };
    })
    .filter(Boolean) as { session: AgentSessionSummary; plan: AgentMissionPlan; projectId: string }[];
}

function bookActivityScore(book: NovelBookView, sessions: AgentSessionSummary[]) {
  const relatedSessions = sessions.filter((session) => {
    const plan = session.memory?.missionPlan;
    return session.activeProjectId === book.projectId || plan?.projectId === book.projectId || plan?.bookTaskTree?.projectId === book.projectId;
  }).filter((session) => !hasSuspectPlanForBook(book, session.memory?.missionPlan));
  const tasks = relatedSessions.flatMap((session) => session.memory?.missionPlan?.schedulerState?.tasks ?? [])
    .filter((task) => task.projectId === book.projectId);
  const runningTasks = tasks.filter((task) => task.status === 'running').length;
  const waitingTasks = tasks.filter((task) => task.status === 'waiting_confirmation').length;
  const blockedTasks = tasks.filter((task) => task.status === 'blocked').length;
  const chapterTasks = relatedSessions.flatMap((session) => missionChapters(session.memory?.missionPlan)).length;
  const emptyUntitledPenalty = isGenericTitle(normalizeLabel(book.title)) && tasks.length === 0 && chapterTasks === 0 && book.plannedChapterCount === 0 && book.generatedChapterCount === 0
    ? 500
    : 0;
  return runningTasks * 2000
    + waitingTasks * 1800
    + blockedTasks * 1500
    + tasks.length * 180
    + chapterTasks * 70
    + book.plannedChapterCount * 35
    + book.generatedChapterCount * 25
    - emptyUntitledPenalty;
}

function hasSuspectPlanForBook(book: NovelBookView, plan?: AgentMissionPlan | null) {
  if (!plan) return false;
  const bookTitle = normalizeLabel(book.title);
  if (!isGenericTitle(bookTitle)) {
    const planTitle = normalizeLabel(plan.projectTitle || plan.bookTaskTree?.title);
    return !!planTitle && planTitle !== bookTitle;
  }

  const volumeTitles = plan.bookTaskTree?.volumes
    .map((volume) => normalizeLabel(volume.title))
    .filter(Boolean) ?? [];
  return volumeTitles.some((title) => !isGenericTitle(title) && !normalizeLabel(book.coreHook).includes(title));
}

function normalizeLabel(value?: string | null) {
  return (value ?? '').replace(/[^\p{L}\p{N}]/gu, '').toLowerCase();
}

function isGenericTitle(value: string) {
  return !value || value.includes('未命名') || value.includes('当前小说');
}

function projectShortId(projectId?: string | null) {
  if (!projectId) return '';
  return projectId === 'default' ? 'default' : projectId.slice(0, 8);
}

function compactList(values: (string | undefined | null)[]) {
  return values
    .map((value) => (value ?? '').trim())
    .filter(Boolean)
    .slice(0, 8)
    .join(' / ');
}

function formatWorkbenchPrompt(args: {
  action: WorkbenchAction;
  projectId: string;
  sessionId: string;
  chapter: DraftChapter | null;
  runId: string;
  artifactStatus: string;
  currentStatus: string;
  issueSummary: string;
  userFeedback: string;
}) {
  return [
    `工作台操作：${args.action.area}`,
    `projectId: ${args.projectId}`,
    `sessionId: ${args.sessionId}`,
    `chapterId: ${args.chapter?.chapterId || '未选中章节'}`,
    `runId: ${args.runId || '无'}`,
    `artifactStatus: ${args.artifactStatus || '未知'}`,
    `当前状态: ${args.currentStatus || '未知'}`,
    `问题摘要: ${args.issueSummary || '用户从工作台要求重新检查这一块。'}`,
    `用户意见: ${args.userFeedback || '请按当前工作流状态判断最合适的返工动作。'}`,
    '',
    '请先判断是否需要重建候选、上下文包、修复草稿或重新评审，并按 ToolPolicy 执行。不要直接提交入库。',
  ].join('\n');
}

export default function WorkflowPage() {
  const queryClient = useQueryClient();
  const { data: agentSessions } = useQuery({
    queryKey: ['agentSessions'],
    queryFn: listAgentSessions,
    refetchInterval: 8000,
  });
  const [selectedChapterId, setSelectedChapterId] = useState<string | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [selectedWorkbenchAction, setSelectedWorkbenchAction] = useState(workbenchActions[0].key);
  const [workbenchFeedback, setWorkbenchFeedback] = useState('');
  const [workbenchNotice, setWorkbenchNotice] = useState('');

  // TODO: Full workflow API migration pending backend endpoints
  // Missing: getProjectWorkflow, NovelLibrary APIs for books/runs/artifacts
  // Temporary empty state to prevent runtime errors until backend is ready
  const books: NovelBookView[] = [];
  const activeBook = books.find((book) => book.isActive) ?? books[0] ?? null;
  const missionOnlyCards = buildMissionOnlyCards(agentSessions ?? [], books);
  const fallbackProjectId = useMemo(() => {
    const sessions = agentSessions ?? [];
    const scoredBooks = books
      .map((book) => ({ projectId: book.projectId, score: bookActivityScore(book, sessions) }))
      .sort((a, b) => b.score - a.score);
    const activeMissionCard = missionOnlyCards
      .map((card) => ({
        projectId: card.projectId,
        score: missionChapters(card.plan).length * 35 + (card.plan.schedulerState?.tasks.length ?? 0) * 120,
      }))
      .sort((a, b) => b.score - a.score);
    return scoredBooks.find((item) => item.score > 0)?.projectId
      ?? activeMissionCard.find((item) => item.score > 0)?.projectId
      ?? activeBook?.projectId
      ?? books[0]?.projectId
      ?? missionOnlyCards[0]?.projectId
      ?? null;
  }, [activeBook?.projectId, agentSessions, books, missionOnlyCards]);
  const effectiveProjectId = selectedProjectId ?? fallbackProjectId;

  // TODO: Restore workflow query when backend endpoints are available
  // const { data: workflow, isLoading: workflowLoading } = useQuery({
  //   queryKey: ['projectWorkflow', effectiveProjectId],
  //   queryFn: () => getProjectWorkflow(effectiveProjectId!),
  //   enabled: !!effectiveProjectId,
  //   refetchInterval: 5000,
  // });
  const workflowLoading = false;

  const selectedBook = books.find((book) => book.projectId === effectiveProjectId) ?? activeBook;
  const volumes = useMemo(
    () => flattenVolumes([], [], []),
    [],
  );
  const chapters = volumes.flatMap((volume) => volume.chapters);
  const selectedChapter = useMemo<DraftChapter | null>(() => {
    if (chapters.length === 0) return null;
    return (
      chapters.find((chapter) => chapter.chapterId === selectedChapterId) ??
      chapters.find((chapter) => chapter.run || chapter.artifact?.hasDraft || chapter.visibleInWorkflow) ??
      chapters[0]
    );
  }, [chapters, selectedChapterId]);

  const selectedRun = selectedChapter?.run ?? null;
  const selectedArtifact = selectedChapter?.artifact ?? null;
  const selectedBrief = selectedRun?.chapterBrief ?? null;
  const selectedCandidate = selectedBrief?.selectedCandidateTitle || selectedBrief?.recommendedCandidateTitle || firstCandidateTitle(selectedBrief?.candidates ?? []);
  const selectedVolume = volumes.find((volume) => volume.volumeId === selectedChapter?.volumeId) ?? volumes[0];
  const selectedPlan = null;
  const selectedTreeChapters = missionChapters(selectedPlan);
  const selectedTreeChapter = selectedTreeChapters.find((chapter) => chapter.chapterId === selectedChapter?.chapterId) ?? null;
  const selectedQualityIssues = selectedTreeChapters
    .filter((chapter) => chapter.qualityIssueSummary || chapter.gateIssueSummary)
    .slice(0, 5);
  const selectedProjectTaskQueue: AgentScheduledTask[] = [];
  const generatedCount = selectedBook?.generatedChapterCount ?? 0;
  const plannedCount = selectedBook?.plannedChapterCount ?? 0;
  const needsRewriteCount = selectedBook?.needsRewriteCount ?? 0;
  const activeSessionTitle = '未绑定会话';
  const activeWorkflowSessionId = '';
  const selectedAction = workbenchActions.find((action) => action.key === selectedWorkbenchAction) ?? workbenchActions[0];
  const selectedRunId = selectedRun?.runId || selectedArtifact?.runId || selectedChapter?.runId || '';
  const selectedArtifactStatus = selectedChapter?.artifactStatus
    || selectedArtifact?.status
    || selectedArtifact?.draftStatus
    || selectedChapter?.draftArtifactStatus
    || selectedChapter?.writingStatus
    || '';
  const selectedIssueSummary = compactList([
    selectedTreeChapter?.gateIssueSummary,
    selectedTreeChapter?.qualityIssueSummary,
    ...(selectedChapter?.gateIssues ?? []),
    ...(selectedArtifact?.gateIssues ?? []),
    ...(selectedArtifact?.qualityIssues ?? []),
    ...(selectedArtifact?.dependencyWarnings ?? []),
  ]);
  const hasWorkflowSession = !!activeWorkflowSessionId;
  const canUseWorkbenchAction = hasWorkflowSession && (!selectedAction.requiresChapter || !!selectedChapter);
  const hasCurrentArtifacts = !!selectedArtifact || !!selectedRun || chapters.some((chapter) => chapter.artifact || chapter.run || chapter.hasGeneratedContent || chapter.visibleInWorkflow);
  const canRemoveEmptyProject = !!selectedBook
    && !selectedBook.isActive
    && generatedCount === 0
    && plannedCount === 0
    && !hasCurrentArtifacts
    && false;
  const diagnosticReasons: string[] = [];
  const diagnosticTaskCount = 0;

  useEffect(() => {
    if (!selectedProjectId && fallbackProjectId) setSelectedProjectId(fallbackProjectId);
  }, [fallbackProjectId, selectedProjectId]);

  useEffect(() => {
    setSelectedChapterId(null);
  }, [effectiveProjectId]);

  const selectProject = (projectId: string) => {
    setSelectedProjectId(projectId);
    setSelectedChapterId(null);
  };

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
  };

  const refreshAsync = async () => {
    await queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
  };

  const agentActionMutation = useMutation({
    mutationFn: (message: string) => sendChat({ sessionId: activeWorkflowSessionId, message }),
    onSuccess: async () => {
      setWorkbenchFeedback('');
      setWorkbenchNotice('已交给 Agent 处理，工作台会跟随任务黑板刷新。');
      await refreshAsync();
    },
  });

  const removeProjectMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async () => {
      setSelectedProjectId(null);
      setSelectedChapterId(null);
      setWorkbenchNotice('空项目已移除。');
      await refreshAsync();
    },
  });

  const submitWorkbenchAction = async () => {
    if (!effectiveProjectId || !activeWorkflowSessionId) return;
    const message = formatWorkbenchPrompt({
      action: selectedAction,
      projectId: effectiveProjectId,
      sessionId: activeWorkflowSessionId,
      chapter: selectedChapter,
      runId: selectedRunId,
      artifactStatus: selectedArtifactStatus,
      currentStatus: selectedChapter ? statusLabel(selectedChapter) : missionStageLabel(selectedPlan),
      issueSummary: selectedIssueSummary,
      userFeedback: workbenchFeedback.trim(),
    });
    await agentActionMutation.mutateAsync(message);
  };

  const archiveWorkflow = async () => {
    // TODO: Restore when workflow sessions are available
    window.alert('工作流归档功能需要完整的 workflow API 支持');
  };

  const removeEmptyProject = async () => {
    if (!effectiveProjectId || !selectedBook) return;
    if (selectedBook.isActive) {
      window.alert('当前项目是 active，删除会切换工作区。请先切换 active project，或后续使用安全删除接口。');
      return;
    }
    if (!canRemoveEmptyProject) return;
    const ok = window.confirm(`移除空项目「${selectedBook.title}」？该操作只用于清理没有章节和草稿的项目。`);
    if (!ok) return;
    await removeProjectMutation.mutateAsync(effectiveProjectId);
  };

  return (
    <>
      <Topbar
        title="Agent 生产工作台"
        actions={
          <button className="ghost-button" onClick={refresh}>
            刷新工作台
          </button>
        }
      />

      <div className="ops-workspace">
        <section className="ops-header">
          <div>
            <span className="ops-kicker">Mission Control</span>
            <h2>{selectedBook?.title || '未选择小说任务'}</h2>
            <p>{selectedBook?.coreHook || 'Agent 创建的新书、草稿、门禁和质量评审会先进入这里。'}</p>
          </div>
          <div className="ops-status-grid">
            <strong>0<small>会话</small></strong>
            <strong>{plannedCount}<small>章节位</small></strong>
            <strong>{generatedCount}<small>已入库</small></strong>
            <strong>{needsRewriteCount}<small>需返工</small></strong>
          </div>
          <div className="ops-live-card">
            <span>{missionStageLabel(selectedPlan)}</span>
            <strong>{activeSessionTitle}</strong>
            <small>{'工作台自动刷新中'}</small>
          </div>
        </section>

        <section className="ops-board">
          <aside className="ops-project-rail">
            <div className="ops-panel-head">
              <span>项目 / 会话</span>
              <strong>{books.length + missionOnlyCards.length}</strong>
            </div>
            <div className="ops-project-list">
              {books.map((book) => (
                <button
                  key={book.projectId}
                  className={`ops-project-card ${book.projectId === effectiveProjectId ? 'selected' : ''} ${book.isActive ? 'active' : ''}`}
                  onClick={() => selectProject(book.projectId)}
                >
                  <span>{book.status || 'Drafting'} · {projectShortId(book.projectId)}</span>
                  <strong>{book.title}</strong>
                  <small>{book.coreHook || book.selectedChapter?.summary || '等待 Agent 补齐地基'}</small>
                  <i><b style={{ width: `${progressPercent(book)}%` }} /></i>
                  <em>{book.generatedChapterCount}/{book.plannedChapterCount || 0} 章 · {bookActivityScore(book, agentSessions ?? []) > 0 ? '有活动' : '空项目'}</em>
                </button>
              ))}
              {missionOnlyCards.map(({ session, plan, projectId }) => (
                <button
                  key={session.sessionId}
                  className={`ops-project-card mission ${projectId === effectiveProjectId ? 'selected' : ''}`}
                  onClick={() => selectProject(projectId)}
                >
                  <span>{missionStageLabel(plan)}</span>
                  <strong>{plan.projectTitle || session.title || 'Agent 小说任务'}</strong>
                  <small>{plan.currentNovelGoal || plan.overallGoal || session.phase}</small>
                  <em>{missionChapters(plan).length} 章任务</em>
                </button>
              ))}
            </div>
          </aside>

          <main className="ops-stage">
            <div className="ops-stage-head">
              <div>
                <span>{selectedVolume?.title || '章节生产线'}</span>
                <h3>{selectedChapter?.title || '等待章节任务'}</h3>
              </div>
              <strong className={`ops-state ${statusClass(selectedChapter)}`}>{selectedChapter ? statusLabel(selectedChapter) : '未开始'}</strong>
            </div>

            <div className="ops-pipeline">
              {pipelineStages.map((stage) => (
                <div key={stage.key} className={`ops-pipeline-step ${stageState(selectedChapter, stage)}`}>
                  <span>{stage.label}</span>
                </div>
              ))}
            </div>

            <div className="ops-main-grid">
              <aside className="ops-chapter-rail">
                {workflowLoading ? (
                  <div className="empty compact">正在载入项目工作流...</div>
                ) : volumes.length === 0 ? (
                  <div className="empty compact">这个项目还没有章节生产线。让 Agent 先建立地基和卷规划。</div>
                ) : volumes.map((volume) => (
                  <section key={volume.volumeId} className="ops-volume-group">
                    <div className="ops-volume-title">
                      <strong>{volume.title}</strong>
                      <span>{volume.chapters.length} 章</span>
                    </div>
                    {volume.chapters.map((chapter) => (
                      <button
                        key={chapter.chapterId}
                        className={`ops-chapter-row ${selectedChapter?.chapterId === chapter.chapterId ? 'active' : ''}`}
                        onClick={() => setSelectedChapterId(chapter.chapterId)}
                      >
                        <i className={statusClass(chapter)}>{chapter.beatIndex || '-'}</i>
                        <span>
                          <strong>{chapter.title}</strong>
                          <small>{chapter.userVisibleStatus || chapter.chapterId}</small>
                        </span>
                      </button>
                    ))}
                  </section>
                ))}
              </aside>

              <section className="ops-detail-stack">
                <div className="ops-brief-grid">
                  <article>
                    <span>本章目标</span>
                    <p>{selectedChapter?.goal || selectedBrief?.coreIdea || '等待 Agent 生成章节目标。'}</p>
                  </article>
                  <article>
                    <span>冲突转折</span>
                    <p>{selectedChapter?.turn || selectedBrief?.conflictMove || '等待候选生成。'}</p>
                  </article>
                  <article>
                    <span>代价</span>
                    <p>{selectedChapter?.cost || selectedBrief?.costOrConsequence || '等待候选生成。'}</p>
                  </article>
                </div>

                <section className="ops-gate-card">
                  <div className="ops-card-title">
                    <div>
                      <span>Gate / Quality</span>
                      <h4>{selectedChapter?.gateStatus || selectedArtifact?.gateStatus || '等待门禁'}</h4>
                    </div>
                    <em>{selectedChapter?.repairAttemptCount ?? 0} 次修复</em>
                  </div>
                  <div className="ops-check-grid">
                    <strong className={selectedChapter?.changesProtocolPassed ? 'pass' : 'fail'}>CHANGES</strong>
                    <strong className={selectedChapter?.factSnapshotPassed ? 'pass' : 'fail'}>事实快照</strong>
                    <strong className={selectedChapter?.blueprintPassed ? 'pass' : 'fail'}>章节蓝图</strong>
                    <strong className={selectedChapter?.longDistanceRagPassed ? 'pass' : 'fail'}>长距 RAG · {selectedArtifact?.ragRecallCount ?? selectedChapter?.ragRecallCount ?? 0}</strong>
                  </div>
                  <div className="ops-issue-columns">
                    <div>
                      <span>门禁问题</span>
                      {(selectedChapter?.gateIssues.length || selectedArtifact?.gateIssues.length) ? (
                        [...(selectedChapter?.gateIssues ?? []), ...(selectedArtifact?.gateIssues ?? [])].slice(0, 5).map((issue) => <p key={issue}>{issue}</p>)
                      ) : <p>暂无结构门禁阻塞。</p>}
                    </div>
                    <div>
                      <span>质量问题</span>
                      {selectedArtifact?.qualityIssues.length ? (
                        selectedArtifact.qualityIssues.slice(0, 5).map((issue) => <p key={issue}>{issue}</p>)
                      ) : <p>{selectedArtifact?.qualityStatus || '等待 Reflect 质量裁判。'}</p>}
                    </div>
                    <div>
                      <span>依赖影响</span>
                      {selectedArtifact?.dependencyWarnings.length || selectedChapter?.dependencyWarnings.length ? (
                        [...(selectedArtifact?.dependencyWarnings ?? []), ...(selectedChapter?.dependencyWarnings ?? [])].slice(0, 5).map((warning) => <p key={warning}>{warning}</p>)
                      ) : <p>暂无依赖重建或重校验要求。</p>}
                    </div>
                  </div>
                </section>

                {selectedBrief ? (
                  <section className="ops-candidate-card">
                    <div className="ops-card-title">
                      <div>
                        <span>Chapter Candidates</span>
                        <h4>{selectedBrief.chapterId}</h4>
                      </div>
                      {selectedCandidate && <em>推荐：{selectedCandidate}</em>}
                    </div>
                    <div className="ops-candidate-grid">
                      {selectedBrief.candidates.map((candidate) => (
                        <article key={candidate.title} className={candidate.title === selectedCandidate ? 'recommended' : ''}>
                          <strong>{candidate.title}</strong>
                          <p>{candidate.coreTwist}</p>
                          <small>{candidate.characterChoice} / {candidate.costOrConsequence}</small>
                        </article>
                      ))}
                    </div>
                  </section>
                ) : null}

                {(selectedArtifact?.draftPreview || selectedRun?.draftArtifact?.draftContent || selectedChapter?.hasGeneratedContent) && (
                  <section className="ops-draft-card">
                    <div className="ops-card-title">
                      <div>
                        <span>Draft Artifact</span>
                        <h4>{selectedChapter?.visibleInLibrary ? '已入库正文' : selectedArtifact?.draftStatus || selectedChapter?.draftArtifactStatus || '生成中草稿'}</h4>
                      </div>
                      <em>{selectedChapter?.wordCount || 0} 字</em>
                    </div>
                    <div className="ops-draft-preview">
                      {(selectedArtifact?.draftPreview || selectedRun?.draftArtifact?.draftContent || selectedChapter?.content || '')
                        .split(/\n{2,}/)
                        .slice(0, 5)
                        .map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                    </div>
                  </section>
                )}
              </section>
            </div>
          </main>

          <aside className="ops-side-panel">
            <section className="ops-side-card ops-action-card">
              <div className="ops-inspector-title">
                <span>Action Dock</span>
                <strong>Agent 返工</strong>
              </div>
              {!hasWorkflowSession ? (
                <p className="ops-muted-line">未绑定可操作会话</p>
              ) : (
                <>
                  <div className="ops-action-grid">
                    {workbenchActions.map((action) => (
                      <button
                        key={action.key}
                        type="button"
                        className={selectedWorkbenchAction === action.key ? 'selected' : ''}
                        onClick={() => setSelectedWorkbenchAction(action.key)}
                      >
                        {action.label}
                      </button>
                    ))}
                  </div>
                  <textarea
                    value={workbenchFeedback}
                    onChange={(event) => setWorkbenchFeedback(event.target.value)}
                    placeholder="指出哪里不好，或写下你想让 Agent 怎么返工。"
                    rows={3}
                  />
                  <button
                    className="ink-button"
                    type="button"
                    onClick={() => void submitWorkbenchAction()}
                    disabled={!canUseWorkbenchAction || agentActionMutation.isPending}
                  >
                    交给 Agent 返工
                  </button>
                  {!selectedChapter && selectedAction.requiresChapter ? (
                    <small>请先选中一个章节，再发起这个返工动作。</small>
                  ) : (
                    <small>目标会话 {projectShortId(activeWorkflowSessionId)}</small>
                  )}
                </>
              )}
              {workbenchNotice && <p className="ops-notice">{workbenchNotice}</p>}
              {agentActionMutation.isError && <p className="ops-error">Agent 操作没有完成，请稍后重试。</p>}
            </section>

            <section className="ops-side-card ops-decision-card quiet">
              <div className="ops-inspector-title">
                <span>Decision Strip</span>
                <strong>无待确认</strong>
              </div>
              <p className="ops-muted-line">无待确认</p>
            </section>

            <section className="ops-side-card ops-compact-card">
              <div className="ops-inspector-title">
                <span>当前项目调度</span>
                <strong>{selectedProjectTaskQueue.length}</strong>
              </div>
              {selectedProjectTaskQueue.length === 0 ? (
                <p className="ops-muted-line">暂无调度任务</p>
              ) : selectedProjectTaskQueue.slice(0, 5).map((task: AgentScheduledTask) => (
                <div key={task.taskId} className={`ops-task-row ${task.status}`}>
                  <strong>{task.chapterId || task.taskType}</strong>
                  <small>{taskStatusLabel(task.status)} · {task.nextAction || task.taskType}</small>
                  {task.blockedReason && <em>{task.blockedReason}</em>}
                </div>
              ))}
            </section>

            <section className="ops-side-card ops-compact-card">
              <div className="ops-inspector-title">
                <span>质量关注</span>
                <strong>{selectedQualityIssues.length}</strong>
              </div>
              {selectedQualityIssues.length === 0 ? (
                <p className="ops-muted-line">无质量阻塞</p>
              ) : selectedQualityIssues.map((chapter) => (
                <div key={chapter.chapterId} className="ops-task-row blocked">
                  <strong>{chapter.title || chapter.chapterId}</strong>
                  <small>{chapter.status} · {chapter.nextAction || '等待 Agent 判断'}</small>
                  <em>{chapter.qualityIssueSummary || chapter.gateIssueSummary}</em>
                </div>
              ))}
            </section>

            <details className="ops-side-card ops-detail-fold" open={diagnosticReasons.length + diagnosticTaskCount > 0}>
              <summary>
                <span>历史诊断</span>
                <strong>{diagnosticReasons.length + diagnosticTaskCount}</strong>
              </summary>
              {diagnosticReasons.length + diagnosticTaskCount === 0 ? (
                <p className="ops-muted-line">无历史诊断</p>
              ) : (
                <>
                  {diagnosticReasons.slice(0, 5).map((reason) => (
                    <p key={reason}>{reason}</p>
                  ))}
                </>
              )}
            </details>

            <details className="ops-side-card ops-detail-fold">
              <summary>
                <span>工作流维护</span>
                <strong>维护</strong>
              </summary>
              <p>归档只隐藏会话工作流；移除项目只开放给非 active 的空项目。</p>
              <div className="ops-maintenance-actions">
                <button
                  className="ghost-button"
                  type="button"
                  onClick={() => void archiveWorkflow()}
                  disabled={true}
                >
                  归档工作流
                </button>
                <button
                  className="danger-button compact"
                  type="button"
                  onClick={() => void removeEmptyProject()}
                  disabled={!canRemoveEmptyProject || removeProjectMutation.isPending}
                  title={selectedBook?.isActive ? '当前项目是 active，不能从工作台直接删除' : undefined}
                >
                  移除空项目
                </button>
              </div>
              {selectedBook?.isActive && generatedCount === 0 ? (
                <small className="ops-warning">active 空项目暂不直接删除。</small>
              ) : null}
            </details>
          </aside>
        </section>
      </div>
    </>
  );
}
