import {
  NovelCommandError,
  clone,
  type AcceptCandidateCommand,
  type Acceptance,
  type AcceptanceResult,
  type ActorScope,
  type Batch,
  type CandidateChapter,
  type ChapterCandidateIntent,
  type ConfirmGoalCommand,
  type Goal,
  type GoalCommitResult,
  type GoalProposal,
  type GoalRevision,
  type NovelContextPackage,
  type NovelProjectSnapshot,
  type Production,
  type Review,
  type Task,
  type WorkflowProjection,
  type CanonMergeResult,
} from "../contracts.js";
import type { NovelApplicationPorts } from "../ports.js";
import { InMemoryContextProvider } from "../context/context-provider.js";

export class InMemoryNovelStore implements NovelApplicationPorts {
  public readonly contextProvider: InMemoryContextProvider;
  private readonly proposals = new Map<string, GoalProposal>();
  private readonly proposalKeys = new Map<string, string>();
  private readonly goals = new Map<string, Goal>();
  private readonly revisions = new Map<string, GoalRevision>();
  private readonly productions = new Map<string, Production>();
  private readonly batches = new Map<string, Batch>();
  private readonly tasks = new Map<string, Task>();
  private readonly contexts = new Map<string, NovelContextPackage>();
  private readonly candidates = new Map<string, CandidateChapter>();
  private readonly reviews = new Map<string, Review>();
  private readonly acceptances = new Map<string, AcceptanceResult>();
  private readonly canonical = new Map<string, CanonMergeResult>();
  private readonly projections = new Map<string, WorkflowProjection>();
  private nextId = 0;
  private commandQueue: Promise<void> = Promise.resolve();

  private enqueue<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.commandQueue.then(operation);
    this.commandQueue = result.then(() => undefined, () => undefined);
    return result;
  }

  public constructor(contextProvider = new InMemoryContextProvider()) {
    this.contextProvider = contextProvider;
  }

  public async createProject(input: {
    projectId: string;
    userId: string;
    canonSnapshot?: string;
    characterStates?: readonly NovelProjectSnapshot["characterStates"][number][];
    foreshadowEntries?: readonly NovelProjectSnapshot["foreshadowEntries"][number][];
  }): Promise<NovelProjectSnapshot> {
    return this.contextProvider.createProject(input);
  }

  public async loadForConversation(input: { actor: ActorScope; correlationId: string }): Promise<NovelProjectSnapshot> {
    return this.contextProvider.loadForConversation(input);
  }

  public async freezeForChapter(input: {
    actor: ActorScope;
    correlationId: string;
    goalRevisionId: string;
    taskId: string;
  }): Promise<NovelContextPackage> {
    const packageValue = await this.contextProvider.freezeForChapter(input);
    this.contexts.set(packageValue.taskId, clone(packageValue));
    return clone(packageValue);
  }

  public async saveProposal(proposal: GoalProposal, idempotencyKey: string): Promise<GoalProposal> {
    return this.enqueue(() => this.saveProposalUnsafe(proposal, idempotencyKey));
  }

  private async saveProposalUnsafe(proposal: GoalProposal, idempotencyKey: string): Promise<GoalProposal> {
    const key = this.key(proposal.projectId, proposal.createdBy, idempotencyKey);
    const existingId = this.proposalKeys.get(key);
    if (existingId) {
      const existing = this.proposals.get(existingId);
      if (!existing) throw new NovelCommandError("conflict", "Proposal idempotency record is inconsistent.");
      if (existing.contentHash !== proposal.contentHash) {
        throw new NovelCommandError("conflict", "Idempotency key was already used for another proposal.");
      }
      return clone(existing);
    }
    if (this.proposals.has(proposal.proposalId)) {
      throw new NovelCommandError("conflict", `Proposal already exists: ${proposal.proposalId}`);
    }
    await this.assertProjectOwner(proposal.projectId, proposal.createdBy);
    this.proposals.set(proposal.proposalId, clone(proposal));
    this.proposalKeys.set(key, proposal.proposalId);
    this.refreshProjection({ projectId: proposal.projectId, userId: proposal.createdBy });
    return clone(proposal);
  }

  public async getProposal(proposalId: string, actor: ActorScope): Promise<GoalProposal | null> {
    const proposal = this.proposals.get(proposalId);
    if (!proposal) return null;
    this.assertActorProject(actor, proposal.projectId, proposal.createdBy);
    return clone(proposal);
  }

  public async findProposalByIdempotency(actor: ActorScope, idempotencyKey: string): Promise<GoalProposal | null> {
    const id = this.proposalKeys.get(this.key(actor.projectId, actor.userId, idempotencyKey));
    return id ? clone(this.proposals.get(id) as GoalProposal) : null;
  }

  public async confirmGoal(input: ConfirmGoalCommand): Promise<GoalCommitResult> {
    return this.enqueue(() => this.confirmGoalUnsafe(input));
  }

  private async confirmGoalUnsafe(input: ConfirmGoalCommand): Promise<GoalCommitResult> {
    const existing = this.findGoalCommit(input);
    if (existing) {
      if (existing.goal.proposalId !== input.proposalId) {
        throw new NovelCommandError("conflict", "Idempotency key was already used for another goal confirmation.");
      }
      return existing;
    }
    const proposal = this.proposals.get(input.proposalId);
    if (!proposal) throw new NovelCommandError("not_found", `Proposal not found: ${input.proposalId}`);
    this.assertActorProject(input.actor, proposal.projectId, proposal.createdBy);
    const current = [...this.goals.values()].find((goal) => goal.projectId === input.actor.projectId);
    if (current) throw new NovelCommandError("conflict", "This project already has a confirmed Goal.");

    const id = (prefix: string): string => `${prefix}-${++this.nextId}`;
    const now = new Date(0).toISOString();
    const goal: Goal = {
      goalId: id("goal"), projectId: input.actor.projectId, userId: input.actor.userId,
      status: "confirmed", proposalId: proposal.proposalId, currentRevision: 1, createdAt: now,
    };
    const goalRevision: GoalRevision = {
      goalRevisionId: id("goal-revision"), goalId: goal.goalId, projectId: goal.projectId, userId: goal.userId,
      revision: 1, chapterNumber: proposal.chapterNumber, chapterBrief: proposal.chapterBrief,
      acceptanceCriteria: clone(proposal.acceptanceCriteria), sourceMessageIds: clone(proposal.sourceMessageIds),
      proposalContentHash: proposal.contentHash, createdAt: now,
    };
    const production: Production = {
      productionId: id("production"), goalId: goal.goalId, goalRevisionId: goalRevision.goalRevisionId,
      projectId: goal.projectId, userId: goal.userId, status: "running", currentBatchId: "pending", createdAt: now,
    };
    const batch: Batch = {
      batchId: id("batch"), productionId: production.productionId, goalRevisionId: goalRevision.goalRevisionId,
      projectId: goal.projectId, userId: goal.userId, status: "running", createdAt: now,
    };
    const task: Task = {
      taskId: id("task"), batchId: batch.batchId, productionId: production.productionId,
      goalRevisionId: goalRevision.goalRevisionId, projectId: goal.projectId, userId: goal.userId,
      chapterNumber: proposal.chapterNumber, status: "queued", taskVersion: 1, createdAt: now,
    };
    const completeProduction = { ...production, currentBatchId: batch.batchId };
    this.goals.set(goal.goalId, goal);
    this.revisions.set(goalRevision.goalRevisionId, goalRevision);
    this.productions.set(production.productionId, completeProduction);
    this.batches.set(batch.batchId, batch);
    this.tasks.set(task.taskId, task);

    const snapshot = await this.contextProvider.loadForConversation({ actor: input.actor, correlationId: input.correlationId });
    this.contextProvider.replaceProjectSnapshot({
      ...snapshot,
      versionVector: { ...snapshot.versionVector, goalRevision: goalRevision.revision },
    });
    const contextPackage = await this.freezeForChapter({
      actor: input.actor, correlationId: input.correlationId,
      goalRevisionId: goalRevision.goalRevisionId, taskId: task.taskId,
    });
    const result: GoalCommitResult = {
      goal, goalRevision, production: completeProduction, batch, task, contextPackage,
    };
    this.recordGoalCommit(input, result);
    this.refreshProjection(input.actor);
    return clone(result);
  }

  public async getGoal(goalId: string, actor: ActorScope): Promise<Goal | null> {
    const value = this.goals.get(goalId); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }
  public async getGoalRevision(id: string, actor: ActorScope): Promise<GoalRevision | null> {
    const value = this.revisions.get(id); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }
  public async getProduction(id: string, actor: ActorScope): Promise<Production | null> {
    const value = this.productions.get(id); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }
  public async getBatch(id: string, actor: ActorScope): Promise<Batch | null> {
    const value = this.batches.get(id); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }
  public async getTask(id: string, actor: ActorScope): Promise<Task | null> {
    const value = this.tasks.get(id); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }

  public async submitCandidateIntent(input: ChapterCandidateIntent): Promise<CandidateChapter> {
    return this.enqueue(() => this.submitCandidateIntentUnsafe(input));
  }

  private async submitCandidateIntentUnsafe(input: ChapterCandidateIntent): Promise<CandidateChapter> {
    const task = this.tasks.get(input.taskId);
    if (!task) throw new NovelCommandError("not_found", `Task not found: ${input.taskId}`);
    this.assertActorProject({ projectId: input.projectId, userId: input.userId }, task.projectId, task.userId);
    if (task.status !== "queued" && task.status !== "running") {
      throw new NovelCommandError("invalid_transition", `Task cannot accept a candidate from ${task.status}.`);
    }
    const context = this.contexts.get(task.taskId);
    if (!context) throw new NovelCommandError("invalid_transition", "Task has no frozen context package.");
    const existing = [...this.candidates.values()].find((candidate) => candidate.taskId === task.taskId);
    if (existing) return clone(existing);
    const candidate: CandidateChapter = {
      candidateId: `candidate-${++this.nextId}`, taskId: task.taskId, projectId: input.projectId, userId: input.userId,
      chapterNumber: input.chapterNumber, title: input.title, body: input.body,
      contextPackageHash: input.contextPackageHash, modelProfile: input.modelProfile, candidateVersion: 1,
      status: "generated", referencedCharacterIds: clone(input.referencedCharacterIds),
      updatedForeshadowEntryIds: clone(input.updatedForeshadowEntryIds), characterUpdates: clone(input.characterUpdates),
      foreshadowUpdates: clone(input.foreshadowUpdates), createdAt: new Date(0).toISOString(),
    };
    this.candidates.set(candidate.candidateId, candidate);
    this.tasks.set(task.taskId, { ...task, status: "running" });
    this.refreshProjection({ projectId: candidate.projectId, userId: candidate.userId });
    return clone(candidate);
  }

  public async saveCandidate(candidate: CandidateChapter): Promise<CandidateChapter> {
    this.candidates.set(candidate.candidateId, clone(candidate)); return clone(candidate);
  }
  public async getCandidate(id: string, actor: ActorScope): Promise<CandidateChapter | null> {
    const value = this.candidates.get(id); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }
  public async saveReview(review: Review): Promise<Review> {
    return this.enqueue(() => this.saveReviewUnsafe(review));
  }

  private async saveReviewUnsafe(review: Review): Promise<Review> {
    const candidate = this.candidates.get(review.candidateId);
    if (!candidate) throw new NovelCommandError("not_found", `Candidate not found: ${review.candidateId}`);
    this.assertActorProject({ projectId: review.projectId, userId: review.userId }, candidate.projectId, candidate.userId);
    if (candidate.status !== "generated") {
      throw new NovelCommandError("invalid_transition", `Candidate cannot be reviewed from ${candidate.status}.`);
    }
    this.reviews.set(review.candidateId, clone(review));
    const task = this.tasks.get(candidate.taskId);
    if (task) this.tasks.set(task.taskId, { ...task, status: review.passed ? "awaiting_acceptance" : "blocked", taskVersion: task.taskVersion + 1 });
    const production = task ? this.productions.get(task.productionId) : undefined;
    if (production) this.productions.set(production.productionId, { ...production, status: review.passed ? "awaiting_acceptance" : "blocked" });
    this.candidates.set(candidate.candidateId, { ...candidate, status: review.passed ? "awaiting_acceptance" : "blocked" });
    this.refreshProjection({ projectId: candidate.projectId, userId: candidate.userId });
    return clone(review);
  }
  public async getReview(candidateId: string, actor: ActorScope): Promise<Review | null> {
    const value = this.reviews.get(candidateId); if (!value) return null;
    this.assertActorProject(actor, value.projectId, value.userId); return clone(value);
  }

  public async acceptCandidate(input: AcceptCandidateCommand): Promise<AcceptanceResult> {
    return this.enqueue(() => this.acceptCandidateUnsafe(input));
  }

  private async acceptCandidateUnsafe(input: AcceptCandidateCommand): Promise<AcceptanceResult> {
    const key = this.key(input.actor.projectId, input.actor.userId, input.idempotencyKey);
    const existing = this.acceptances.get(key);
    if (existing) {
      const prior = existing.acceptance;
      if (
        prior.candidateId !== input.candidateId
        || prior.expectedCandidateVersion !== input.expectedCandidateVersion
        || prior.decision !== input.decision
        || prior.contextPackageHash !== input.contextPackageHash
      ) {
        throw new NovelCommandError("conflict", "Idempotency key was already used for another acceptance.");
      }
      return clone(existing);
    }
    const candidate = this.candidates.get(input.candidateId);
    if (!candidate) throw new NovelCommandError("not_found", `Candidate not found: ${input.candidateId}`);
    this.assertActorProject(input.actor, candidate.projectId, candidate.userId);
    if (candidate.candidateVersion !== input.expectedCandidateVersion) throw new NovelCommandError("conflict", "Candidate version is stale.");
    if (candidate.contextPackageHash !== input.contextPackageHash) throw new NovelCommandError("conflict", "Candidate context hash is stale.");
    const review = this.reviews.get(candidate.candidateId);
    if (!review || !review.passed) throw new NovelCommandError("invalid_transition", "Only a passed continuity review can be accepted.");
    if (candidate.status !== "awaiting_acceptance") throw new NovelCommandError("invalid_transition", `Candidate cannot be accepted from ${candidate.status}.`);
    const acceptance: Acceptance = {
      acceptanceId: `acceptance-${++this.nextId}`, candidateId: candidate.candidateId, projectId: candidate.projectId,
      userId: candidate.userId, actorId: input.actorId, decision: input.decision,
      expectedCandidateVersion: input.expectedCandidateVersion, contextPackageHash: input.contextPackageHash,
      idempotencyKey: input.idempotencyKey, createdAt: new Date(0).toISOString(),
    };
    const context = this.contexts.get(candidate.taskId);
    if (!context) throw new NovelCommandError("invalid_transition", "Candidate context package is missing.");

    if (input.decision === "rejected") {
      const result: AcceptanceResult = { acceptance, canonMerge: null };
      this.candidates.set(candidate.candidateId, { ...candidate, status: "rejected" });
      const task = this.tasks.get(candidate.taskId);
      if (task) {
        this.tasks.set(task.taskId, { ...task, status: "blocked", taskVersion: task.taskVersion + 1 });
        const production = this.productions.get(task.productionId);
        if (production) this.productions.set(production.productionId, { ...production, status: "blocked" });
        const batch = this.batches.get(task.batchId);
        if (batch) this.batches.set(batch.batchId, { ...batch, status: "blocked" });
      }
      this.acceptances.set(key, clone(result));
      this.refreshProjection(input.actor);
      return clone(result);
    }

    const reservation: AcceptanceResult = { acceptance, canonMerge: null };
    this.acceptances.set(key, clone(reservation));
    try {
      const canonMerge = await this.mergeAcceptedCandidateUnsafe({
        actor: input.actor,
        candidate,
        contextPackage: context,
        acceptance,
      });
      const result: AcceptanceResult = { acceptance, canonMerge };
      this.acceptances.set(key, clone(result));
      this.refreshProjection(input.actor);
      return clone(result);
    } catch (error) {
      this.acceptances.delete(key);
      this.refreshProjection(input.actor);
      throw error;
    }
  }

  public async mergeAcceptedCandidate(input: {
    actor: ActorScope;
    candidate: CandidateChapter;
    contextPackage: NovelContextPackage;
    acceptance: Acceptance;
  }): Promise<CanonMergeResult> {
    return this.enqueue(() => this.mergeAcceptedCandidateUnsafe(input));
  }

  private async mergeAcceptedCandidateUnsafe(input: {
    actor: ActorScope;
    candidate: CandidateChapter;
    contextPackage: NovelContextPackage;
    acceptance: Acceptance;
  }): Promise<CanonMergeResult> {
    const candidate = this.candidates.get(input.candidate.candidateId);
    if (!candidate) throw new NovelCommandError("not_found", "Candidate not found.");
    this.assertActorProject(input.actor, candidate.projectId, candidate.userId);
    if (input.acceptance.decision !== "accepted") {
      throw new NovelCommandError("invalid_transition", "Canon merge requires an accepted acceptance record.");
    }
    const persistedAcceptance = [...this.acceptances.values()]
      .find((value) => value.acceptance.candidateId === candidate.candidateId)?.acceptance;
    if (!persistedAcceptance || persistedAcceptance.acceptanceId !== input.acceptance.acceptanceId || persistedAcceptance.decision !== "accepted") {
      throw new NovelCommandError("invalid_transition", "Canon merge requires the persisted accepted acceptance.");
    }
    const prior = this.canonical.get(candidate.candidateId);
    if (prior) return clone(prior);
    if (candidate.status !== "awaiting_acceptance") throw new NovelCommandError("invalid_transition", "Candidate is not awaiting acceptance.");
    if (candidate.contextPackageHash !== input.contextPackage.contentHash || input.acceptance.contextPackageHash !== input.contextPackage.contentHash) {
      throw new NovelCommandError("conflict", "Canon merge context hash does not match frozen package.");
    }
    const snapshot = await this.contextProvider.loadForConversation({ actor: input.actor, correlationId: "canon-merge" });
    const characters = snapshot.characterStates.map((state) => {
      const update = candidate.characterUpdates.find((item) => item.characterId === state.characterId);
      return update ? { ...state, summary: update.summary, status: update.status, version: state.version + 1, updatedFromCandidateId: candidate.candidateId, updatedFromContextPackageHash: input.contextPackage.contentHash } : state;
    });
    const foreshadows = snapshot.foreshadowEntries.map((entry) => {
      const update = candidate.foreshadowUpdates.find((item) => item.entryId === entry.entryId);
      return update ? { ...entry, status: update.status, description: update.description, version: entry.version + 1, updatedFromCandidateId: candidate.candidateId, updatedFromContextPackageHash: input.contextPackage.contentHash } : entry;
    });
    const canonicalChapter = {
      canonicalChapterId: `canon-${++this.nextId}`, projectId: candidate.projectId, userId: candidate.userId,
      candidateId: candidate.candidateId, candidateVersion: candidate.candidateVersion, chapterNumber: candidate.chapterNumber,
      title: candidate.title, body: candidate.body, contextPackageHash: candidate.contextPackageHash, createdAt: new Date(0).toISOString(),
    };
    const result: CanonMergeResult = { canonicalChapter, characterStates: characters, foreshadowEntries: foreshadows, projectionVersion: 1 };
    this.canonical.set(candidate.candidateId, clone(result));
    this.candidates.set(candidate.candidateId, { ...candidate, status: "merged" });
    const task = this.tasks.get(candidate.taskId);
    if (task) {
      this.tasks.set(task.taskId, { ...task, status: "completed", taskVersion: task.taskVersion + 1 });
      const production = this.productions.get(task.productionId);
      if (production) this.productions.set(production.productionId, { ...production, status: "completed" });
      const batch = this.batches.get(task.batchId);
      if (batch) this.batches.set(batch.batchId, { ...batch, status: "completed" });
    }
    this.contextProvider.replaceProjectSnapshot({
      ...snapshot, canonSnapshot: `${snapshot.canonSnapshot}${snapshot.canonSnapshot ? "\n" : ""}${candidate.title}\n${candidate.body}`,
      characterStates: characters, foreshadowEntries: foreshadows,
      sourceReferences: [...snapshot.sourceReferences, { sourceId: candidate.candidateId, sourceType: "canon", version: candidate.candidateVersion }],
      versionVector: { ...snapshot.versionVector, canon: snapshot.versionVector.canon + 1 }, previousChapterNumber: candidate.chapterNumber,
    });
    this.refreshProjection(input.actor);
    return clone(result);
  }

  public async markTaskFailed(input: { actor: ActorScope; taskId: string; correlationId: string }): Promise<Task> {
    return this.enqueue(async () => {
      const task = this.tasks.get(input.taskId);
      if (!task) throw new NovelCommandError("not_found", `Task not found: ${input.taskId}`);
      this.assertActorProject(input.actor, task.projectId, task.userId);
      if (task.status === "completed" || task.status === "blocked") {
        throw new NovelCommandError("invalid_transition", `Task cannot fail from ${task.status}.`);
      }
      const failed = { ...task, status: "failed" as const, taskVersion: task.taskVersion + 1 };
      this.tasks.set(task.taskId, failed);
      const production = this.productions.get(task.productionId);
      if (production) this.productions.set(production.productionId, { ...production, status: "failed" });
      const batch = this.batches.get(task.batchId);
      if (batch) this.batches.set(batch.batchId, { ...batch, status: "failed" });
      this.refreshProjection(input.actor);
      return clone(failed);
    });
  }

  public async get(request: { actor: ActorScope }): Promise<WorkflowProjection> {
    await this.contextProvider.loadForConversation({ actor: request.actor, correlationId: "workflow-query" });
    return clone(this.projections.get(request.actor.projectId) ?? this.emptyProjection(request.actor));
  }

  private refreshProjection(actor: ActorScope): void {
    const proposal = [...this.proposals.values()].find((value) => value.projectId === actor.projectId && value.createdBy === actor.userId) ?? null;
    const goal = [...this.goals.values()].find((value) => value.projectId === actor.projectId && value.userId === actor.userId) ?? null;
    const revision = goal ? this.revisions.get([...this.revisions.keys()].find((id) => this.revisions.get(id)?.goalId === goal.goalId) ?? "") ?? null : null;
    const production = goal ? [...this.productions.values()].find((value) => value.goalId === goal.goalId) ?? null : null;
    const batch = production ? this.batches.get(production.currentBatchId) ?? null : null;
    const task = batch ? [...this.tasks.values()].find((value) => value.batchId === batch.batchId) ?? null : null;
    const context = task ? this.contexts.get(task.taskId) ?? null : null;
    const candidate = task ? [...this.candidates.values()].find((value) => value.taskId === task.taskId) ?? null : null;
    const review = candidate ? this.reviews.get(candidate.candidateId) ?? null : null;
    const acceptance = candidate ? [...this.acceptances.values()].find((value) => value.acceptance.candidateId === candidate.candidateId)?.acceptance ?? null : null;
    const canon = candidate ? this.canonical.get(candidate.candidateId) ?? null : null;
    this.projections.set(actor.projectId, {
      projectId: actor.projectId, userId: actor.userId, proposal, goal, goalRevision: revision, production, batch, task,
      contextPackage: context, candidate, review, acceptance, canonicalChapter: canon?.canonicalChapter ?? null,
      characterStates: canon?.characterStates ?? (task ? [...(this.contexts.get(task.taskId)?.characterStates ?? [])] : []),
      foreshadowEntries: canon?.foreshadowEntries ?? (task ? [...(this.contexts.get(task.taskId)?.foreshadowEntries ?? [])] : []),
      projectionVersion: canon?.projectionVersion ?? 0, sourceCandidateId: canon?.canonicalChapter.candidateId ?? null,
      sourceCandidateVersion: canon?.canonicalChapter.candidateVersion ?? null, sourceContextPackageHash: canon?.canonicalChapter.contextPackageHash ?? null,
    });
  }

  private emptyProjection(actor: ActorScope): WorkflowProjection {
    return { projectId: actor.projectId, userId: actor.userId, proposal: null, goal: null, goalRevision: null, production: null, batch: null, task: null, contextPackage: null, candidate: null, review: null, acceptance: null, canonicalChapter: null, characterStates: [], foreshadowEntries: [], projectionVersion: 0, sourceCandidateId: null, sourceCandidateVersion: null, sourceContextPackageHash: null };
  }
  private recordGoalCommit(input: ConfirmGoalCommand, result: GoalCommitResult): void {
    this.confirmationResults.set(this.key(input.actor.projectId, input.actor.userId, input.idempotencyKey), clone(result));
  }
  private readonly confirmationResults = new Map<string, GoalCommitResult>();
  private findGoalCommit(input: ConfirmGoalCommand): GoalCommitResult | null {
    const value = this.confirmationResults.get(this.key(input.actor.projectId, input.actor.userId, input.idempotencyKey));
    return value ? clone(value) : null;
  }
  private key(projectId: string, userId: string, idempotencyKey: string): string { return `${projectId}:${userId}:${idempotencyKey}`; }
  private async assertProjectOwner(projectId: string, userId: string): Promise<void> {
    await this.contextProvider.loadForConversation({ actor: { projectId, userId }, correlationId: "scope" });
  }
  private assertActorProject(actor: ActorScope, projectId: string, userId: string): void {
    if (actor.projectId !== projectId || actor.userId !== userId) throw new NovelCommandError("forbidden", "Actor is not authorized for this project resource.");
  }
}
