import { AgentCore, type AgentCoreEvent } from "@tianming/agent-core";
import {
  type AcceptCandidateCommand,
  type ActorScope,
  type ConversationTurn,
  type GoalCommitResult,
  type GoalProposal,
  type ConfirmGoalCommand,
  type WorkflowProjection,
  NovelCommandError,
  proposalContentHash,
} from "../contracts.js";
import type { NovelApplicationPorts, RuntimeEventSink } from "../ports.js";
import { chapterWriterRole, assertRoleTools } from "../roles/chapter-writer.js";
import {
  createChapterWriterTools,
  createProposeGoalTool,
} from "../tools/domain-tools.js";
import {
  createFakeChapterWriter,
  createFakeProposalModel,
  type FakeModelScenario,
} from "../runtime/fake-model.js";
import { actorRuntimeContext, RuntimeEventMapper } from "../runtime/event-mapper.js";
import { chapterWritingSkill, renderChapterWriterPrompt } from "../skills/chapter-writing.js";
import { runContinuityGate } from "../domain/continuity-gate.js";

export interface ConversationResult {
  readonly proposal: GoalProposal;
  readonly coreRunReason: "natural_stop" | "aborted" | "max_turns_exceeded" | "error";
}

export interface ChapterRunResult {
  readonly candidate: import("../contracts.js").CandidateChapter | null;
  readonly review: import("../contracts.js").Review | null;
  readonly coreRunReason: ConversationResult["coreRunReason"];
}

export class NovelAgentApplication {
  public constructor(
    private readonly ports: NovelApplicationPorts,
    private readonly runtimeSink: RuntimeEventSink,
  ) {}

  public async handleConversationTurn(input: ConversationTurn): Promise<ConversationResult> {
    const actor: ActorScope = { projectId: input.projectId, userId: input.userId };
    const snapshot = await this.ports.loadForConversation({
      actor,
      correlationId: input.correlationId,
    });
    const chapterNumber = snapshot.previousChapterNumber + 1;
    const idempotencyKey = input.idempotencyKey ?? `turn:${input.conversationId}:${input.correlationId}`;
    const expectedProposalHash = proposalContentHash({
      projectId: input.projectId,
      conversationId: input.conversationId,
      createdBy: input.userId,
      intent: "write_chapter_candidate",
      chapterNumber,
      chapterBrief: input.message.trim(),
      acceptanceCriteria: ["章节号与目标一致", "角色与伏笔引用存在", "正文非空"],
      executionMode: "interactive_batch",
      requiresConfirmation: true,
      sourceMessageIds: [input.correlationId],
      proposalVersion: 1,
    });
    const model = createFakeProposalModel({
      projectId: input.projectId,
      conversationId: input.conversationId,
      chapterNumber,
      chapterBrief: input.message.trim(),
      acceptanceCriteria: ["章节号与目标一致", "角色与伏笔引用存在", "正文非空"],
      sourceMessageIds: [input.correlationId],
      idempotencyKey,
    });
    const core = new AgentCore({
      systemPrompt: "You propose a novel goal; proposal requires explicit user confirmation.",
      model: model.model,
      streamFn: model.streamFn,
      tools: [createProposeGoalTool({
        actor,
        correlationId: input.correlationId,
        contextPort: this.ports,
        proposalPort: this.ports,
        candidatePort: this.ports,
      })],
      maxTurns: 3,
    });
    const mapper = new RuntimeEventMapper(this.runtimeSink);
    const runId = `proposal-run-${input.conversationId}-${input.correlationId}`;
    const pendingEvents: Promise<unknown>[] = [];
    const unsubscribe = core.subscribe((event: AgentCoreEvent) => {
      pendingEvents.push(mapper.map(
        event,
        actorRuntimeContext(actor, "proposal", input.correlationId, runId),
      ));
    });
    const run = await core.prompt(input.message);
    unsubscribe();
    await Promise.all(pendingEvents);
    const proposal = await this.ports.findProposalByIdempotency(actor, idempotencyKey);
    if (!proposal) {
      throw new NovelCommandError("model_error", "Proposal model completed without submitting a proposal.");
    }
    if (proposal.contentHash !== expectedProposalHash) {
      throw new NovelCommandError("conflict", "Proposal idempotency key was reused for different content.");
    }
    return { proposal, coreRunReason: run.reason };
  }

  public async confirmGoal(input: ConfirmGoalCommand): Promise<GoalCommitResult> {
    return this.ports.confirmGoal(input);
  }

  public async runChapterTask(input: {
    actor: ActorScope;
    correlationId: string;
    runId?: string;
    scenario?: FakeModelScenario;
  }): Promise<ChapterRunResult> {
    const workflow = await this.ports.get({ actor: input.actor });
    const task = workflow.task;
    const goalRevision = workflow.goalRevision;
    const contextPackage = workflow.contextPackage;
    const existingCandidate = workflow.candidate;
    if (!task || !goalRevision || !contextPackage) {
      throw new NovelCommandError("invalid_transition", "No confirmed chapter task is ready to run.");
    }
    if (task.status !== "queued" && task.status !== "running") {
      throw new NovelCommandError("invalid_transition", `Task cannot run from ${task.status}.`);
    }
    if (existingCandidate) {
      throw new NovelCommandError("invalid_transition", `Task already has a candidate in ${existingCandidate.status}.`);
    }
    const fakeOptions = { contextPackage, goalRevision };
    const fake = input.scenario === undefined
      ? createFakeChapterWriter(fakeOptions)
      : createFakeChapterWriter({ ...fakeOptions, scenario: input.scenario });
    const tools = createChapterWriterTools({
      actor: input.actor,
      correlationId: input.correlationId,
      contextPort: this.ports,
      proposalPort: this.ports,
      candidatePort: this.ports,
      contextPackage,
    });
    assertRoleTools(chapterWriterRole, tools);
    const core = new AgentCore({
      systemPrompt: `${chapterWriterRole.systemPrompt}\nSkill: ${chapterWritingSkill.name}\nFrozen context: ${renderChapterWriterPrompt(contextPackage, goalRevision.chapterNumber, goalRevision.chapterBrief, goalRevision.acceptanceCriteria)}`,
      model: fake.model,
      streamFn: fake.streamFn,
      tools,
      maxTurns: 3,
    });
    const mapper = new RuntimeEventMapper(this.runtimeSink);
    const runId = input.runId ?? `chapter-run-${task.taskId}-${input.correlationId}`;
    let candidateSubmitted = false;
    let toolExecutionFailed = false;
    const pendingEvents: Promise<unknown>[] = [];
    const unsubscribe = core.subscribe((event: AgentCoreEvent) => {
      if (event.type === "tool_execution_end") {
        toolExecutionFailed ||= event.isError;
        candidateSubmitted ||= event.toolName === "request_chapter" && !event.isError;
      }
      pendingEvents.push(mapper.map(
        event,
        actorRuntimeContext(input.actor, task.taskId, input.correlationId, runId),
      ));
    });
    const run = await core.prompt(JSON.stringify({
      chapterNumber: goalRevision.chapterNumber,
      chapterBrief: goalRevision.chapterBrief,
    }));
    unsubscribe();
    await Promise.all(pendingEvents);
    const candidate = (await this.ports.get({ actor: input.actor })).candidate;
    if (toolExecutionFailed || !candidateSubmitted || !candidate) {
      if (toolExecutionFailed || !candidateSubmitted) {
        await this.ports.markTaskFailed({
          actor: input.actor,
          taskId: task.taskId,
          correlationId: input.correlationId,
        });
        throw new NovelCommandError(
          run.reason === "aborted" ? "aborted" : "model_error",
          run.reason === "aborted"
            ? "Chapter generation was aborted."
            : "Chapter model did not submit a valid candidate intent.",
        );
      }
      return { candidate: null, review: null, coreRunReason: run.reason };
    }
    if (run.reason !== "natural_stop") {
      await this.ports.markTaskFailed({
        actor: input.actor,
        taskId: task.taskId,
        correlationId: input.correlationId,
      });
      return { candidate, review: null, coreRunReason: run.reason };
    }
    const review = runContinuityGate({ candidate, goalRevision, contextPackage });
    await this.ports.saveReview(review);
    return {
      candidate: await this.ports.getCandidate(candidate.candidateId, input.actor),
      review,
      coreRunReason: run.reason,
    };
  }

  public async acceptCandidate(input: AcceptCandidateCommand): Promise<import("../contracts.js").AcceptanceResult> {
    return this.ports.acceptCandidate(input);
  }

  public async queryWorkflow(actor: ActorScope): Promise<WorkflowProjection> {
    return this.ports.get({ actor });
  }
}
