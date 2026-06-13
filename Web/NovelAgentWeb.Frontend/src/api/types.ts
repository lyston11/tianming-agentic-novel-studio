// ============================================================
// Enums
// ============================================================

export type NovelAgentIntent =
  | 'Unknown'
  | 'CreateStoryFoundation'
  | 'RefineStoryFoundation'
  | 'PlanVolumeArc'
  | 'PlanChapter'
  | 'GenerateChapter'
  | 'RewriteChapter'
  | 'ValidateContinuity'
  | 'ExploreWorldbuilding';

export type NovelAgentRunStatus =
  | 'Draft'
  | 'Planning'
  | 'Retrieving'
  | 'AwaitingConfirmation'
  | 'Executing'
  | 'Validating'
  | 'Repairing'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

export type NovelAgentStepStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Skipped'
  | 'WaitingUser';

export type NovelToolRiskLevel = 'Low' | 'Medium' | 'High' | 'Critical';

export type CanonLedgerEntryType =
  | 'WorldRule'
  | 'CharacterRule'
  | 'FactionRule'
  | 'LocationRule'
  | 'PlotRule'
  | 'Foreshadowing'
  | 'Theme'
  | 'Constraint';

export type CanonLedgerEntryStatus =
  | 'Draft'
  | 'Proposed'
  | 'Canon'
  | 'Deprecated'
  | 'Conflict'
  | 'Rejected';

export type ForeshadowLedgerEntryType =
  | 'Plot'
  | 'WorldRule'
  | 'CharacterSecret'
  | 'Relationship'
  | 'Object'
  | 'Threat'
  | 'Theme'
  | 'Other';

export type ForeshadowLedgerStatus =
  | 'Draft'
  | 'Proposed'
  | 'Planned'
  | 'Setup'
  | 'Reinforced'
  | 'Due'
  | 'PaidOff'
  | 'Abandoned'
  | 'Conflict';

export type CharacterLedgerEntryType =
  | 'Goal'
  | 'Secret'
  | 'Relationship'
  | 'AbilityCost'
  | 'Psychology'
  | 'Belief'
  | 'Identity'
  | 'Role'
  | 'Death'
  | 'Other';

export type CharacterLedgerStatus =
  | 'Draft'
  | 'Proposed'
  | 'Active'
  | 'GoalUpdated'
  | 'SecretSeeded'
  | 'SecretRevealed'
  | 'RelationshipChanged'
  | 'AbilityChanged'
  | 'PsychologyShifted'
  | 'BeliefChanged'
  | 'IdentityChanged'
  | 'RoleChanged'
  | 'Dead'
  | 'Conflict'
  | 'Rejected';

export type VolumeArcStatus = 'Draft' | 'Proposed' | 'Canon' | 'Deprecated';

export type NovelAgentReviewCheckStatus = 'Unknown' | 'Pass' | 'Warning' | 'Fail';

export type CreativeKnowledgeCategory =
  | 'GenrePrinciple'
  | 'TropePattern'
  | 'AntiTropeStrategy'
  | 'ProjectUsedPattern'
  | 'ReaderPromise'
  | 'ThemeDepth'
  | 'EmotionArc'
  | 'RelationshipDynamic';

// ============================================================
// Core Models
// ============================================================

export interface StoryCreativeConstitution {
  genre: string;
  subGenre: string;
  readerPromise: string;
  coreHook: string;
  coreTheme: string;
  mainPleasure: string;
  secondaryPleasure: string;
  worldCoreRule: string;
  mainConflictEngine: string;
  protagonistEngine: string;
  noveltyPoint: string;
  depthLayer: string;
  forbiddenDirections: string;
  commercialRhythm: string;
  genreProfile: GenreDirectionProfile | null;
}

export interface GenreDirectionProfile {
  pleasureStrength: number;
  mysteryStrength: number;
  emotionStrength: number;
  worldbuildingStrength: number;
  ensembleStrength: number;
  depthStrength: number;
  paceStrength: number;
  strategy: string;
  riskWarnings: string[];
}

export interface MacroStoryConceptCandidate {
  title: string;
  coreHook: string;
  worldCoreRule: string;
  mainConflictEngine: string;
  protagonistEngine: string;
  depthLayer: string;
  noveltyScore: number;
  sustainabilityScore: number;
  typeMatchScore: number;
  risks: string[];
}

export interface NovelAgentRun {
  runId: string;
  userGoal: string;
  intent: NovelAgentIntent;
  status: NovelAgentRunStatus;
  targetChapterId: string;
  storyConstitution: StoryCreativeConstitution | null;
  macroCandidates: MacroStoryConceptCandidate[];
  chapterBrief: ChapterCreativeBrief | null;
  volumeArcPlan: VolumeArcPlan | null;
  storyState: StoryStateSnapshot | null;
  contextPackage?: ChapterContextPackageSummary | null;
  draftArtifact?: ChapterDraftArtifact | null;
  gateReport?: GenerationGateReport | null;
  dependencyImpact?: DependencyImpactReport | null;
  postGenerationReview: NovelAgentPostGenerationReview | null;
  rewriteAttempts: NovelAgentRewriteAttempt[];
  steps: NovelAgentPlanStep[];
  notes: string[];
  updatedAt: string;
}

export interface ChapterContextPackageSummary {
  chapterId: string;
  status: string;
  worldRules: string[];
  characterStates: string[];
  activeConflicts: string[];
  activeForeshadowing: string[];
  chapterBlueprints: string[];
  previousSummaries: string[];
  longDistanceRecall: string[];
  ragQueries: string[];
  warnings: string[];
  builtAt: string;
}

export interface GenerationGateReport {
  status: string;
  protocolPassed: boolean;
  changesDetected: boolean;
  factSnapshotPassed: boolean;
  blueprintPassed: boolean;
  ragPassed: boolean;
  issues: string[];
  repairHints: string[];
  validatedAt: string;
}

export interface ChapterDraftArtifact {
  artifactId: string;
  chapterId: string;
  status: string;
  draftContent: string;
  committedContent: string;
  changesJson: string;
  hasChanges: boolean;
  repairAttemptCount: number;
  generatedAt: string;
  committedAt: string | null;
}

export interface DependencyImpactReport {
  status: string;
  changedModules: string[];
  impactedModules: string[];
  impactedChapters: string[];
  summary: string;
  createdAt: string;
}

export interface NovelAgentPlanStep {
  id: string;
  name: string;
  purpose: string;
  toolName: string;
  status: NovelAgentStepStatus;
  riskLevel: NovelToolRiskLevel;
  requiresConfirmation: boolean;
  inputs: Record<string, string>;
}

export interface ChapterCreativeBrief {
  chapterId: string;
  coreIdea: string;
  conflictMove: string;
  characterChoice: string;
  costOrConsequence: string;
  foreshadowingAction: string;
  worldbuildingGap: string;
  forbiddenPatterns: string;
  knowledgeNotes: string;
  antiTropeStrategies: string;
  patternWarnings: string;
  similarContentWarnings: string;
  volumeArcNotes: string;
  volumeBeatRole: string;
  recommendedCandidateTitle: string;
  selectedCandidateTitle: string;
  candidates: PlotCandidate[];
}

export interface PlotCandidate {
  title: string;
  coreTwist: string;
  conflictMove: string;
  characterChoice: string;
  costOrConsequence: string;
  noveltyScore: number;
  consistencyScore: number;
  dramaScore: number;
  typeMatchScore: number;
  clicheRisk: number;
  totalScore: number;
  recommendationReason: string;
  knowledgeSupport: string;
  antiTropePlan: string;
  risks: string[];
}

export interface VolumeArcPlan {
  id: string;
  volumeId: string;
  title: string;
  startChapterId: string;
  endChapterId: string;
  expectedChapterCount: number;
  status: VolumeArcStatus;
  volumePromise: string;
  entryState: string;
  exitState: string;
  coreQuestion: string;
  mainConflictUpgrade: string;
  midpointReversal: string;
  climax: string;
  aftermathHook: string;
  chapterBeats: VolumeChapterBeat[];
  foreshadowingPlan: VolumeForeshadowPlan[];
  characterArcPlan: VolumeCharacterArc[];
  worldbuildingIncrements: string[];
  mustAvoid: string[];
}

export interface VolumeChapterBeat {
  index: number;
  role: string;
  goal: string;
  turn: string;
  cost: string;
}

export interface VolumeForeshadowPlan {
  name: string;
  setup: string;
  payoff: string;
  payoffBeatIndex: number;
}

export interface VolumeCharacterArc {
  characterName: string;
  startingBelief: string;
  pressure: string;
  choice: string;
  changedState: string;
}

export interface CanonLedgerEntry {
  id: string;
  type: CanonLedgerEntryType;
  status: CanonLedgerEntryStatus;
  title: string;
  content: string;
  rationale: string;
  impactScope: string;
  conflictCheck: string;
  sourceRunId: string;
  sourceChapterId: string;
}

export interface ForeshadowLedgerEntry {
  id: string;
  name: string;
  type: ForeshadowLedgerEntryType;
  status: ForeshadowLedgerStatus;
  setup: string;
  payoff: string;
  sourceVolumeId: string;
  plannedSetupChapterId: string;
  plannedPayoffChapterId: string;
  actualSetupChapterIds: string[];
  actualReinforceChapterIds: string[];
  actualPayoffChapterId: string;
  importance: number;
  evidence: string;
  notes: string;
}

export interface CharacterLedgerEntry {
  id: string;
  characterName: string;
  role: string;
  type: CharacterLedgerEntryType;
  status: CharacterLedgerStatus;
  summary: string;
  currentGoal: string;
  currentIntent: string;
  nextPressure: string;
  secret: CharacterSecretState | null;
  relationship: CharacterRelationshipState | null;
  abilityCost: CharacterAbilityCostState | null;
  psychology: CharacterPsychologyState | null;
  beliefShift: string;
  identityState: string;
  importance: number;
  evidence: string;
  notes: string;
  sourceRunId: string;
  sourceChapterId: string;
}

export interface CharacterSecretState {
  content: string;
  status: string;
  knownBy: string[];
}

export interface CharacterRelationshipState {
  targetCharacter: string;
  status: string;
  tension: number;
  change: string;
}

export interface CharacterAbilityCostState {
  ability: string;
  levelOrBoundary: string;
  cost: string;
  debt: string;
  limitation: string;
}

export interface CharacterPsychologyState {
  emotion: string;
  wound: string;
  copingStrategy: string;
  stressLevel: number;
}

export interface StoryStateSnapshot {
  chapterId: string;
  chapterTitle: string;
  chapterGoal: string;
  chapterTurn: string;
  readerExperienceGoal: string;
  previousChapterId: string;
  previousChapterSummary: string;
  activeConflicts: string[];
  activeForeshadowing: string[];
  foreshadowLedgerItems: string[];
  worldRules: string[];
  characterStates: string[];
  characterLedgerItems: string[];
  usedPlotPatterns: string[];
  longDistanceRecall: string[];
  similarContentFragments: string[];
  ragSearchQueries: string[];
  warnings: string[];
}

export interface NovelAgentPostGenerationReview {
  reviewId: string;
  chapterId: string;
  overallResult: string;
  summary: string;
  qualityScore: number;
  contentLength: number;
  validationOverallResult: string;
  validationIssueCount: number;
  requiresRewrite: boolean;
  checks: NovelAgentReviewCheck[];
  storyVariableChanges: string[];
  proposedCanonEntries: CanonLedgerEntry[];
  proposedForeshadowEntries: ForeshadowLedgerEntry[];
  proposedCharacterEntries: CharacterLedgerEntry[];
  nextChapterSuggestions: string[];
}

export interface NovelAgentReviewCheck {
  key: string;
  name: string;
  status: NovelAgentReviewCheckStatus;
  riskLevel: NovelToolRiskLevel;
  message: string;
  evidence: string;
  suggestions: string[];
}

export interface NovelAgentRewriteAttempt {
  attemptId: string;
  chapterId: string;
  success: boolean;
  beforeQualityScore: number;
  afterQualityScore: number;
  repairHints: string;
  repairResult: string;
  reviewAfterRewrite: NovelAgentPostGenerationReview | null;
}

export interface CreativeKnowledgeEntry {
  id: string;
  category: CreativeKnowledgeCategory;
  genre: string;
  subGenre: string;
  title: string;
  content: string;
  tags: string[];
  weight: number;
  source: string;
  usageCount?: number;
  createdAt?: string;
  projectUsageStatus?: string;
  projectUsageCount?: number;
  projectLastUsedAt?: string | null;
}

export interface CreativeKnowledgeMutationResult {
  success: boolean;
  message: string;
  entry: CreativeKnowledgeEntry | null;
}

export interface CreativeKnowledgeRetrievalResult {
  success: boolean;
  message: string;
  query: string;
  hits: CreativeKnowledgeHit[];
  genrePrinciples: string[];
  tropeWarnings: string[];
  antiTropeStrategies: string[];
  emotionRelationshipGuides: string[];
  projectMemory: string[];
}

export interface CreativeKnowledgeHit {
  entry: CreativeKnowledgeEntry;
  score: number;
  reason: string;
}

export interface StoryBibleDocument {
  schemaVersion: string;
  constitution: StoryCreativeConstitution | null;
  macroCandidates: MacroStoryConceptCandidate[];
  volumeArcs: VolumeArcPlan[];
  foreshadowLedger: ForeshadowLedgerEntry[];
  characterLedger: CharacterLedgerEntry[];
  canonLedger: CanonLedgerEntry[];
  agentRuns: NovelAgentRun[];
  revisions: StoryBibleRevision[];
}

export interface StoryBibleRevision {
  id: string;
  action: string;
  summary: string;
  sourceRunId: string;
  sourceChapterId: string;
}

// ============================================================
// API Request/Response DTOs
// ============================================================

export interface CommitStoryFoundationRequest {
  overwrite?: boolean;
  confirmed?: boolean;
  selectedMacroCandidateTitle?: string;
}

export interface ConfirmRequest {
  overwrite?: boolean;
  confirmed?: boolean;
}

export interface ConfirmOnlyRequest {
  confirmed?: boolean;
}

export interface ContinueRunRequest {
  maxAutoRisk?: string;
  maxAutoSteps?: number;
}

export interface SelectChapterCandidateRequest {
  candidateTitles?: string;
  selectionMode?: string;
  selectionRationale?: string;
  confirmed?: boolean;
}

export interface EntryConfirmRequest {
  entryIds?: string;
  confirmed?: boolean;
}

export interface CreativeKnowledgeQueryRequest {
  query?: string;
}

export interface UsedPatternRequest {
  chapterId?: string;
  pattern?: string;
  note?: string;
}

export interface MaterialIngestRequest {
  fileName?: string;
  content?: string;
  sourceType?: string;
}

export interface MaterialUpdateRequest {
  fileName?: string;
  summary?: string;
  tags?: string;
  content?: string;
}

// ============================================================
// REST API Response Types (Phase 4)
// ============================================================

// Material response types
export interface MaterialResponse {
  id: string;
  userId: string;
  projectId: string | null;
  title: string;
  category: string | null;
  contentType: string | null;
  filePath: string | null;
  tags: string | null;
  createdAt: string;
  vectorChunkCount: number;
}

export interface MaterialListResponse {
  materials: MaterialResponse[];
  totalCount: number;
}

export interface MaterialContentResponse {
  id: string;
  title: string;
  contentType: string | null;
  content: string;
}

export interface UploadMaterialResponse {
  id: string;
  title: string;
  filePath: string;
  category: string | null;
}

// Knowledge response types
export interface KnowledgeResponse {
  id: string;
  projectId: string;
  entryType: string;
  title: string;
  content: string;
  usageCount: number;
  createdAt: string;
  vectorId: string | null;
  projectUsageStatus: string;
  projectUsageCount: number;
  projectLastUsedAt: string | null;
}

export interface KnowledgeSearchResult {
  id: string;
  entryType: string;
  title: string;
  content: string;
  score: number;
  projectUsageStatus: string;
  projectUsageCount: number;
  projectLastUsedAt: string | null;
}

export interface KnowledgeSearchResponse {
  results: KnowledgeSearchResult[];
  totalCount: number;
}

// StoryBible response types
export interface StoryConstitutionResponse {
  id: string;
  userId: string;
  projectId: string;
  genre: string;
  subGenre: string | null;
  coreHook: string;
  readerPromise: string | null;
  genreProfile: string | null;
  targetAudience: string | null;
  taboos: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CharacterResponse {
  id: string;
  userId: string;
  projectId: string;
  name: string;
  role: string;
  alias: string | null;
  age: number | null;
  gender: string | null;
  appearance: string | null;
  personality: string | null;
  background: string | null;
  initialPowerLevel: string | null;
  currentPowerLevel: string | null;
  specialAbilities: string | null;
  coreGoal: string | null;
  motivation: string | null;
  relationships: string | null;
  status: string;
  firstAppearChapter: string | null;
  lastAppearChapter: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface StoryBibleResponse {
  constitution: StoryConstitutionResponse | null;
  characters: CharacterResponse[];
}

// VolumeArc response types
export interface VolumeArcResponse {
  id: string;
  userId: string;
  projectId: string;
  volumeNumber: number;
  volumeTitle: string;
  volumeTheme: string | null;
  targetChapters: number | null;
  currentChapters: number;
  act1Setup: string | null;
  act2Confrontation: string | null;
  act3Climax: string | null;
  act4Resolution: string | null;
  keyEvents: string | null;
  majorConflict: string | null;
  conflictEscalation: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
}

export interface StoryFoundationRequest {
  userSeed: string;
  genre: string;
  subGenre: string;
  targetReader: string;
  desiredDirection: string;
}

export interface VolumeArcPlanningRequest {
  userGoal: string;
  volumeId: string;
  volumeTitle: string;
  startChapterId: string;
  endChapterId: string;
  expectedChapterCount: number;
}

export interface ChapterCreativeRequest {
  userGoal: string;
  chapterId: string;
  constitution?: StoryCreativeConstitution;
  volumeArc?: VolumeArcPlan;
}

export interface NovelProjectCreateRequest {
  title?: string;
  genre?: string;
  seed?: string;
}

export interface NovelProjectUpdateRequest {
  title?: string;
  status?: string;
}

export interface NovelProjectInfo {
  id: string;
  title: string;
  genre: string;
  subGenre: string;
  coreHook: string;
  readerPromise: string;
  status: string;
  storageProjectName: string;
  createdAt: string;
  updatedAt: string;
}

export interface NovelProjectDeleteResult {
  success: boolean;
  message: string;
  activeProjectId: string;
}

// ============================================================
// Response DTOs
// ============================================================

export interface NovelLibraryDocument {
  books: NovelBookView[];
  activeBook: NovelBookView | null;
  volumes: NovelVolumeView[];
  selectedChapter: NovelChapterView | null;
  generatedChapterCount: number;
  plannedChapterCount: number;
  needsRewriteCount: number;
}

export interface NovelBookView {
  projectId: string;
  title: string;
  genre: string;
  subGenre: string;
  coreHook: string;
  readerPromise: string;
  status: string;
  isActive: boolean;
  volumeCount: number;
  generatedChapterCount: number;
  plannedChapterCount: number;
  needsRewriteCount: number;
  updatedAt: string;
  selectedChapter: NovelChapterView | null;
}

export interface WorkspaceResponse {
  projects: NovelBookView[];
  totalCount: number;
}

export interface NovelVolumeView {
  volumeId: string;
  title: string;
  status: string;
  startChapterId: string;
  endChapterId: string;
  expectedChapterCount: number;
  chapters: NovelChapterView[];
}

export interface NovelChapterView {
  chapterId: string;
  title: string;
  volumeId: string;
  volumeTitle: string;
  beatIndex: number;
  beatRole: string;
  goal: string;
  turn: string;
  cost: string;
  status: string;
  runId: string;
  intent: string;
  updatedAt: string;
  hasGeneratedContent: boolean;
  needsRewrite: boolean;
  wordCount: number;
  summary: string;
  content: string;
  selectedCandidateTitle: string;
  qualityScore: number;
  rewriteAttemptCount: number;
  reviewChecks: string[];
  nextSuggestions: string[];
  writingStatus: string;
  contextPackageStatus: string;
  draftArtifactStatus: string;
  gateStatus: string;
  changesProtocolPassed: boolean;
  factSnapshotPassed: boolean;
  blueprintPassed: boolean;
  longDistanceRagPassed: boolean;
  ragRecallCount: number;
  repairAttemptCount: number;
  gateIssues: string[];
  repairHints: string[];
  dependencyWarnings: string[];
  contextWarnings: string[];
  visibleInWorkflow: boolean;
  visibleInLibrary: boolean;
  userVisibleStatus: string;
  artifactStatus: string;
  draftArtifactId: string;
  gateReportId: string;
  qualityReportId: string;
}

export interface ProjectWorkflowDocument {
  project: NovelBookView | null;
  library: NovelLibraryDocument;
  sessions: WorkflowSessionSummary[];
  runs: NovelAgentRun[];
  schedulerTasks: AgentScheduledTask[];
  missionPlans: AgentMissionPlan[];
  chapterArtifacts: WorkflowChapterArtifactSummary[];
  currentChapterArtifacts: WorkflowChapterArtifactSummary[];
  diagnosticTasks: AgentScheduledTask[];
  activityScore: number;
  isEmptyProject: boolean;
  suspectReasons: string[];
  staleMissionWarnings: string[];
  pendingConfirmation?: AgentPendingConfirmation | null;
  pendingConfirmationSessionId: string;
  activeSessionId: string;
  activeRunId: string;
  updatedAt: string;
}

export interface WorkflowSessionSummary {
  sessionId: string;
  title: string;
  phase: string;
  activeRunId: string;
  updatedAt: string;
  missionPlan: AgentMissionPlan;
  pendingConfirmation?: AgentPendingConfirmation | null;
}

export interface WorkflowChapterArtifactSummary {
  chapterId: string;
  runId: string;
  intent: string;
  status: string;
  updatedAt: string;
  candidateTitle: string;
  draftStatus: string;
  draftPreview: string;
  hasDraft: boolean;
  gateStatus: string;
  gateIssues: string[];
  repairHints: string[];
  qualityStatus: string;
  qualityScore: number;
  qualityIssues: string[];
  contextWarnings: string[];
  dependencyWarnings: string[];
  ragRecallCount: number;
  sourceRunIds: string[];
  lifecycleRank: number;
  isCurrent: boolean;
}

export interface MaterialLibraryDocument {
  materials: MaterialReference[];
}

export interface MaterialReference {
  id: string;
  fileName: string;
  sourceType: string;
  summary: string;
  tags: string[];
  workflowReferences: string[];
  characterCount: number;
  createdAt: string;
  isAnalyzed: boolean;
  analysisResults: MaterialAnalysisStageResult[];
  knowledgeEntriesCreated: number;
}

// ============================================================
// Material Analysis DTOs
// ============================================================

export interface MaterialAnalysisProgress {
  stage: string;
  stageLabel: string;
  stageIndex: number;
  totalStages: number;
  status: 'running' | 'completed' | 'failed' | 'done';
  message: string;
  entries: CreativeKnowledgeEntry[];
}

export interface MaterialAnalysisResult {
  success: boolean;
  materialId: string;
  fileName: string;
  totalEntriesCreated: number;
  stages: MaterialAnalysisStageResult[];
  material: MaterialReference | null;
}

export interface MaterialAnalysisStageResult {
  stage: string;
  stageLabel: string;
  entriesCreated: number;
  entryTitles: string[];
}

export interface MaterialAnalysisRequest {
  fileName?: string;
  content?: string;
  sourceType?: string;
}

export interface WorkspaceInfo {
  projectName: string;
  storageRoot: string;
  storyBiblePath: string;
  creativeKnowledgePath: string;
}

export interface NovelAgentRunOperationResult {
  success: boolean;
  message: string;
  run: NovelAgentRun | null;
  runs: NovelAgentRun[];
}

export interface StoryBibleCommitResult {
  success: boolean;
  requiresOverwrite: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  storagePath: string;
  document: StoryBibleDocument | null;
}

export interface ChapterCandidateSelectionResult {
  success: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  run: NovelAgentRun | null;
}

export interface NovelAgentExecutionResult {
  success: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  writerResult: string;
  contextPackage?: ChapterContextPackageSummary | null;
  draftArtifact?: ChapterDraftArtifact | null;
  gateReport?: GenerationGateReport | null;
  dependencyImpact?: DependencyImpactReport | null;
  run: NovelAgentRun | null;
}

export interface CanonMaintenanceResult {
  success: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  importedEntries: CanonLedgerEntry[];
  promotedEntries: CanonLedgerEntry[];
  rejectedEntries: CanonLedgerEntry[];
  conflictEntries: CanonLedgerEntry[];
  run: NovelAgentRun | null;
  document: StoryBibleDocument | null;
}

export interface ForeshadowMaintenanceResult {
  success: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  importedEntries: ForeshadowLedgerEntry[];
  updatedEntries: ForeshadowLedgerEntry[];
  conflictEntries: ForeshadowLedgerEntry[];
  run: NovelAgentRun | null;
  document: StoryBibleDocument | null;
}

export interface CharacterMaintenanceResult {
  success: boolean;
  requiresConfirmation: boolean;
  riskLevel: NovelToolRiskLevel;
  message: string;
  importedEntries: CharacterLedgerEntry[];
  updatedEntries: CharacterLedgerEntry[];
  conflictEntries: CharacterLedgerEntry[];
  run: NovelAgentRun | null;
  document: StoryBibleDocument | null;
}

// ============================================================
// Agent Session & SSE Types
// ============================================================

export interface AgentChatRequest {
  message?: string;
  sessionId?: string;
}

export interface AgentChatResponse {
  reply: string;
  suggestions: string[];
  sessionId: string;
  runId: string | null;
  phase: string;
  decision?: AgentDecisionTrace | null;
  rag?: AgentRagContext | null;
  memory?: AgentWorkingMemorySnapshot | null;
  runtimeTrace?: AgentRuntimeStep[] | null;
  missionPlan?: AgentMissionPlan | null;
  pendingConfirmation?: AgentPendingConfirmation | null;
}

export interface AgentDecisionTrace {
  mode: string;
  intent: string;
  needsRag: boolean;
  ragQueries: string[];
  toolName: string;
  risk: string;
  requiresConfirmation: boolean;
  assistantBrief: string;
  source: string;
}

export interface AgentRagContext {
  used: boolean;
  strategy?: AgentRagStrategy;
  queries: string[];
  knowledgeNotes: string[];
  projectNotes: string[];
  resultsByBucket?: Record<string, string[]>;
}

export interface AgentRagStrategy {
  stage: string;
  queryPlan: AgentRagQueryPlan[];
}

export interface AgentRagQueryPlan {
  bucket: string;
  query: string;
  reason: string;
}

export interface AgentWorkingMemorySnapshot {
  currentGoal: string;
  openQuestions: string[];
  userPreferences: string[];
  recentObservations?: AgentRuntimeObservation[];
  pendingToolName: string;
  lastIntent: string;
  lastMode: string;
  mission?: AgentMissionStateSnapshot;
  missionPlan?: AgentMissionPlan;
  pendingConfirmation?: AgentPendingConfirmation | null;
}

export interface AgentToolCall {
  name: string;
  arguments: Record<string, string>;
}

export interface AgentPendingConfirmation {
  confirmationId: string;
  toolCall?: AgentToolCall | null;
  risk: string;
  impactSummary: string;
  requiresUserInput: string;
  projectId: string;
  runId: string;
  createdAt: string;
}

export interface AgentToolArtifact {
  artifactType: string;
  artifactId: string;
  projectId: string;
  runId: string;
  summary: string;
  nextHints: string[];
  visibleInWorkflow?: boolean;
  visibleInLibrary?: boolean;
  userVisibleStatus?: string;
}

export interface AgentRuntimeObservation {
  stepIndex: number;
  toolName: string;
  success: boolean;
  requiresConfirmation: boolean;
  risk: string;
  message: string;
  runId: string;
  phase: string;
  artifact?: AgentToolArtifact | null;
  createdAt: string;
}

export interface AgentMissionPlan {
  missionId: string;
  blackboardVersion: number;
  dependencyVersion: number;
  lastRecoveredAt?: string | null;
  projectId: string;
  projectTitle: string;
  status: string;
  currentObjective: string;
  activeChapterId: string;
  turnIntent?: TurnIntent | null;
  interactionState?: UserTurnEnvelope | null;
  activeTurnId?: string;
  activeToolTransactionId?: string;
  artifactCursor?: string;
  activeArtifactCursor?: string;
  lastUserVisibleState?: string;
  lifecycleStatus?: string;
  lastRecoveredFrom?: string;
  recoveryConfidence?: number;
  schedulerLease?: string;
  reviewReports?: AgentReviewerReport[];
  qualityArbiterDecision?: QualityArbiterDecision;
  toolTransactions?: ToolTransaction[];
  allowedNextActions: string[];
  activeArtifacts: AgentToolArtifact[];
  blockedReason: string;
  lastVerifiedState: string;
  currentRunId: string;
  overallGoal: string;
  currentNovelGoal: string;
  stage: string;
  milestones: string[];
  todoQueue: string[];
  completedItems: string[];
  blockers: string[];
  authorPreferences?: string[];
  confirmedDecisions: string[];
  bookTaskTree?: AgentBookTaskTree;
  schedulerState?: AgentTaskSchedulerState;
  dependencyImpacts: AgentDependencyImpactState[];
  recoveryWarnings: string[];
  updatedAt: string;
}

export interface AgentBookTaskTree {
  bookId: string;
  projectId: string;
  title: string;
  status: string;
  currentFocus: string;
  foundation: AgentFoundationTask;
  volumes: AgentVolumeTask[];
  updatedAt: string;
}

export interface AgentFoundationTask {
  status: string;
  confirmedAt?: string | null;
  blockers: string[];
  decisions: string[];
  updatedAt: string;
}

export interface AgentVolumeTask {
  volumeId: string;
  title: string;
  goal: string;
  status: string;
  startChapterId: string;
  endChapterId: string;
  chapters: AgentChapterTask[];
  blockers: string[];
  updatedAt: string;
}

export interface AgentChapterTask {
  chapterId: string;
  title: string;
  status: string;
  candidateStatus: string;
  contextStatus: string;
  draftStatus: string;
  gateStatus: string;
  qualityStatus: string;
  qualityScores: AgentQualityScores;
  repairAttemptCount: number;
  commitStatus: string;
  dependencyStatus: string;
  requiresContextRebuild: boolean;
  requiresRevalidation: boolean;
  runId: string;
  gateIssueSummary: string;
  qualityIssueSummary: string;
  artifactStatus?: string;
  draftArtifactId?: string;
  gateReportId?: string;
  qualityReportId?: string;
  commitConfirmationId?: string;
  userVisibleStatus?: string;
  nextAction: string;
  allowedNextActions: string[];
  lastArtifactIds: string[];
  lastTransitionReason: string;
  updatedAt: string;
}

export interface TurnIntent {
  type: string | number;
  label: string;
  rawMessage: string;
  creativeBrief: string;
  referencedProjectId: string;
  referencedRunId: string;
  referencedChapterId: string;
  selectedOption: string;
  selectedOptionIndex?: number | null;
  selectionKind: string;
  constraints: string[];
  confidence: number;
  source: string;
}

export interface UserTurnEnvelope {
  turnId: string;
  intent: TurnIntent;
  dialogueAct: string | number;
  targetArtifact: string;
  referencedTask: string;
  confirmationDecision: string;
  creativeBrief: string;
  feedbackPatch: string;
  statusQueryScope: string;
  selectedOption: string;
  selectedOptionIndex?: number | null;
  selectionKind: string;
  createdAt: string;
}

export interface AgentTaskSchedulerState {
  tasks: AgentScheduledTask[];
  activeTaskId: string;
  activeRunId: string;
  activeChapterId: string;
  lastDecisionReason: string;
  leaseOwner?: string;
  leaseExpiresAt?: string | null;
  updatedAt: string;
}

export interface ToolTransaction {
  transactionId: string;
  turnId: string;
  toolName: string;
  arguments: Record<string, string>;
  policyResult: string;
  confirmationState: string;
  executionState: string;
  observationArtifact?: AgentToolArtifact | null;
  createdAt: string;
  updatedAt: string;
}

export interface AgentScheduledTask {
  taskId: string;
  sessionId: string;
  projectId: string;
  projectTitle: string;
  chapterId: string;
  runId: string;
  taskType: string;
  status: string;
  nextAction: string;
  blockedReason: string;
  risk: string;
  requiresConfirmation: boolean;
  reason: string;
  updatedAt: string;
}

export interface AgentQualityGateReport {
  status: string;
  scores: AgentQualityScores;
  issues: string[];
  evidence: string[];
  rewriteDecision: string;
  recommendedTool: string;
  requiresUserInput: boolean;
  reviewReports?: AgentReviewerReport[];
  arbiterDecision?: QualityArbiterDecision;
}

export interface AgentReviewerReport {
  reviewer: string;
  status: string;
  score: number;
  issues: string[];
  evidence: string[];
  rewriteAdvice: string;
  blocking: boolean;
}

export interface QualityArbiterDecision {
  status: string;
  reason: string;
  recommendedTool: string;
  requiresUserInput: boolean;
  blockingReviewers: string[];
}

export interface AgentQualityScores {
  pacing: number;
  characterMotivation: number;
  conflict: number;
  continuity: number;
  prose: number;
  readerPromise: number;
}

export interface AgentDependencyImpactState {
  impactId: string;
  sourceRunId: string;
  status: string;
  changedModules: string[];
  impactedModules: string[];
  impactedChapters: string[];
  summary: string;
  createdAt: string;
}

export interface AgentMissionPatch {
  status: string;
  stage: string;
  currentFocus: string;
  chapterPatches: Array<{
    chapterId: string;
    status: string;
    gateStatus: string;
    qualityIssueSummary: string;
    nextAction: string;
  }>;
}

export interface AgentActionTrace {
  type: string | number;
  intent: string;
  toolCall?: AgentToolCall | null;
  reply: string;
  brief: string;
  risk: string;
  requiresConfirmation: boolean;
  ragQueries: string[];
  suggestions: string[];
  confidence: number;
  source: string;
}

export interface AgentReflection {
  summary: string;
  goalSatisfied: boolean;
  shouldContinue: boolean;
  requiresUserInput: boolean;
  nextIntent: string;
  replyDraft: string;
  completedItems: string[];
  newTodoItems: string[];
  blockers: string[];
  qualityGate?: AgentQualityGateReport;
  missionPatch?: AgentMissionPatch;
  nextToolCallRecommendation?: AgentToolCall | null;
}

export interface AgentRuntimeStep {
  stepIndex: number;
  stage: string;
  action?: AgentActionTrace | null;
  observation?: AgentRuntimeObservation | null;
  reflection?: AgentReflection | null;
  stopReason: string;
  createdAt: string;
}

export interface AgentMissionStateSnapshot {
  currentGoal: string;
  creativePhase: string;
  readiness: string;
  nextIntent: string;
  pendingUserDecision: string;
  foundationBrief: Record<string, string>;
  missingFoundationFields: string[];
  pendingQuestion: string;
}

export interface AgentConversationTurnView {
  role: string;
  content: string;
  createdAt: string;
}

export interface AgentSseEvent {
  type: string;
  sessionId: string;
  runId?: string;
  stepId?: string;
  message: string;
  data?: unknown;
  timestamp: string;
}

export interface AgentSessionInfo {
  sessionId: string;
  title: string;
  phase: string;
  activeProjectId: string;
  activeRunId: string | null;
  isArchived: boolean;
  runHistory: string[];
  createdAt: string;
  updatedAt: string;
  messages: AgentConversationTurnView[];
  memory: AgentWorkingMemorySnapshot;
}

export interface AgentSessionSummary {
  sessionId: string;
  title: string;
  phase: string;
  activeProjectId: string;
  activeRunId: string | null;
  isArchived: boolean;
  updatedAt: string;
  messageCount: number;
  memory: AgentWorkingMemorySnapshot;
}

export interface AgentSessionUpdateRequest {
  title?: string;
  isArchived?: boolean;
}

// ============================================================
// User Settings
// ============================================================

export interface LlmPreset {
  name: string;
  provider: string;
  baseUrl: string;
  model: string;
}

export interface UserSettings {
  // LLM
  llmProvider: string;
  llmApiKey: string;
  llmBaseUrl: string;
  llmModel: string;
  llmTemperature: number;
  llmMaxTokens: number;

  // Embedding
  embeddingProvider: string;
  embeddingApiKey: string;
  embeddingBaseUrl: string;
  embeddingModel: string;

  // Agent
  agentDefaultRisk: string;
  agentAutoContinue: boolean;
  agentMaxAutoSteps: number;

  // Generation defaults
  defaultGenre: string;
  defaultSubGenre: string;
  defaultChapterWordCount: number;
  defaultVolumeChapterCount: number;

  // UI
  theme: string;
  language: string;
  showStepDetails: boolean;

  // Presets
  presets: LlmPreset[];
}
