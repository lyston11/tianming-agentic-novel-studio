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
  | 'StyleExample'
  | 'HardFact'
  | 'ProjectUsedPattern'
  | 'ReaderPromise'
  | 'ThemeDepth'
  | 'EmotionArc'
  | 'RelationshipDynamic';

export type GoalCancellationStrategy =
  | 'PreserveCandidateBranch'
  | 'MergeAcceptedPrefix'
  | 'DiscardCandidateBranch';

export type NovelAgentStreamKind = 'Conversation' | 'Workflow';

export type NovelAgentConversationDecisionKind =
  | 'DiscussOnly'
  | 'ProposeGoal'
  | 'ProposeRevision'
  | 'NeedClarification'
  | 'RejectUnsafe';

export interface NovelAgentConversationConfirmation {
  goalId: string;
  goalRevisionId: string;
  productionId: string;
  correlationId: string;
}

export interface AppendNovelAgentTurnRequest {
  idempotencyKey: string;
  content: string;
  attachmentIds?: string[] | null;
}

export interface AccessibleProjectCatalogItem {
  projectId: string;
  title: string;
  status: string;
  updatedAt: string;
  description?: string | null;
}

export interface ActivateNovelAgentProjectContextRequest {
  projectId: string;
  idempotencyKey: string;
  expectedBindingVersion: number;
  sourceUserMessageId?: string | null;
  confirmationActionId?: string | null;
}

export interface ProjectContextActivationResult {
  code: string;
  succeeded: boolean;
  recoverable: boolean;
  message: string;
  projectId?: string | null;
  bindingVersion: number;
  confirmedAt?: string | null;
}

export interface NovelAgentConversationDecision {
  kind: NovelAgentConversationDecisionKind;
  message: string;
  proposalId?: string | null;
  proposalJson?: string | null;
  proposalHash?: string | null;
  autoConfirmRequested?: boolean;
}

export interface NovelAgentConversationTurnResult {
  messageId: string;
  decision: NovelAgentConversationDecision;
  correlationId: string;
  confirmation?: NovelAgentConversationConfirmation | null;
  toolError?: string | null;
}

export interface ConfirmNovelAgentProposalRequest {
  idempotencyKey: string;
  confirmationNote?: string | null;
}

export interface ConfirmNovelAgentProposalResult {
  goalId: string;
  goalRevisionId: string;
  productionId: string;
  correlationId: string;
}

export interface CreateLegacyRecoveryProposalRequest {
  sessionId: string;
  idempotencyKey: string;
  objective: string;
  mode: 'SingleChapter' | 'InteractiveBatch' | 'AutonomousBook';
  startChapter: number;
  endChapter: number;
  successCriteria: string[];
  mustPreserve?: string[] | null;
  mustHappen?: string[] | null;
  mustNotChange?: string[] | null;
  acceptancePolicy?: string;
  reworkPolicy?: string;
  totalCostLimit?: number;
}

export interface CreateLegacyRecoveryProposalResult {
  proposalId: string;
  contractHash: string;
  evidence: {
    formalChapterVersionIds: string[];
    confirmedDecisionIds: string[];
    knowledgeIds: string[];
  };
  correlationId: string;
}

export interface NovelAgentWorkflowProposalView {
  id: string;
  sourceSessionId: string;
  status: string;
  contractHash: string;
  updatedAt: string;
}

export interface NovelAgentWorkflowGoalView {
  id: string;
  status: string;
  objective: string;
  currentRevisionId?: string | null;
  version: number;
  totalCostLimit: number;
  reservedCost: number;
  actualCost: number;
}

export interface NovelAgentWorkflowProductionView {
  id: string;
  goalId: string;
  goalRevisionId: string;
  mode: string;
  status: string;
  taskGraphVersionId: string;
  version: number;
  terminalReason?: string | null;
}

export interface NovelAgentWorkflowTaskView {
  id: string;
  goalId: string;
  taskType: string;
  kernelName: string;
  status: string;
  attempt: number;
  maxAttempts: number;
  priority: number;
}

export interface NovelAgentWorkflowProjectView {
  projectId: string;
  proposals: NovelAgentWorkflowProposalView[];
  goals: NovelAgentWorkflowGoalView[];
  productions: NovelAgentWorkflowProductionView[];
  tasks: NovelAgentWorkflowTaskView[];
  lastWorkflowSequence: number;
}

export interface NovelAgentWorkflowResponse {
  current: NovelAgentWorkflowProjectView;
  legacy: ProjectWorkflowDocument;
}

export interface NovelAgentEventEnvelope<TData = unknown> {
  eventId: string;
  streamKind: NovelAgentStreamKind;
  streamId: string;
  sequence: number;
  eventType: string;
  schemaVersion: number;
  occurredAt: string;
  correlationId: string;
  causationId?: string | null;
  projectId: string | null;
  sessionId?: string | null;
  goalId?: string | null;
  productionId?: string | null;
  transient: boolean;
  data: TData;
}

export interface CreativeGoalContract {
  goalType: string;
  collaborationMode: string;
  humanReadableObjective: string;
  targetChapterRangeJson: string;
  successCriteria: string[];
  mustPreserve: string[];
  mustHappen: string[];
  mustNotChange: string[];
  acceptancePolicyJson: string;
  reworkPolicyJson: string;
  executionStrategy: BookExecutionStrategy;
  bookPlanJson: string;
}

export type BookExecutionStrategy = 'full_auto' | 'interactive_batch';

export interface BookProductionView {
  id: string;
  goalId: string;
  executionStrategy: BookExecutionStrategy;
  status: string;
  targetStartChapterNumber: number;
  targetEndChapterNumber: number;
  nextChapterNumber: number;
  batchSize: number;
  currentBatchNumber: number;
  completedAt?: string | null;
}

export interface ProductionBatchView {
  id: string;
  batchNumber: number;
  startChapterNumber: number;
  endChapterNumber: number;
  status: string;
  taskGraphVersionId?: string | null;
  canonBranchId?: string | null;
  acceptanceActor: 'human' | 'agent';
}

export interface DirectorTurnView {
  projectId: string;
  state: 'Exploring' | 'Proposed' | 'Committed' | 'Revising' | 'Cancelled';
  authorization: 'None' | 'UnambiguousLanguage' | 'ConfirmedContract' | 'ExplicitAction';
  requiresConfirmation: boolean;
  rationale: string;
  proposedContract?: CreativeGoalContract | null;
}

export interface GoalWorkflowConfirmationView {
  submission: {
    status: string;
    goalId?: string | null;
  };
  graph: unknown;
}

export interface CreativeGoalView {
  id: string;
  projectId: string;
  sourceSessionId: string;
  humanReadableObjective: string;
  status: string;
  totalCostLimit: number;
  reservedCost: number;
  actualCost: number;
  aggregateVersion: number;
  targetChapterRangeJson: string;
}

export interface GoalTaskGraphView {
  id: string;
  version: number;
  status: string;
  contentHash: string;
}

export interface GoalKernelTaskView {
  id: string;
  branchId?: string | null;
  kernelName: string;
  taskType: string;
  status: string;
  attempt: number;
  maxAttempts: number;
  outputArtifactIdsJson: string;
  createdAt: string;
  updatedAt: string;
}

export interface GoalCanonBranchView {
  id: string;
  status: string;
  startChapterNumber: number;
  endChapterNumber: number;
  canonBaselineVersion: string;
}

export interface GoalChapterSummaryView {
  id: string;
  chapterId: string;
  chapterNumber: number;
  version: number;
  status: string;
  authorship: string;
  isProtected: boolean;
  currentArtifactId: string;
  branchId: string;
}

export interface GoalWorkflowStatusView {
  goal: CreativeGoalView;
  production?: BookProductionView | null;
  batches: ProductionBatchView[];
  graph?: GoalTaskGraphView | null;
  tasks: GoalKernelTaskView[];
  branches: GoalCanonBranchView[];
  candidates: GoalChapterSummaryView[];
  candidateChapterCount: number;
}

export interface GoalKernelArtifactView {
  id: string;
  artifactType: string;
  contentJson: string;
  contentHash: string;
  status: string;
  authorship: string;
  isProtected: boolean;
  createdAt: string;
}

export interface GoalKnowledgeCitationView {
  id: string;
  knowledgeEntryId: string;
  knowledgeVersion: number;
  purpose: string;
  sourceArtifactId: string;
}

export interface GoalChapterDetailView {
  candidate: GoalChapterSummaryView;
  draftArtifact: GoalKernelArtifactView;
  reviewArtifacts: GoalKernelArtifactView[];
  citations: GoalKnowledgeCitationView[];
}

export interface GoalChapterReworkRequest {
  candidateChapterId: string;
  candidateVersion: number;
  sessionId: string;
  userDescription: string;
  selectionStart?: number | null;
  selectionEnd?: number | null;
  selectedText: string;
  idempotencyKey: string;
}

export interface GoalChapterReworkResponse {
  taskId: string;
  intentArtifactId?: string | null;
  status: string;
}

export interface GoalChapterManualEditRequest {
  candidateChapterId: string;
  candidateVersion: number;
  content: string;
}

export interface GoalChapterManualEditResponse {
  candidateChapterId: string;
  candidateVersion: number;
  artifactId: string;
  authorship: string;
  isProtected: boolean;
}

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
  forbiddenDirections: string[];
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
  isArchived?: boolean;
  sourceProjectId?: string | null;
  sourceProjectTitle?: string | null;
  sourceType?: string | null;
  sourceUploadTaskId?: string | null;
  chunkIndex?: number | null;
  extractionContext?: string | null;
  projectUsageStatus?: string;
  projectUsageCount?: number;
  projectLastUsedAt?: string | null;
  projectUsages?: KnowledgeProjectUsage[];
  constraintEvidence?: KnowledgeConstraintEvidence[];
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

export interface CreateCreativeIntentRequest {
  projectId: string;
  sessionId?: string;
  runId?: string;
  rawContent: string;
  normalizedIntent?: string;
  source?: string;
  targetScope?: string;
  targetVolumeId?: string;
  targetChapterId?: string;
  targetCharacterName?: string;
  impactLevel?: string;
  requiresConfirmation?: boolean;
  conflictStatus?: string;
  metadataJson?: string;
}

export interface DecideCreativeIntentRequest {
  projectId: string;
  status: string;
  decisionReason?: string;
  conflictStatus?: string;
  markExecuted?: boolean;
}

export interface CreativeIntentItem {
  id: string;
  projectId: string;
  sessionId: string;
  runId: string;
  source: string;
  rawContent: string;
  normalizedIntent: string;
  targetScope: string;
  targetVolumeId: string;
  targetChapterId: string;
  targetCharacterName: string;
  status: string;
  impactLevel: string;
  requiresConfirmation: boolean;
  conflictStatus: string;
  decisionReason: string;
  createdAt: string;
  updatedAt: string;
  decidedAt?: string | null;
  executedAt?: string | null;
}

export interface CreativeIntentQueryResult {
  projectId: string;
  status: string;
  targetChapterId: string;
  items: CreativeIntentItem[];
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
  category: string | null;
}

// Knowledge response types
export interface KnowledgeResponse {
  id: string;
  usageProjectId: string | null;
  sourceProjectId: string | null;
  sourceProjectTitle: string | null;
  sourceType: string;
  sourceUploadTaskId: string | null;
  chunkIndex: number | null;
  extractionContext: string | null;
  entryType: string;
  title: string;
  content: string;
  tags: string[];
  weight: number;
  usageCount: number;
  createdAt: string;
  isArchived: boolean;
  vectorId: string | null;
  projectUsageStatus: string;
  projectUsageCount: number;
  projectLastUsedAt: string | null;
  projectUsages: KnowledgeProjectUsage[];
  constraintEvidence: KnowledgeConstraintEvidence[];
}

export interface KnowledgeDirectoryResponse {
  key: string;
  name: string;
  description: string;
  isSystem: boolean;
  entryCount: number;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface KnowledgeProjectUsage {
  projectId: string;
  projectTitle: string;
  status: string;
  usageCount: number;
  firstSeenAt: string;
  lastUsedAt: string | null;
}

export interface KnowledgeConstraintEvidence {
  knowledgeId: string;
  title: string;
  entryType: string;
  subject: string;
  constraintLevel: string;
  packagePolicy: string;
  evidenceStatus: string;
  gateStatus: string;
  projectId: string;
  projectTitle: string;
  chapterId: string;
  factSnapshotId: string;
  factSnapshotVersion: number;
  createdAt: string;
  allowedTerms: string[];
  forbiddenTerms: string[];
  violations: string[];
}

export interface KnowledgeSearchResult {
  id: string;
  sourceType: string;
  sourceUploadTaskId?: string | null;
  chunkIndex?: number | null;
  extractionContext?: string | null;
  entryType: string;
  title: string;
  content: string;
  score: number;
  projectUsageStatus: string;
  projectUsageCount: number;
  projectLastUsedAt: string | null;
}

export interface KnowledgeProcessingTaskResponse {
  id: string;
  fileName: string;
  fileSize: number;
  status: string;
  strategy: string;
  progress: number;
  totalChunks: number;
  processedChunks: number;
  extractedEntriesCount: number;
  errorMessage?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  createdAt: string;
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
  candidateDirections?: string[];
  forbiddenDirections?: string[];
}

export interface VolumeArcPlanningRequest {
  userGoal: string;
  volumeId: string;
  volumeTitle: string;
  startChapterId: string;
  endChapterId: string;
  expectedChapterCount: number;
  candidateDirections?: string[];
  forbiddenDirections?: string[];
}

export interface ChapterCreativeRequest {
  userGoal: string;
  chapterId: string;
  constitution?: StoryCreativeConstitution;
  volumeArc?: VolumeArcPlan;
  candidateDirections?: string[];
  forbiddenDirections?: string[];
}

export interface NovelProjectCreateRequest {
  title?: string;
  genre?: string;
  seed?: string;
}

export interface NovelProjectUpdateRequest {
  title?: string;
  status?: string;
  coverImageUrl?: string;
}

export interface NovelProjectInfo {
  id: string;
  title: string;
  genre: string;
  subGenre: string;
  coreHook: string;
  readerPromise: string;
  status: string;
  coverImageUrl?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ChapterResponse {
  id: string;
  projectId: string;
  volumeId: string | null;
  title: string;
  chapterNumber: number;
  status: string;
  wordCount: number;
  content?: string | null;
  productionChains: WorkflowProductionChain[];
  productionEvidence: ChapterProductionEvidenceResponse;
  createdAt: string;
  updatedAt: string;
}

export interface ChapterProductionEvidenceResponse {
  revisionPlans: ChapterRevisionPlanEvidenceResponse[];
  latestFactSnapshot?: ChapterFactSnapshotEvidenceResponse | null;
  outboxEvents: ChapterOutboxEvidenceResponse[];
}

export interface ChapterRevisionPlanEvidenceResponse {
  id: string;
  source: string;
  planType: string;
  targetScope: string;
  targetChapterId: string;
  targetChapterLogicalId: string;
  targetChapterDisplayName: string;
  status: string;
  riskLevel: string;
  recommendation: string;
  affectedChapterIds: string[];
  invalidatedPackageIds: string[];
  updatedAt: string;
}

export interface ChapterFactSnapshotEvidenceResponse {
  id: string;
  chapterVersionId: string;
  versionNumber: number;
  source: string;
  snapshotPreview: string;
  createdAt: string;
}

export interface ChapterOutboxEvidenceResponse {
  id: string;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  status: string;
  attempts: number;
  lastError: string;
  nextAttemptAt?: string | null;
  completedAt?: string | null;
  updatedAt: string;
}

export interface ChapterVersionResponse {
  id: string;
  chapterId: string;
  contentDocumentId: string;
  versionNumber: number;
  title: string;
  wordCount: number;
  status: string;
  runtimeRunId?: string | null;
  packageId?: string | null;
  kernelVersion?: string | null;
  promptVersion?: string | null;
  gateReportJson?: string | null;
  agentReviewJson?: string | null;
  rebuiltFromPackageIds: string[];
  isCurrent: boolean;
  contentPreview: string;
  createdAt: string;
}

export interface ChapterVersionDiffBlock {
  kind: 'unchanged' | 'changed' | 'added' | 'removed' | string;
  leftText: string;
  rightText: string;
}

export interface ChapterVersionCreativeIntentAlignment {
  intentId: string;
  normalizedIntent: string;
  targetScope: string;
  targetChapterId: string;
  impactLevel: string;
  source: string;
  status: string;
}

export interface ChapterVersionRevisionPlanAlignment {
  revisionPlanId: string;
  planType: string;
  targetScope: string;
  targetChapterId: string;
  targetChapterLogicalId: string;
  targetChapterDisplayName: string;
  status: string;
  affectedChapterIds: string[];
  invalidatedPackageIds: string[];
  riskLevel: string;
  recommendation: string;
}

export interface ChapterVersionAgentReviewCheckAlignment {
  key: string;
  name: string;
  status: string;
  message: string;
  evidence: string[];
}

export interface ChapterVersionProductionAlignment {
  leftPackageId: string;
  rightPackageId: string;
  agentReviewDecision: string;
  rebuiltFromPackageIds: string[];
  acceptedCreativeIntents: ChapterVersionCreativeIntentAlignment[];
  sourceRevisionPlans: ChapterVersionRevisionPlanAlignment[];
  agentReviewChecks: ChapterVersionAgentReviewCheckAlignment[];
}

export interface ChapterVersionCompareResponse {
  chapterId: string;
  left: ChapterVersionResponse;
  right: ChapterVersionResponse;
  wordCountDelta: number;
  summary: string;
  diffBlocks: ChapterVersionDiffBlock[];
  productionAlignment: ChapterVersionProductionAlignment;
}

export interface ChapterCreateRequest {
  projectId: string;
  volumeId?: string | null;
  title: string;
  chapterNumber: number;
  content: string;
  status?: 'draft' | 'published' | 'archived';
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
  coverImageUrl?: string | null;
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

export interface WorkflowChapterProductionSummary {
  chainId: string;
  status: string;
  summary: string;
  runtimeRunId: string;
  packageId: string;
  updatedAt: string;
  chapterVersionId: string;
  chapterVersionNumber: number;
  factSnapshotId: string;
  factSnapshotVersion: number;
  endingState: string;
  gateStatus: string;
  agentReviewResult: string;
  agentReviewAction: string;
  chapterChangeCount: number;
  revisionPlanCount: number;
  rebuildCount: number;
  revisionPlanIds: string[];
  revisionPlans: WorkflowChapterRevisionPlanSummary[];
  rebuildPackageIds: string[];
  creativeIntents: WorkflowChapterCreativeIntentSummary[];
  traceItems: WorkflowChapterProductionTraceItem[];
  hasCanonicalEvidence: boolean;
}

export interface WorkflowChapterRevisionPlanSummary {
  revisionPlanId: string;
  source: string;
  planType: string;
  targetScope: string;
  targetChapterId: string;
  targetChapterLogicalId: string;
  targetChapterDisplayName: string;
  status: string;
  riskLevel: string;
  recommendation: string;
  affectedChapterIds: string[];
  invalidatedPackageIds: string[];
}

export interface WorkflowChapterCreativeIntentSummary {
  intentId: string;
  normalizedIntent: string;
  targetScope: string;
  targetChapterId: string;
  impactLevel: string;
  source: string;
  status: string;
}

export interface WorkflowChapterProductionTraceItem {
  key: string;
  label: string;
  status: string;
  artifactType: string;
  artifactId: string;
  description: string;
  relatedArtifactIds: string[];
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
  productionSummary?: WorkflowChapterProductionSummary | null;
}

export interface ProjectWorkflowDocument {
  projectionKind: 'goal' | 'legacy';
  latestGoalId: string;
  goalState?: WorkflowGoalState | null;
  legacyAudit: WorkflowLegacyAudit;
  project: NovelBookView | null;
  library: NovelLibraryDocument;
  sessions: WorkflowSessionSummary[];
  runs: WorkflowRunSummary[];
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
  creativeIntents: WorkflowCreativeIntentEvidence[];
  productionStages: WorkflowProductionStage[];
  productionChains: WorkflowProductionChain[];
  artifactTimeline: WorkflowArtifactTimelineItem[];
}

export interface WorkflowGoalState {
  goalId: string;
  sourceSessionId: string;
  goalStatus: string;
  executionStrategy: string;
  productionStatus: string;
  currentBatchNumber: number;
  nextChapterNumber: number;
  activeTaskGraphId: string;
  activeTaskGraphVersion: number;
  batches: WorkflowGoalBatchState[];
  tasks: WorkflowGoalTaskState[];
  candidates: WorkflowGoalCandidateState[];
  artifacts: WorkflowGoalArtifactState[];
}

export interface WorkflowGoalBatchState {
  batchId: string;
  batchNumber: number;
  startChapterNumber: number;
  endChapterNumber: number;
  status: string;
  acceptanceActor: string;
  taskGraphVersionId: string;
  canonBranchId: string;
}

export interface WorkflowGoalTaskState {
  taskId: string;
  taskType: string;
  status: string;
  kernelName: string;
  branchId: string;
  attempt: number;
  maxAttempts: number;
  inputArtifactIds: string[];
  outputArtifactIds: string[];
  failureKind: string;
  lastError: string;
  startedAt?: string | null;
  completedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface WorkflowGoalCandidateState {
  candidateChapterId: string;
  chapterId: string;
  chapterNumber: number;
  version: number;
  status: string;
  authorship: string;
  isProtected: boolean;
  artifactId: string;
  branchId: string;
}

export interface WorkflowGoalArtifactState {
  artifactId: string;
  artifactType: string;
  taskId: string;
  branchId: string;
  status: string;
  createdAt: string;
}

export interface WorkflowLegacyAudit {
  missionPlanCount: number | null;
  agentRunCount: number | null;
  toolExecutionCount: number | null;
  status: string;
}

export interface WorkflowRunSummary {
  runId: string;
  userGoal: string;
  intent: string;
  status: string;
  targetChapterId: string;
  createdAt: string;
  updatedAt: string;
  selectedCandidateTitle: string;
  draftStatus: string;
  gateStatus: string;
  reviewStatus: string;
  qualityScore: number;
  notes: string[];
  steps: WorkflowRunStepSummary[];
}

export interface WorkflowRunStepSummary {
  id: string;
  name: string;
  purpose: string;
  toolName: string;
  status: string;
  riskLevel: string;
  requiresConfirmation: boolean;
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

export interface WorkflowProductionStage {
  key: string;
  label: string;
  surface: string;
  status: string;
  summary: string;
  detail: string;
  artifactCount: number;
  currentCount: number;
  totalCount: number;
  updatedAt: string;
  primaryArtifactId: string;
  primaryRunId: string;
  emptyReason: string;
  nextIntentHint: string;
  productionEvents: WorkflowProductionEventSummary[];
  toolExecutions: WorkflowToolExecutionSummary[];
  taskExecutions: WorkflowGoalTaskState[];
}

export interface WorkflowToolExecutionSummary {
  id: string;
  runId: string;
  toolName: string;
  phase: string;
  status: string;
  risk: string;
  resultMessage: string;
  errorMessage: string;
  startedAt: string;
  completedAt: string;
  semanticContract: WorkflowToolSemanticContractSummary;
  failure?: WorkflowToolFailureSummary | null;
}

export interface WorkflowToolSemanticContractSummary {
  displayName: string;
  domainSurface: string;
  outputKind: string;
  inputArtifacts: string[];
  outputArtifacts: string[];
  idempotencyPolicy: string;
  rollbackPolicy: string;
  userVisibleWhere: string;
  resultSemantics: string;
}

export interface WorkflowToolFailureSummary {
  code: string;
  failedStage: string;
  reason: string;
  recoverable: boolean;
  recommendedAction: string;
  inputArtifacts: WorkflowToolInputArtifactSummary[];
}

export interface WorkflowToolInputArtifactSummary {
  artifactName: string;
  status: string;
  artifactId: string;
  message: string;
  blocksExecution: boolean;
  recommendedActions: string[];
}

export interface WorkflowProductionChain {
  id: string;
  chapterId: string;
  chapterLogicalId: string;
  chapterDisplayName: string;
  runtimeRunId: string;
  packageId: string;
  status: string;
  summary: string;
  updatedAt: string;
  chapterVersionId: string;
  chapterVersionNumber: number;
  factSnapshotId: string;
  factSnapshotVersion: number;
  revisionPlanIds: string[];
  rebuildLinks: WorkflowPackageRebuildLinkEvidence[];
  steps: WorkflowProductionChainStep[];
  evidence?: WorkflowProductionChainEvidence | null;
}

export interface WorkflowProductionChainEvidence {
  gate?: WorkflowGateEvidence | null;
  factSnapshot?: WorkflowFactSnapshotEvidence | null;
  agentReview?: WorkflowAgentReviewSummaryEvidence | null;
  chapterChangeCount: number;
  chapterChangeArtifactIds: string[];
}

export interface WorkflowProductionChainStep {
  key: string;
  label: string;
  status: string;
  eventId: string;
  eventType: string;
  stage: string;
  artifactType: string;
  artifactId: string;
  message: string;
  createdAt: string;
  outboxEventId: string;
}

export interface WorkflowProductionEventSummary {
  id: string;
  runtimeRunId: string;
  chapterId: string;
  packageId: string;
  eventType: string;
  stage: string;
  status: string;
  message: string;
  artifactType: string;
  artifactId: string;
  dataJson: string;
  createdAt: string;
  evidence?: WorkflowProductionEvidenceSummary | null;
  failure?: WorkflowProductionFailureSummary | null;
}

export interface WorkflowProductionFailureSummary {
  code: string;
  stage: string;
  message: string;
  recoverable: boolean;
  recommendedAction: string;
  artifactIds: string[];
  requiresUserDecision: boolean;
}

export interface WorkflowProductionEvidenceSummary {
  packageKind: string;
  packageStatus: string;
  promptVersion: string;
  kernelVersion: string;
  knowledgeFactCount: number;
  knowledgeBindingCount: number;
  knowledgeBindings: WorkflowKnowledgeBindingEvidence[];
  ragQueryCount: number;
  factSnapshotVersion: number;
  factSnapshotSource: string;
  factSnapshotId: string;
  chapterVersionNumber: number;
  chapterVersionId: string;
  chapterVersionStatus: string;
  chapterVersionWordCount: number;
  acceptedCreativeIntentCount: number;
  creativeIntents: WorkflowCreativeIntentEvidence[];
  agentReviewChecks: WorkflowAgentReviewCheckEvidence[];
  knowledgeConstraintEvidence: WorkflowKnowledgeConstraintEvidence[];
  sourceRevisionPlans: WorkflowRevisionPlanEvidence[];
  rebuiltFromPackageIds: string[];
  rollback?: WorkflowRollbackEvidence | null;
  gate?: WorkflowGateEvidence | null;
  factSnapshot?: WorkflowFactSnapshotEvidence | null;
  agentReview?: WorkflowAgentReviewSummaryEvidence | null;
  knowledgeBindingSummary?: WorkflowKnowledgeBindingSummaryEvidence | null;
  memoryReads: WorkflowMemoryReadEvidence[];
  memoryPromotions: WorkflowMemoryPromotionEvidence[];
  rebuildLinks: WorkflowPackageRebuildLinkEvidence[];
  outbox?: WorkflowOutboxEvidence | null;
}

export interface WorkflowGateEvidence {
  status: string;
  protocolPassed: boolean;
  factSnapshotPassed: boolean;
  blueprintPassed: boolean;
  ragPassed: boolean;
  changesDetected: boolean;
  issues: string[];
  repairHints: string[];
}

export interface WorkflowFactSnapshotEvidence {
  protagonistName: string;
  protagonistIdentity: string;
  protagonistStatus: string;
  currentLocation: string;
  systemState: string;
  equipmentState: string;
  keyEvents: string[];
  endingState: string;
  nextChapterMustCarry: string[];
}

export interface WorkflowAgentReviewSummaryEvidence {
  decision: string;
  overallResult: string;
  problems: string[];
  suggestions: string[];
  meetsAcceptedCreativeIntents?: boolean | null;
  continuityRisk: string;
  chapterPacing: string;
  recommendedAction: string;
}

export interface WorkflowMemoryReadEvidence {
  id: string;
  projectId: string;
  sessionId: string;
  runId: string;
  memoryScope: string;
  memoryKeys: string[];
  sourceType: string;
  consumer: string;
  createdAt: string;
}

export interface WorkflowMemoryPromotionEvidence {
  id: string;
  projectId: string;
  sessionId: string;
  runId: string;
  sourceScope: string;
  targetScope: string;
  sourceMemoryKey: string;
  targetMemoryKey: string;
  promotionReason: string;
  createdAt: string;
}

export interface WorkflowPackageRebuildLinkEvidence {
  oldPackageId: string;
  oldPackageStatus: string;
  newPackageId: string;
  newPackageStatus: string;
  newPackageKind: string;
  chapterId: string;
  runtimeRunId: string;
}

export interface WorkflowOutboxEvidence {
  outboxEventId: string;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  status: string;
  attempts: number;
  lastError: string;
}

export interface WorkflowRollbackEvidence {
  targetVersionId: string;
  targetVersionNumber: number;
  currentDocumentId: string;
  invalidatedPackageIds: string[];
  reason: string;
}

export interface WorkflowRevisionPlanEvidence {
  revisionPlanId: string;
  planType: string;
  targetScope: string;
  targetChapterId: string;
  targetChapterLogicalId: string;
  targetChapterDisplayName: string;
  status: string;
  affectedChapterIds: string[];
  invalidatedPackageIds: string[];
  riskLevel: string;
  recommendation: string;
}

export interface WorkflowKnowledgeBindingEvidence {
  knowledgeId: string;
  title: string;
  entryType: string;
  projectUsageStatus: string;
  weight: number;
  role: string;
  constraintLevel: string;
  packagePolicy: string;
  classificationId: string;
  classificationRule: string;
  shouldEnterGate: boolean;
  shouldEnterBlueprint: boolean;
  shouldEnterFactSnapshot: boolean;
  classificationConfidence: number;
}

export interface WorkflowKnowledgeBindingSummaryEvidence {
  bindingCount: number;
  shouldEnterGateCount: number;
  shouldEnterBlueprintCount: number;
  shouldEnterFactSnapshotCount: number;
  hardConstraintCount: number;
  referenceCount: number;
  classifiedCount: number;
  pendingClassificationCount: number;
  importedCount: number;
  referencedCount: number;
}

export interface WorkflowCreativeIntentEvidence {
  intentId: string;
  normalizedIntent: string;
  targetScope: string;
  targetChapterId: string;
  impactLevel: string;
  source: string;
  status: string;
}

export interface WorkflowAgentReviewCheckEvidence {
  key: string;
  name: string;
  status: string;
  message: string;
  evidence: string[];
}

export interface WorkflowKnowledgeConstraintEvidence {
  knowledgeId: string;
  title: string;
  entryType: string;
  subject: string;
  constraintLevel: string;
  packagePolicy: string;
  evidenceStatus: string;
  gateStatus: string;
  chapterId: string;
  factSnapshotId: string;
  factSnapshotVersion: number;
  allowedTerms: string[];
  forbiddenTerms: string[];
  violations: string[];
  classificationId: string;
  classificationRule: string;
  shouldEnterGate: boolean;
  shouldEnterBlueprint: boolean;
  shouldEnterFactSnapshot: boolean;
}

export interface WorkflowArtifactTimelineItem {
  id: string;
  kind: string;
  label: string;
  surface: string;
  status: string;
  title: string;
  summary: string;
  preview: string;
  volumeId: string;
  chapterId: string;
  runId: string;
  updatedAt: string;
  isFinal: boolean;
  isUserVisible: boolean;
  source: string;
}

export interface WorkspaceInfo {
  projectName: string;
  storageRoot: string;
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
  clientMessageId?: string;
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
  memoryAudit?: AgentMemoryAuditSummary | null;
  activeProjectId?: string;
  director?: DirectorTurnView | null;
  knowledge?: AgentKnowledgeContext | null;
}

export interface AgentKnowledgeContext {
  toolName: string;
  intent: 'inventory' | 'retrieve';
  scope: 'current_project' | 'user_library';
  knowledgeVersion: string;
  catalogRevision: string;
  query: string;
  totalCount: number;
  directories: AgentKnowledgeDirectory[];
  items: AgentKnowledgeItem[];
  truncated: boolean;
}

export interface AgentKnowledgeDirectory {
  key: string;
  name: string;
  count: number;
  sampleTitles: string[];
}

export interface AgentKnowledgeItem {
  id: string;
  entryType: string;
  title: string;
  excerpt: string;
  score?: number | null;
  sourceType: string;
  projectUsageStatus: string;
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

export interface AgentMemoryAuditSummary {
  reads: AgentMemoryReadAuditSummary[];
  promotions: AgentMemoryPromotionAuditSummary[];
}

export interface AgentMemoryReadAuditSummary {
  id: string;
  projectId: string;
  sessionId: string;
  runId: string;
  memoryScope: string;
  memoryKeys: string[];
  sourceType: string;
  consumer: string;
  createdAt: string;
}

export interface AgentMemoryPromotionAuditSummary {
  id: string;
  projectId: string;
  sessionId: string;
  runId: string;
  sourceScope: string;
  targetScope: string;
  sourceMemoryKey: string;
  targetMemoryKey: string;
  promotionReason: string;
  createdAt: string;
}

export interface AgentToolCall {
  name: string;
  arguments: Record<string, string>;
}

export interface AgentToolSchema {
  name: string;
  description: string;
  risk: string;
  requiresConfirmation: boolean;
  parameters: Record<string, string>;
  sideEffects: AgentToolSideEffectSpec;
}

export interface AgentToolSideEffectSpec {
  writesLedger: boolean;
  writesRedisRecentCache: boolean;
  writesToolSearchCache: boolean;
  writesSqliteSnapshot: boolean;
  writesMemoryScopes: string[];
  writesSqliteEntities: string[];
  writesVectorIndexes: string[];
}

export interface AgentToolExecutionSnapshot {
  id: string;
  toolName: string;
  status: string;
  runId?: string | null;
  phase: string;
  resultPhase: string;
  resultMessage: string;
  recommendedNextTool: string;
  startedAt: string;
  completedAt?: string | null;
}

export interface AgentToolProgressView {
  executionId: string;
  toolName: string;
  status: string;
  title: string;
  detail: string;
  resultLocation: string;
  runId?: string | null;
  isRunning: boolean;
  startedAt: string;
  completedAt?: string | null;
}

export interface AgentArtifactPreviewView {
  title: string;
  summary: string;
  resultLocation: string;
  runId?: string | null;
  items: string[];
  createdAt: string;
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
  turnId?: string;
  turnIndex?: number;
  role: string;
  content: string;
  knowledge?: AgentKnowledgeContext | null;
  createdAt: string;
}

export interface AgentSseEvent {
  eventId?: string;
  type: string;
  sessionId: string;
  runId?: string;
  sourceMessageId?: string;
  stepId?: string;
  stage?: string;
  status?: string;
  artifactType?: string;
  artifactId?: string;
  displaySurface?: string;
  displayPolicy?: string;
  message: string;
  data?: unknown;
  timestamp: string;
}

export interface AgentRuntimeEventView {
  eventId: string;
  type: string;
  runId?: string | null;
  sourceMessageId?: string | null;
  sessionId?: string;
  userId?: string;
  projectId?: string | null;
  stage?: string;
  status?: string;
  artifactType?: string;
  artifactId?: string;
  displaySurface?: string;
  displayPolicy?: string;
  message: string;
  data?: unknown;
  dataJson?: string;
  timestamp?: string;
  createdAt?: string;
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

export interface AgentSessionResumeResponse extends AgentSessionInfo {
  missionPlan: AgentMissionPlan;
  pendingToolCall?: AgentToolCall | null;
  pendingConfirmation?: AgentPendingConfirmation | null;
  hasPendingTool: boolean;
  hasPendingConfirmation: boolean;
  discoveredPhase?: string | null;
  discoveredTools: AgentToolSchema[];
  toolSearchCacheVersion?: string | null;
  lastToolSearchAt?: string | null;
  toolSearchCacheFresh: boolean;
  toolSearchCacheSource: 'restored' | 'none' | string;
  recentToolExecutions: AgentToolExecutionSnapshot[];
  recentRuntimeEvents: AgentRuntimeEventView[];
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
}

export interface AgentSessionUpdateRequest {
  title?: string;
  isArchived?: boolean;
}

export interface RuntimeRunDto {
  runId: string;
  userId: string;
  sessionId: string;
  projectId?: string | null;
  status: string;
  mode: string;
  currentPhase: string;
  currentStep: number;
  activeTool: string;
  lastMessage: string;
  sourceMessageId: string;
  idempotencyKey: string;
  budgetJson: string;
  resultJson: string;
  errorMessage: string;
  failureJson: string;
  cancelRequested: boolean;
  startedAt?: string | null;
  completedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface RuntimeActiveRunDto {
  hasActiveRun: boolean;
  status: string;
  run?: RuntimeRunDto | null;
  heartbeatAt?: string | null;
  fromDistributedCache: boolean;
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

export interface LlmConnectionHealth {
  status: string;
  isConfigured: boolean;
  isReachable: boolean;
  isAuthenticated: boolean;
  requiresUserAction: boolean;
  statusCode?: number | null;
  provider: string;
  model: string;
  baseUrl: string;
  failureStage: string;
  message: string;
  recommendedAction: string;
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
  agentLoopAutoProceed: boolean;
  agentLoopMaxSteps: number;

  // Generation defaults
  defaultGenre: string;
  defaultSubGenre: string;
  defaultChapterWordCount: number;
  defaultVolumeChapterCount: number;

  // UI
  theme: string;
  language: string;

  // Presets
  presets: LlmPreset[];
}
