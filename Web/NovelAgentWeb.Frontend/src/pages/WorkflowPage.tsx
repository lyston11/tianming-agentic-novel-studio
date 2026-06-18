import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import {
  deleteNovelProject,
  getProjectWorkflow,
  getWorkspace,
  listAgentSessions,
  sendChat,
} from '../api';
import type {
  AgentMissionPlan,
  AgentScheduledTask,
  AgentSessionSummary,
  NovelBookView,
  NovelChapterView,
  NovelProjectInfo,
  NovelVolumeView,
  WorkflowArtifactTimelineItem,
} from '../api/types';
import Topbar from '../components/layout/Topbar';
import { useProjectStore } from '../stores/useProjectStore';
import '../styles/workflow.css';

type ArtifactFilter = 'all' | 'workflow' | 'library';
type ChapterDetailTab = 'manuscript' | 'workflow' | 'issues' | 'artifacts';
type ChapterProgressStatus = 'done' | 'running' | 'blocked' | 'drafting' | 'unstarted';

interface ChapterProgressStep {
  key: string;
  label: string;
  status: ChapterProgressStatus;
  caption: string;
  artifactId?: string;
  summary: string;
}

interface ArtifactLedgerGroup {
  key: string;
  label: string;
  surface: string;
  artifacts: WorkflowArtifactTimelineItem[];
}

function progressPercent(book: NovelBookView) {
  if (book.plannedChapterCount <= 0) return 0;
  return Math.min(100, Math.round((book.generatedChapterCount / book.plannedChapterCount) * 100));
}

function missionChapters(plan?: AgentMissionPlan | null) {
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

function normalizeLabel(value?: string | null) {
  return (value ?? '').replace(/[^\p{L}\p{N}]/gu, '').toLowerCase();
}

function isGenericTitle(value: string) {
  return !value || value.includes('未命名') || value.includes('当前小说');
}

function bookActivityScore(book: NovelBookView, sessions: AgentSessionSummary[]) {
  const relatedSessions = sessions.filter((session) => session.activeProjectId === book.projectId);
  const messageCount = relatedSessions.reduce((sum, session) => sum + session.messageCount, 0);
  const emptyUntitledPenalty = isGenericTitle(normalizeLabel(book.title)) && relatedSessions.length === 0 && book.plannedChapterCount === 0 && book.generatedChapterCount === 0
    ? 500
    : 0;
  return relatedSessions.length * 120
    + messageCount * 8
    + book.plannedChapterCount * 35
    + book.generatedChapterCount * 25
    - emptyUntitledPenalty;
}

function projectShortId(projectId?: string | null) {
  if (!projectId) return '';
  return projectId === 'default' ? 'default' : projectId.slice(0, 8);
}

function bookToProjectInfo(book: NovelBookView): NovelProjectInfo {
  return {
    id: book.projectId,
    title: book.title,
    genre: book.genre,
    subGenre: book.subGenre,
    coreHook: book.coreHook,
    readerPromise: book.readerPromise,
    status: book.status,
    createdAt: book.updatedAt,
    updatedAt: book.updatedAt,
  };
}

function artifactClass(artifact?: WorkflowArtifactTimelineItem | null) {
  const status = (artifact?.status ?? '').toLowerCase();
  if (artifact?.isFinal) return 'done';
  if (status.includes('fail') || status.includes('blocked') || status.includes('rewrite')) return 'blocked';
  if (status.includes('running') || status.includes('executing')) return 'running';
  if (artifact) return 'drafting';
  return 'unstarted';
}

function isWeakArtifactTitle(title?: string | null) {
  const value = (title ?? '').trim();
  return !value
    || /^chapter-\d+/i.test(value)
    || value === '目标推进'
    || value === '章节推进';
}

function artifactDisplayTitle(artifact: WorkflowArtifactTimelineItem, chapterTitleById: Map<string, string>) {
  const chapterTitle = artifact.chapterId ? chapterTitleById.get(artifact.chapterId) : '';
  if (chapterTitle && isWeakArtifactTitle(artifact.title)) return chapterTitle;
  return artifact.title || chapterTitle || artifact.label;
}

function artifactSummaryText(artifact: WorkflowArtifactTimelineItem, chapterTitleById: Map<string, string>) {
  const title = artifactDisplayTitle(artifact, chapterTitleById);
  const summary = (artifact.summary || '').trim();
  if (!summary || normalizeLabel(summary) === normalizeLabel(title)) {
    if (artifact.isFinal) return '已进入小说书城，可在正文成稿中查看。';
    return artifact.status || '过程产物';
  }
  return summary;
}

function chapterStatusClass(chapter?: NovelChapterView | null) {
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

function chapterStatusLabel(chapter: NovelChapterView) {
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

function formatTime(value?: string | null) {
  if (!value) return '暂无时间';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function visiblePreview(value: string) {
  const text = value.trim();
  if (!text) return '';
  return text.length <= 1200 ? text : `${text.slice(0, 1200)}...`;
}

function manuscriptPreview(value: string) {
  const text = value.trim();
  if (!text) return [];
  const paragraphs = text
    .split(/\n+/)
    .map((paragraph) => paragraph.trim())
    .filter(Boolean);
  if (paragraphs.length > 1 || text.length < 900) return paragraphs.slice(0, 12);

  const chunks: string[] = [];
  let current = '';
  for (const sentence of text.match(/[^。！？!?]+[。！？!?]?/g) ?? [text]) {
    if (current.length > 520) {
      chunks.push(current);
      current = sentence;
    } else {
      current += sentence;
    }
  }
  if (current) chunks.push(current);
  return chunks.slice(0, 12);
}

function artifactSurfaceFilter(artifact: WorkflowArtifactTimelineItem, filter: ArtifactFilter) {
  if (filter === 'library') return artifact.surface === '小说书城' || artifact.kind === 'library_chapter';
  if (filter === 'workflow') return artifact.surface !== '小说书城' && artifact.kind !== 'scheduled_task';
  return artifact.kind !== 'scheduled_task';
}

function buildMissionOnlyCards(_sessions: AgentSessionSummary[], _books: NovelBookView[]) {
  return [] as { session: AgentSessionSummary; plan: AgentMissionPlan; projectId: string }[];
}

function buildAgentContextMessage(args: {
  projectId: string;
  sessionId: string;
  selectedArtifact: WorkflowArtifactTimelineItem | null;
  selectedChapter: NovelChapterView | null;
  userFeedback: string;
}) {
  const lines = [
    '用户在创作工作流页面补充了要求。',
    `projectId: ${args.projectId}`,
    `sessionId: ${args.sessionId}`,
    `selectedArtifactId: ${args.selectedArtifact?.id || '无'}`,
    `artifactKind: ${args.selectedArtifact?.kind || '无'}`,
    `chapterId: ${args.selectedArtifact?.chapterId || args.selectedChapter?.chapterId || '无'}`,
    `runId: ${args.selectedArtifact?.runId || args.selectedChapter?.runId || '无'}`,
    `当前产物状态: ${args.selectedArtifact?.status || (args.selectedChapter ? chapterStatusLabel(args.selectedChapter) : '未知')}`,
    '',
    args.userFeedback || '请结合当前工作流真实产物，判断下一步应该推进、解释、修订还是等待用户确认。',
  ];
  return lines.join('\n');
}

function firstRealChapter(volumes: NovelVolumeView[]) {
  return volumes
    .flatMap((volume) => volume.chapters)
    .find((chapter) => isLibraryChapter(chapter) || chapter.visibleInWorkflow || chapter.hasGeneratedContent || chapter.runId || chapter.summary);
}

function isLibraryChapter(chapter: NovelChapterView) {
  return chapter.visibleInLibrary
    || chapter.artifactStatus === 'committed'
    || chapter.writingStatus === 'committed'
    || ['committed', 'published', 'completed'].includes((chapter.status || '').toLowerCase());
}

function hasReadableManuscript(chapter?: NovelChapterView | null) {
  return !!chapter && isLibraryChapter(chapter) && chapter.content.trim().length > 0;
}

function chapterQualityWarnings(chapter?: NovelChapterView | null) {
  if (!chapter) return [] as string[];
  const warnings: string[] = [];
  if (hasReadableManuscript(chapter) && chapter.wordCount > 0 && chapter.wordCount < 3000) {
    warnings.push(`正文 ${chapter.wordCount} 字，低于当前章节成稿目标 3000 字，建议进入扩写/修订。`);
  }
  if (!hasReadableManuscript(chapter)) {
    warnings.push(isLibraryChapter(chapter) ? '这一章已标记入库，但没有可展示正文，需要检查提交内容。' : '这一章还没有进入书城成稿，当前只能查看工作流产物。');
  }
  if (chapter.needsRewrite) warnings.push('质量评审标记为需要重写。');
  if (chapter.gateIssues.length > 0) warnings.push(`结构门禁还有 ${chapter.gateIssues.length} 个问题。`);
  if (!chapter.changesProtocolPassed && chapter.hasGeneratedContent) warnings.push('修订记录协议未通过，需重新生成或修复 CHANGES。');
  return warnings;
}

function artifactPriority(artifact: WorkflowArtifactTimelineItem) {
  const order: Record<string, number> = {
    quality_review: 80,
    gate_report: 70,
    draft_artifact: 60,
    chapter_artifact_summary: 55,
    library_chapter: 50,
    context_package: 40,
    chapter_brief: 30,
  };
  return order[artifact.kind] ?? 10;
}

function progressStatusFromArtifact(
  artifact: WorkflowArtifactTimelineItem | undefined,
  fallbackDone = false,
): ChapterProgressStatus {
  if (!artifact) return fallbackDone ? 'done' : 'unstarted';
  const status = artifactClass(artifact);
  if (status === 'blocked' || status === 'running') return status;
  if (artifact.isFinal || status === 'done') return 'done';
  return 'done';
}

function progressCaption(status: ChapterProgressStatus) {
  const labels: Record<ChapterProgressStatus, string> = {
    done: '已完成',
    running: '执行中',
    blocked: '需处理',
    drafting: '生成中',
    unstarted: '待生成',
  };
  return labels[status];
}

function buildChapterProgressSteps(
  chapter: NovelChapterView | null,
  artifacts: WorkflowArtifactTimelineItem[],
): ChapterProgressStep[] {
  const findArtifact = (predicate: (artifact: WorkflowArtifactTimelineItem) => boolean) => artifacts.find(predicate);
  const step = (
    key: string,
    label: string,
    artifact: WorkflowArtifactTimelineItem | undefined,
    fallbackDone = false,
    emptySummary = '暂无对应产物。',
  ): ChapterProgressStep => {
    const status = progressStatusFromArtifact(artifact, fallbackDone);
    return {
      key,
      label,
      status,
      caption: progressCaption(status),
      artifactId: artifact?.id,
      summary: artifact?.summary || emptySummary,
    };
  };

  const planArtifact = findArtifact((artifact) => artifact.kind === 'chapter_brief');
  const contextArtifact = findArtifact((artifact) => artifact.kind === 'context_package');
  const draftArtifact = findArtifact((artifact) =>
    artifact.kind === 'draft_artifact' ||
    artifact.kind === 'chapter_artifact_summary' && !!artifact.preview?.trim());
  const gateArtifact = findArtifact((artifact) => artifact.kind === 'gate_report');
  const qualityArtifact = findArtifact((artifact) => artifact.kind === 'quality_review');
  const libraryArtifact = findArtifact((artifact) => artifact.kind === 'library_chapter' || artifact.isFinal && artifact.surface === '小说书城');

  return [
    step('chapter_plan', '章节规划', planArtifact, !!chapter && ['planned', 'candidates_ready', 'candidate_selected', 'context_ready', 'draft_generated', 'validated', 'committed'].includes(chapter.writingStatus)),
    step('context', '上下文包', contextArtifact, !!chapter && ['context_ready', 'draft_generated', 'validated', 'committed'].includes(chapter.writingStatus)),
    step('draft', '正文草稿', draftArtifact, !!chapter?.hasGeneratedContent),
    step('gate', '结构门禁', gateArtifact, !!chapter && ['validated', 'committed'].includes(chapter.writingStatus)),
    step('quality', '质量评审', qualityArtifact, !!chapter && (chapter.artifactStatus === 'quality_passed' || chapter.writingStatus === 'committed')),
    step('library', '书城入库', libraryArtifact, hasReadableManuscript(chapter), hasReadableManuscript(chapter) ? '已进入小说书城。' : '暂无书城正文。'),
  ];
}

function buildArtifactLedgerGroups(artifacts: WorkflowArtifactTimelineItem[]): ArtifactLedgerGroup[] {
  const groups = [
    {
      key: 'chapter_plan',
      label: '章节规划',
      surface: '工作流',
      predicate: (artifact: WorkflowArtifactTimelineItem) => artifact.kind === 'chapter_brief',
    },
    {
      key: 'context',
      label: '上下文包',
      surface: '工作流',
      predicate: (artifact: WorkflowArtifactTimelineItem) => artifact.kind === 'context_package',
    },
    {
      key: 'draft',
      label: '正文草稿',
      surface: '工作流',
      predicate: (artifact: WorkflowArtifactTimelineItem) =>
        artifact.kind === 'draft_artifact' ||
        artifact.kind === 'chapter_artifact_summary' && !!artifact.preview?.trim(),
    },
    {
      key: 'gate',
      label: '结构门禁',
      surface: '工作流',
      predicate: (artifact: WorkflowArtifactTimelineItem) => artifact.kind === 'gate_report',
    },
    {
      key: 'quality',
      label: '质量评审',
      surface: '工作流',
      predicate: (artifact: WorkflowArtifactTimelineItem) => artifact.kind === 'quality_review',
    },
    {
      key: 'library',
      label: '书城入库',
      surface: '书城',
      predicate: (artifact: WorkflowArtifactTimelineItem) => artifact.kind === 'library_chapter' || artifact.isFinal && artifact.surface === '小说书城',
    },
    {
      key: 'other',
      label: '其他产物',
      surface: '工作流',
      predicate: () => true,
    },
  ];

  const usedIds = new Set<string>();
  return groups
    .map((group) => {
      const matched = artifacts.filter((artifact) => !usedIds.has(artifact.id) && group.predicate(artifact));
      matched.forEach((artifact) => usedIds.add(artifact.id));
      return {
        key: group.key,
        label: group.label,
        surface: group.surface,
        artifacts: matched,
      };
    })
    .filter((group) => group.artifacts.length > 0);
}

export default function WorkflowPage() {
  const navigate = useNavigate();
  const { projectId: routeProjectId } = useParams();
  const queryClient = useQueryClient();
  const storeProjectId = useProjectStore((s) => s.currentProjectId);
  const setCurrentProject = useProjectStore((s) => s.setCurrentProject);
  const setCurrentProjectId = useProjectStore((s) => s.setCurrentProjectId);
  const [selectedArtifactId, setSelectedArtifactId] = useState<string | null>(null);
  const [selectedChapterId, setSelectedChapterId] = useState<string | null>(null);
  const [chapterDetailTab, setChapterDetailTab] = useState<ChapterDetailTab>('manuscript');
  const [artifactFilter, setArtifactFilter] = useState<ArtifactFilter>('all');
  const [agentNote, setAgentNote] = useState('');
  const [workbenchNotice, setWorkbenchNotice] = useState('');
  const [agentLastReply, setAgentLastReply] = useState('');
  const currentProjectId = routeProjectId ?? storeProjectId;
  const isDetailView = !!routeProjectId;

  const { data: agentSessions } = useQuery({
    queryKey: ['agentSessions'],
    queryFn: listAgentSessions,
    refetchInterval: 8000,
  });

  const { data: workspaceData, isLoading: workspaceLoading, isError } = useQuery({
    queryKey: ['workspace'],
    queryFn: getWorkspace,
    staleTime: 30_000,
    retry: 3,
  });

  const books = workspaceData?.projects ?? [];
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

  const { data: workflow, isLoading: workflowLoading } = useQuery({
    queryKey: ['projectWorkflow', currentProjectId],
    queryFn: () => getProjectWorkflow(currentProjectId!),
    enabled: isDetailView && !!currentProjectId,
    refetchInterval: 5000,
  });

  const selectedBook = workflow?.project
    ?? books.find((book) => book.projectId === currentProjectId)
    ?? (isDetailView ? null : activeBook);
  const volumes = workflow?.library?.volumes ?? [];
  const chapters = volumes.flatMap((volume) => volume.chapters);
  const selectedPlan = workflow?.missionPlans?.[0]
    ?? workflow?.sessions?.[0]?.missionPlan
    ?? null;
  const timeline = workflow?.artifactTimeline ?? [];
  const filteredTimeline = timeline
    .filter((artifact) => artifact.isUserVisible)
    .filter((artifact) => artifactSurfaceFilter(artifact, artifactFilter));
  const selectedChapter = chapters.find((chapter) => chapter.chapterId === selectedChapterId)
    ?? chapters.find(isLibraryChapter)
    ?? firstRealChapter(volumes)
    ?? chapters[0]
    ?? null;
  const chapterTitleById = useMemo(
    () => new Map(chapters.map((chapter) => [chapter.chapterId, chapter.title])),
    [chapters],
  );
  const selectedChapterArtifacts = selectedChapter
    ? filteredTimeline
      .filter((artifact) => artifact.chapterId === selectedChapter.chapterId)
      .sort((a, b) => artifactPriority(b) - artifactPriority(a) || Date.parse(b.updatedAt || '0') - Date.parse(a.updatedAt || '0'))
    : [];
  const chapterProgressSteps = buildChapterProgressSteps(selectedChapter, selectedChapterArtifacts);
  const chapterArtifactLedgerGroups = buildArtifactLedgerGroups(selectedChapterArtifacts);
  const globalArtifacts = filteredTimeline
    .filter((artifact) => !artifact.chapterId)
    .slice(0, 8);
  const selectedArtifact = filteredTimeline.find((artifact) => artifact.id === selectedArtifactId)
    ?? selectedChapterArtifacts[0]
    ?? filteredTimeline[0]
    ?? null;
  const selectedVolume = volumes.find((volume) => volume.volumeId === selectedChapter?.volumeId) ?? volumes[0] ?? null;
  const selectedChapterWarnings = chapterQualityWarnings(selectedChapter);
  const generatedCount = selectedBook?.generatedChapterCount ?? 0;
  const plannedCount = selectedBook?.plannedChapterCount ?? 0;
  const committedCount = chapters.filter(isLibraryChapter).length;
  const activeWorkflowSessionId = workflow?.activeSessionId
    || workflow?.sessions?.[0]?.sessionId
    || agentSessions?.find((session) => session.activeProjectId === currentProjectId)?.sessionId
    || '';
  const selectedProjectTaskQueue: AgentScheduledTask[] = workflow?.schedulerTasks ?? selectedPlan?.schedulerState?.tasks ?? [];
  const blockedArtifacts = timeline
    .filter((artifact) => artifact.isUserVisible && artifactClass(artifact) === 'blocked')
    .slice(0, 5);
  const hasWorkflowSession = !!activeWorkflowSessionId;
  const hasCurrentArtifacts = timeline.some((artifact) => artifact.kind !== 'scheduled_task')
    || chapters.some((chapter) => chapter.hasGeneratedContent || chapter.visibleInWorkflow);
  const canRemoveEmptyProject = !!selectedBook
    && !selectedBook.isActive
    && generatedCount === 0
    && plannedCount === 0
    && !hasCurrentArtifacts
    && (workflow?.isEmptyProject ?? true);
  const diagnosticReasons: string[] = [
    ...(workflow?.suspectReasons ?? []),
    ...(workflow?.staleMissionWarnings ?? []),
  ];
  const diagnosticTaskCount = workflow?.diagnosticTasks?.length ?? 0;

  useEffect(() => {
    if (!routeProjectId && !currentProjectId && fallbackProjectId) {
      const book = books.find((item) => item.projectId === fallbackProjectId);
      if (book) {
        setCurrentProject(bookToProjectInfo(book));
      } else {
        setCurrentProjectId(fallbackProjectId);
      }
    }
  }, [books, fallbackProjectId, currentProjectId, routeProjectId, setCurrentProject, setCurrentProjectId]);

  useEffect(() => {
    if (!routeProjectId) return;
    const book = books.find((item) => item.projectId === routeProjectId);
    if (book) {
      setCurrentProject(bookToProjectInfo(book));
    } else {
      setCurrentProjectId(routeProjectId);
    }
  }, [books, routeProjectId, setCurrentProject, setCurrentProjectId]);

  useEffect(() => {
    setSelectedArtifactId(null);
    setSelectedChapterId(null);
    setChapterDetailTab('manuscript');
    setArtifactFilter('all');
  }, [currentProjectId]);

  const selectProject = (projectId: string) => {
    const book = books.find((item) => item.projectId === projectId);
    if (book) {
      setCurrentProject(bookToProjectInfo(book));
    } else {
      setCurrentProjectId(projectId);
    }
    setSelectedArtifactId(null);
    setSelectedChapterId(null);
    navigate(`/workflow/${encodeURIComponent(projectId)}`);
  };

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
    queryClient.invalidateQueries({ queryKey: ['projectWorkflow', currentProjectId] });
  };

  const refreshAsync = async () => {
    await queryClient.invalidateQueries({ queryKey: ['agentSessions'] });
    await queryClient.invalidateQueries({ queryKey: ['projectWorkflow', currentProjectId] });
  };

  const agentActionMutation = useMutation({
    mutationFn: (message: string) => sendChat({ sessionId: activeWorkflowSessionId, message }),
    onSuccess: async (response) => {
      setAgentNote('');
      setWorkbenchNotice(response.reply ? 'Agent 已回复。' : '已交给 Agent。');
      setAgentLastReply(response.reply || '');
      await refreshAsync();
    },
    onError: (error) => {
      setWorkbenchNotice(error instanceof Error ? error.message : 'Agent 操作没有完成，请稍后重试。');
    },
  });

  const removeProjectMutation = useMutation({
    mutationFn: (projectId: string) => deleteNovelProject(projectId),
    onSuccess: async () => {
      setCurrentProject(null);
      setSelectedChapterId(null);
      setSelectedArtifactId(null);
      setWorkbenchNotice('空项目已移除。');
      await refreshAsync();
    },
  });

  const submitAgentNote = async () => {
    if (!currentProjectId || !activeWorkflowSessionId) {
      setWorkbenchNotice('未绑定可操作会话，无法发送给 Agent。');
      return;
    }
    if (!agentNote.trim()) {
      setWorkbenchNotice('请先写下要补充、调整或询问的内容。');
      return;
    }
    setWorkbenchNotice('正在发送给 Agent...');
    setAgentLastReply('');
    const message = buildAgentContextMessage({
      projectId: currentProjectId,
      sessionId: activeWorkflowSessionId,
      selectedArtifact,
      selectedChapter,
      userFeedback: agentNote.trim(),
    });
    await agentActionMutation.mutateAsync(message);
  };

  const removeEmptyProject = async () => {
    if (!currentProjectId || !selectedBook) return;
    if (selectedBook.isActive) {
      window.alert('当前项目是 active，删除会切换工作区。请先切换 active project，或后续使用安全删除接口。');
      return;
    }
    if (!canRemoveEmptyProject) return;
    const ok = window.confirm(`移除空项目「${selectedBook.title}」？该操作只用于清理没有章节和草稿的项目。`);
    if (!ok) return;
    await removeProjectMutation.mutateAsync(currentProjectId);
  };

  if (!isDetailView) {
    const activeProjectCount = books.filter((book) => book.isActive).length;
    const projectWithOutputCount = books.filter((book) => book.generatedChapterCount > 0 || book.plannedChapterCount > 0).length;
    const totalPlannedCount = books.reduce((sum, book) => sum + book.plannedChapterCount, 0);
    const totalGeneratedCount = books.reduce((sum, book) => sum + book.generatedChapterCount, 0);

    return (
      <>
        <Topbar
          title="创作工作流"
          actions={
            <button className="ghost-button" onClick={refresh}>
              刷新
            </button>
          }
        />

        <div className="ops-workspace workflow-overview">
          <section className="ops-header workflow-overview-hero">
            <div>
              <span className="ops-kicker">Workflow Overview</span>
              <h2>创作工作流总览</h2>
              <p>这里先看所有小说任务的推进状态。进入单个工作流后，再查看卷、章、过程产物、书城入库和 Agent 补充操作。</p>
            </div>
            <div className="ops-status-grid">
              <strong>{books.length}<small>项目</small></strong>
              <strong>{projectWithOutputCount}<small>有产物</small></strong>
              <strong>{totalPlannedCount}<small>章节位</small></strong>
              <strong>{totalGeneratedCount}<small>过程章</small></strong>
            </div>
            <div className="ops-live-card">
              <span>当前工作台</span>
              <strong>{activeProjectCount > 0 ? `${activeProjectCount} 个 active 项目` : '等待选择项目'}</strong>
              <small>{agentSessions?.length ?? 0} 个 Agent 会话可关联查看</small>
            </div>
          </section>

          <section className="workflow-overview-board">
            <div className="workflow-overview-toolbar">
              <div>
                <span className="ops-kicker">Workflows</span>
                <h3>项目工作流</h3>
              </div>
              <small>{workspaceLoading ? '正在加载项目...' : `共 ${books.length + missionOnlyCards.length} 个工作流入口`}</small>
            </div>

            {workspaceLoading && <div className="workflow-loading">加载项目中...</div>}
            {isError && <div className="workflow-error">加载失败，请刷新重试</div>}
            {!workspaceLoading && !isError && books.length === 0 && missionOnlyCards.length === 0 && (
              <div className="workflow-empty">暂无活跃项目</div>
            )}

            <div className="workflow-overview-grid">
              {books.map((book) => {
                const relatedSessions = (agentSessions ?? []).filter((session) => session.activeProjectId === book.projectId);
                const score = bookActivityScore(book, agentSessions ?? []);
                return (
                  <button
                    key={book.projectId}
                    type="button"
                    className={`workflow-overview-card ${book.isActive ? 'active' : ''} ${book.projectId === storeProjectId ? 'selected' : ''}`}
                    onClick={() => selectProject(book.projectId)}
                  >
                    <div className="workflow-card-head">
                      <span>{book.status || 'Drafting'} · {projectShortId(book.projectId)}</span>
                      <em>{book.isActive ? 'Active' : score > 0 ? '有活动' : '空项目'}</em>
                    </div>
                    <strong>{book.title}</strong>
                    <p>{book.coreHook || book.readerPromise || book.selectedChapter?.summary || '等待 Agent 补齐故事地基和章节计划。'}</p>
                    <i><b style={{ width: `${progressPercent(book)}%` }} /></i>
                    <div className="workflow-card-metrics">
                      <span><b>{book.volumeCount}</b><small>卷</small></span>
                      <span><b>{book.plannedChapterCount}</b><small>章节位</small></span>
                      <span><b>{book.generatedChapterCount}</b><small>过程章</small></span>
                      <span><b>{relatedSessions.length}</b><small>会话</small></span>
                    </div>
                    <small className="workflow-card-time">更新 {formatTime(book.updatedAt)}</small>
                  </button>
                );
              })}

              {missionOnlyCards.map(({ session, plan, projectId }) => (
                <button
                  key={session.sessionId}
                  type="button"
                  className={`workflow-overview-card mission ${projectId === storeProjectId ? 'selected' : ''}`}
                  onClick={() => selectProject(projectId)}
                >
                  <div className="workflow-card-head">
                    <span>{missionStageLabel(plan)}</span>
                    <em>任务会话</em>
                  </div>
                  <strong>{plan.projectTitle || session.title || 'Agent 小说任务'}</strong>
                  <p>{plan.currentNovelGoal || plan.overallGoal || session.phase}</p>
                  <div className="workflow-card-metrics">
                    <span><b>{missionChapters(plan).length}</b><small>章任务</small></span>
                    <span><b>{plan.schedulerState?.tasks.length ?? 0}</b><small>调度</small></span>
                  </div>
                  <small className="workflow-card-time">更新 {formatTime(session.updatedAt)}</small>
                </button>
              ))}
            </div>
          </section>
        </div>
      </>
    );
  }

  return (
    <>
      <Topbar
        title="创作工作流"
        actions={
          <>
            <button className="ghost-button" onClick={() => navigate('/workflow')}>
              返回总览
            </button>
            <button className="ghost-button" onClick={refresh}>
              刷新
            </button>
          </>
        }
      />

      <div className="ops-workspace workflow-detail chapter-detail-page">
        <section className="workflow-project-strip">
          <button className="ghost-button compact" type="button" onClick={() => navigate('/workflow')}>
            返回工作流总览
          </button>
          <div className="workflow-project-title">
            <span className="ops-kicker">Chapter Workflow</span>
            <h2>{selectedBook?.title || '未选择小说任务'}</h2>
            <p>{selectedBook?.coreHook || selectedBook?.readerPromise || '按卷和章节查看真实成稿、门禁、质量评审与过程产物。'}</p>
          </div>
          <div className="workflow-project-metrics">
            <strong>{committedCount}<small>入库章</small></strong>
            <strong>{plannedCount}<small>章节位</small></strong>
            <strong>{selectedBook?.generatedChapterCount ?? generatedCount}<small>过程章</small></strong>
            <strong>{selectedBook?.status || 'Drafting'}<small>状态</small></strong>
          </div>
        </section>

        <section className="workflow-chapter-board">
          <aside className="workflow-chapter-index">
            <div className="ops-panel-head">
              <span>章节目录</span>
              <strong>{chapters.length}</strong>
            </div>
            {workflowLoading ? (
              <div className="empty compact">正在载入项目工作流...</div>
            ) : volumes.length === 0 ? (
              <div className="empty compact">这个项目还没有真实卷或章节。</div>
            ) : volumes.map((volume) => (
              <section key={volume.volumeId} className="ops-volume-group">
                <div className="ops-volume-title">
                  <strong>{volume.title}</strong>
                  <span>{volume.chapters.length}/{volume.expectedChapterCount || volume.chapters.length} 章</span>
                </div>
                {volume.chapters.length === 0 ? (
                  <div className="ops-chapter-empty">还没有章节实体</div>
                ) : volume.chapters.map((chapter) => {
                  const warnings = chapterQualityWarnings(chapter);
                  return (
                    <button
                      key={chapter.chapterId}
                      className={`ops-chapter-row ${selectedChapter?.chapterId === chapter.chapterId ? 'active' : ''}`}
                      onClick={() => {
                        setSelectedChapterId(chapter.chapterId);
                        setSelectedArtifactId(null);
                        setChapterDetailTab(hasReadableManuscript(chapter) ? 'manuscript' : 'workflow');
                      }}
                    >
                      <i className={chapterStatusClass(chapter)}>{chapter.beatIndex || '-'}</i>
                      <span>
                        <strong>{chapter.title}</strong>
                        <small>{chapterStatusLabel(chapter)} · {chapter.wordCount || 0} 字{warnings.length > 0 ? ' · 有问题' : ''}</small>
                      </span>
                    </button>
                  );
                })}
              </section>
            ))}
          </aside>

          <main className="workflow-chapter-main">
            <div className="workflow-chapter-head">
              <div>
                <span>{selectedVolume?.title || '章节'}</span>
                <h3>{selectedChapter?.title || '等待真实章节'}</h3>
                <p>{selectedChapter?.summary || selectedChapter?.goal || selectedArtifact?.summary || '这个章节还没有可展示的正文或工作流摘要。'}</p>
              </div>
              <strong className={`ops-state ${selectedChapter ? chapterStatusClass(selectedChapter) : artifactClass(selectedArtifact)}`}>
                {selectedChapter ? chapterStatusLabel(selectedChapter) : '未开始'}
              </strong>
            </div>

            <div className="workflow-chapter-facts">
              <span><b>{selectedChapter?.wordCount || 0}</b><small>正文字数</small></span>
              <span><b>{selectedChapterArtifacts.length}</b><small>本章产物</small></span>
              <span><b>{selectedChapter?.qualityScore || 0}</b><small>质量分</small></span>
              <span><b>{selectedChapter?.ragRecallCount || 0}</b><small>召回</small></span>
            </div>

            {selectedChapterWarnings.length > 0 && (
              <div className="workflow-warning-list">
                {selectedChapterWarnings.map((warning) => <p key={warning}>{warning}</p>)}
              </div>
            )}

            <div className="workflow-detail-tabs">
              {([
                ['manuscript', '正文成稿'],
                ['workflow', '工作流步骤'],
                ['issues', '问题/门禁'],
                ['artifacts', '产物日志'],
              ] as const).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  className={chapterDetailTab === key ? 'selected' : ''}
                  onClick={() => setChapterDetailTab(key)}
                >
                  {label}
                </button>
              ))}
            </div>

            {chapterDetailTab === 'manuscript' && (
              <section className="workflow-manuscript-panel">
                {selectedChapter && hasReadableManuscript(selectedChapter) ? (
                  <>
                    <div className="ops-card-title">
                      <div>
                        <span>小说书城 · 最终正文</span>
                        <h4>{selectedChapter.title}</h4>
                      </div>
                      <em>{formatTime(selectedChapter.updatedAt)}</em>
                    </div>
                    <article className="workflow-manuscript-reader">
                      {manuscriptPreview(selectedChapter.content).map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                    </article>
                  </>
                ) : (
                  <div className="empty-panel">
                    <p>这一章还没有书城正文。请在“工作流步骤”里查看草稿、门禁或候选产物。</p>
                  </div>
                )}
              </section>
            )}

            {chapterDetailTab === 'workflow' && (
              <section className="workflow-tab-panel">
                <div className="workflow-chapter-progress" aria-label="本章流程概览">
                  <div className="workflow-chapter-progress-head">
                    <span>本章流程概览</span>
                    <em>{selectedChapter ? chapterStatusLabel(selectedChapter) : '未选择章节'}</em>
                  </div>
                  <div className="workflow-chapter-progress-track">
                    {chapterProgressSteps.map((step, index) => (
                      <button
                        key={step.key}
                        type="button"
                        className={`workflow-progress-step ${step.status} ${selectedArtifact?.id === step.artifactId ? 'selected' : ''}`}
                        disabled={!step.artifactId}
                        title={step.summary}
                        onClick={() => {
                          if (step.artifactId) setSelectedArtifactId(step.artifactId);
                        }}
                      >
                        <i>{index + 1}</i>
                        <span>{step.label}</span>
                        <em>{step.caption}</em>
                      </button>
                    ))}
                  </div>
                </div>
                <div className="workflow-step-layout">
                  <div className="workflow-step-grid" aria-label="本章工作流步骤">
                    {selectedChapterArtifacts.length === 0 ? (
                      <div className="empty compact">这一章还没有真实过程产物。</div>
                    ) : selectedChapterArtifacts.map((artifact) => (
                      <button
                        key={artifact.id}
                        type="button"
                        className={`ops-artifact-card ${selectedArtifact?.id === artifact.id ? 'selected' : ''} ${artifactClass(artifact)}`}
                        onClick={() => setSelectedArtifactId(artifact.id)}
                      >
                        <span>{artifact.surface} · {artifact.label}</span>
                        <strong>{artifactDisplayTitle(artifact, chapterTitleById)}</strong>
                        <small>{artifactSummaryText(artifact, chapterTitleById)}</small>
                        <em>{formatTime(artifact.updatedAt)}</em>
                      </button>
                    ))}
                  </div>
                  {selectedArtifact && selectedArtifact.chapterId === selectedChapter?.chapterId ? (
                    <section className="ops-artifact-detail">
                      <div className="ops-card-title">
                        <div>
                          <span>{selectedArtifact.surface} · {selectedArtifact.label}</span>
                          <h4>{artifactDisplayTitle(selectedArtifact, chapterTitleById)}</h4>
                        </div>
                        <em>{formatTime(selectedArtifact.updatedAt)}</em>
                      </div>
                      <p>{selectedArtifact.summary || '暂无摘要。'}</p>
                      {selectedArtifact.preview && selectedArtifact.kind !== 'library_chapter' && (
                        <div className="ops-draft-preview">
                          {visiblePreview(selectedArtifact.preview)
                            .split(/\n{2,}| \/ /)
                            .filter(Boolean)
                            .slice(0, 8)
                            .map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                        </div>
                      )}
                      <div className="ops-artifact-meta">
                        <span>{selectedArtifact.kind}</span>
                        {selectedArtifact.runId && <span>run {projectShortId(selectedArtifact.runId)}</span>}
                        <span>{selectedArtifact.isFinal ? '最终产物' : '过程产物'}</span>
                      </div>
                    </section>
                  ) : (
                    <section className="ops-artifact-detail empty-detail">
                      <div className="ops-card-title">
                        <div>
                          <span>工作流步骤</span>
                          <h4>选择一个步骤查看详情</h4>
                        </div>
                      </div>
                      <p>本区域显示草稿、门禁、质量评审、提交入库等过程产物的摘要和预览。</p>
                    </section>
                  )}
                </div>
              </section>
            )}

            {chapterDetailTab === 'issues' && (
              <section className="workflow-tab-panel">
                <div className="workflow-issue-grid">
                  <article>
                    <span>结构门禁</span>
                    <strong>{selectedChapter?.gateStatus || '暂无'}</strong>
                    {(selectedChapter?.gateIssues.length ?? 0) === 0 ? (
                      <p>没有结构门禁阻塞。</p>
                    ) : selectedChapter?.gateIssues.map((issue) => <p key={issue}>{issue}</p>)}
                  </article>
                  <article>
                    <span>修复建议</span>
                    <strong>{selectedChapter?.repairHints.length ?? 0}</strong>
                    {(selectedChapter?.repairHints.length ?? 0) === 0 ? (
                      <p>暂无修复建议。</p>
                    ) : selectedChapter?.repairHints.map((hint) => <p key={hint}>{hint}</p>)}
                  </article>
                  <article>
                    <span>质量评审</span>
                    <strong>{selectedChapter?.qualityScore || 0}</strong>
                    {(selectedChapter?.reviewChecks.length ?? 0) === 0 && (selectedChapter?.nextSuggestions.length ?? 0) === 0 ? (
                      <p>暂无质量评审细项。</p>
                    ) : [...(selectedChapter?.reviewChecks ?? []), ...(selectedChapter?.nextSuggestions ?? [])].map((item) => <p key={item}>{item}</p>)}
                  </article>
                  <article>
                    <span>上下文/依赖</span>
                    <strong>{(selectedChapter?.contextWarnings.length ?? 0) + (selectedChapter?.dependencyWarnings.length ?? 0)}</strong>
                    {(selectedChapter?.contextWarnings.length ?? 0) + (selectedChapter?.dependencyWarnings.length ?? 0) === 0 ? (
                      <p>暂无上下文或依赖警告。</p>
                    ) : [...(selectedChapter?.contextWarnings ?? []), ...(selectedChapter?.dependencyWarnings ?? [])].map((item) => <p key={item}>{item}</p>)}
                  </article>
                </div>
              </section>
            )}

            {chapterDetailTab === 'artifacts' && (
              <section className="workflow-tab-panel">
                <div className="workflow-artifact-toolbar">
                  <div>
                    <span>当前章节产物</span>
                    <strong>{chapterArtifactLedgerGroups.length} 组 · {selectedChapterArtifacts.length} 条</strong>
                  </div>
                  <div className="ops-filter-tabs">
                    {([
                      ['all', '全部'],
                      ['workflow', '工作流'],
                      ['library', '书城'],
                    ] as const).map(([key, label]) => (
                      <button
                        key={key}
                        type="button"
                        className={artifactFilter === key ? 'selected' : ''}
                        onClick={() => {
                          setArtifactFilter(key);
                          setSelectedArtifactId(null);
                        }}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="workflow-artifact-board">
                  <div className="workflow-artifact-ledger" aria-label="章节产物台账">
                    {chapterArtifactLedgerGroups.length === 0 ? (
                      <div className="empty compact">当前筛选下这一章没有真实产物。</div>
                    ) : chapterArtifactLedgerGroups.map((group) => {
                      const [currentArtifact, ...historyArtifacts] = group.artifacts;
                      return (
                        <section key={group.key} className="workflow-artifact-group">
                          <button
                            type="button"
                            className={`workflow-artifact-current-row ${selectedArtifact?.id === currentArtifact.id ? 'selected' : ''} ${artifactClass(currentArtifact)}`}
                            onClick={() => setSelectedArtifactId(currentArtifact.id)}
                          >
                            <span className="workflow-artifact-kind">{group.surface}</span>
                            <div className="workflow-artifact-copy">
                              <strong>{group.label}</strong>
                              <em>{artifactDisplayTitle(currentArtifact, chapterTitleById)}</em>
                              <small>{artifactSummaryText(currentArtifact, chapterTitleById)}</small>
                            </div>
                            <span className="workflow-artifact-meta-line">
                              <b>{group.artifacts.length} 条</b>
                              <em>{formatTime(currentArtifact.updatedAt)}</em>
                            </span>
                          </button>
                          {historyArtifacts.length > 0 && (
                            <details className="workflow-artifact-history">
                              <summary>历史版本 {historyArtifacts.length}</summary>
                              <div className="workflow-artifact-history-list">
                                {historyArtifacts.map((artifact) => (
                                  <button
                                    key={artifact.id}
                                    type="button"
                                    className={`workflow-artifact-history-row ${selectedArtifact?.id === artifact.id ? 'selected' : ''}`}
                                    onClick={() => setSelectedArtifactId(artifact.id)}
                                  >
                                    <span>{artifact.label}</span>
                                    <strong>{artifactSummaryText(artifact, chapterTitleById)}</strong>
                                    <em>{formatTime(artifact.updatedAt)}</em>
                                  </button>
                                ))}
                              </div>
                            </details>
                          )}
                        </section>
                      );
                    })}
                    {globalArtifacts.length > 0 && (
                      <details className="workflow-global-artifacts">
                        <summary>
                          <span>全局产物</span>
                          <strong>{globalArtifacts.length}</strong>
                        </summary>
                        <div className="workflow-global-artifact-list">
                          {globalArtifacts.map((artifact) => (
                            <button
                              key={artifact.id}
                              type="button"
                              className={`workflow-global-artifact-row ${selectedArtifact?.id === artifact.id ? 'selected' : ''} ${artifactClass(artifact)}`}
                              onClick={() => setSelectedArtifactId(artifact.id)}
                            >
                              <span>{artifact.surface}</span>
                              <strong>{artifactDisplayTitle(artifact, chapterTitleById)}</strong>
                              <small>{artifactSummaryText(artifact, chapterTitleById)}</small>
                            </button>
                          ))}
                        </div>
                      </details>
                    )}
                  </div>
                  {selectedArtifact ? (
                    <section className="ops-artifact-detail workflow-artifact-inspector">
                      <div className="ops-card-title">
                        <div>
                          <span>{selectedArtifact.surface} · {selectedArtifact.label}</span>
                          <h4>{artifactDisplayTitle(selectedArtifact, chapterTitleById)}</h4>
                        </div>
                        <em>{formatTime(selectedArtifact.updatedAt)}</em>
                      </div>
                      <p>{selectedArtifact.summary || '暂无摘要。'}</p>
                      {selectedArtifact.preview && selectedArtifact.kind !== 'library_chapter' && (
                        <div className="ops-draft-preview">
                          {visiblePreview(selectedArtifact.preview)
                            .split(/\n{2,}| \/ /)
                            .filter(Boolean)
                            .slice(0, 8)
                            .map((paragraph, index) => <p key={index}>{paragraph}</p>)}
                        </div>
                      )}
                      <div className="ops-artifact-meta">
                        <span>{selectedArtifact.kind}</span>
                        {selectedArtifact.runId && <span>run {projectShortId(selectedArtifact.runId)}</span>}
                        <span>{selectedArtifact.isFinal ? '最终产物' : '过程产物'}</span>
                      </div>
                    </section>
                  ) : (
                    <section className="ops-artifact-detail workflow-artifact-inspector empty-detail">
                      <div className="ops-card-title">
                        <div>
                          <span>产物日志</span>
                          <h4>选择左侧产物查看详情</h4>
                        </div>
                      </div>
                      <p>左侧只展示每组最新产物和折叠历史，详情、预览和元数据会在这里展开。</p>
                    </section>
                  )}
                </div>
              </section>
            )}
          </main>

          <details className="ops-side-panel workflow-agent-dock">
            <summary className="workflow-agent-dock-summary">
              <span>Agent 工作台</span>
              <strong>补充要求 / 调度 / 阻塞 / 维护</strong>
            </summary>
            <section className="ops-side-card ops-action-card">
              <div className="ops-inspector-title">
                <span>Agent</span>
                <strong>补充要求</strong>
              </div>
              {!hasWorkflowSession ? (
                <p className="ops-muted-line">未绑定可操作会话</p>
              ) : (
                <>
                  <textarea
                    value={agentNote}
                    onChange={(event) => setAgentNote(event.target.value)}
                    placeholder="写下你想调整、继续或询问的内容。"
                    rows={5}
                  />
                  <button
                    className="ink-button"
                    type="button"
                    onClick={() => void submitAgentNote()}
                    disabled={agentActionMutation.isPending || !agentNote.trim()}
                  >
                    {agentActionMutation.isPending ? '发送中...' : '发给 Agent'}
                  </button>
                  <small>
                    {activeWorkflowSessionId ? `会话 ${projectShortId(activeWorkflowSessionId)}` : '未绑定会话'}
                    {' · '}
                    当前产物 {selectedArtifact ? selectedArtifact.label : '未选中'}
                  </small>
                </>
              )}
              {workbenchNotice && <p className="ops-notice">{workbenchNotice}</p>}
              {agentLastReply && (
                <div className="ops-agent-reply">
                  <span>Agent 回复</span>
                  <p>{agentLastReply}</p>
                </div>
              )}
              {agentActionMutation.isError && <p className="ops-error">Agent 操作没有完成，请稍后重试。</p>}
            </section>

            <section className="ops-side-card ops-compact-card">
              <div className="ops-inspector-title">
                <span>当前调度</span>
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
                <span>阻塞关注</span>
                <strong>{blockedArtifacts.length}</strong>
              </div>
              {blockedArtifacts.length === 0 ? (
                <p className="ops-muted-line">暂无阻塞产物</p>
              ) : blockedArtifacts.map((artifact) => (
                <button
                  key={artifact.id}
                  type="button"
                  className="ops-task-row blocked"
                  onClick={() => setSelectedArtifactId(artifact.id)}
                >
                  <strong>{artifact.title}</strong>
                  <small>{artifact.label} · {artifact.status}</small>
                  <em>{artifact.summary}</em>
                </button>
              ))}
            </section>

            <details className="ops-side-card ops-detail-fold" open={diagnosticReasons.length + diagnosticTaskCount > 0}>
              <summary>
                <span>诊断</span>
                <strong>{diagnosticReasons.length + diagnosticTaskCount}</strong>
              </summary>
              {diagnosticReasons.length + diagnosticTaskCount === 0 ? (
                <p className="ops-muted-line">无诊断信息</p>
              ) : (
                diagnosticReasons.slice(0, 5).map((reason) => (
                  <p key={reason}>{reason}</p>
                ))
              )}
            </details>

            <details className="ops-side-card ops-detail-fold">
              <summary>
                <span>维护</span>
                <strong>项目</strong>
              </summary>
              <p>只开放非 active 且没有真实产物的空项目移除。</p>
              <div className="ops-maintenance-actions">
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
          </details>
        </section>
      </div>
    </>
  );
}
