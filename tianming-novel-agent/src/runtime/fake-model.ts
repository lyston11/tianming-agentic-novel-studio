import type {
  AgentCoreOptions,
  AssistantMessage,
  AssistantMessageEvent,
  AssistantMessageEventStream,
  Context,
  Model,
} from "@tianming/agent-core";
import type { ChapterCandidateIntent, NovelContextPackage, GoalRevision } from "../contracts.js";

export type FakeModelScenario = "happy" | "continuity_failure" | "malformed" | "error" | "abort";

export const fakeChapterWriterModel: Model<"openai-completions"> = {
  id: "fake-chapter-writer-v1",
  name: "Deterministic Chapter Writer",
  api: "openai-completions",
  provider: "deterministic-fake",
  baseUrl: "http://127.0.0.1:9",
  reasoning: false,
  input: ["text"],
  cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
  contextWindow: 8192,
  maxTokens: 2048,
};

export interface FakeChapterWriterOptions {
  readonly contextPackage: NovelContextPackage;
  readonly goalRevision: GoalRevision;
  readonly scenario?: FakeModelScenario;
}

export interface FakeChapterWriterHarness {
  readonly streamFn: NonNullable<AgentCoreOptions["streamFn"]>;
  readonly model: Model<"openai-completions">;
  readonly calls: Context[];
  readonly candidateIntent: ChapterCandidateIntent;
}

export function createFakeChapterWriter(options: FakeChapterWriterOptions): FakeChapterWriterHarness {
  const scenario = options.scenario ?? "happy";
  const calls: Context[] = [];
  const characterId = options.contextPackage.characterStates[0]?.characterId ?? "character-missing";
  const foreshadowId = options.contextPackage.foreshadowEntries[0]?.entryId ?? "foreshadow-missing";
  const candidateIntent: ChapterCandidateIntent = {
    taskId: options.contextPackage.taskId,
    projectId: options.contextPackage.projectId,
    userId: options.contextPackage.userId,
    chapterNumber: options.goalRevision.chapterNumber,
    title: `第${options.goalRevision.chapterNumber}章 · ${options.goalRevision.chapterBrief.slice(0, 18)}`,
    body: "夜色沿着城墙缓慢沉降，角色在旧约与新路之间作出选择。这个决定留下了可供后续章节回收的线索。",
    contextPackageHash: options.contextPackage.contentHash,
    modelProfile: fakeChapterWriterModel.id,
    referencedCharacterIds: [scenario === "continuity_failure" ? "unknown-character" : characterId],
    updatedForeshadowEntryIds: [scenario === "continuity_failure" ? "unknown-foreshadow" : foreshadowId],
    characterUpdates: options.contextPackage.characterStates[0]
      ? [{ characterId, summary: "在本章结束时决定继续追查旧约。", status: "active" }]
      : [],
    foreshadowUpdates: options.contextPackage.foreshadowEntries[0]
      ? [{ entryId: foreshadowId, status: "open", description: "旧约的来历仍待揭示。" }]
      : [],
  };

  const streamFn: NonNullable<AgentCoreOptions["streamFn"]> = (_model, context, streamOptions) => {
    calls.push(context);
    const events: AssistantMessageEvent[] = [{ type: "start", partial: assistant([], fakeChapterWriterModel) }];
    if (scenario === "error") {
      events.push({ type: "error", reason: "error", error: assistant([], fakeChapterWriterModel, "error", "deterministic fake model failure") });
      return eventStream(events);
    }
    if (scenario === "abort") {
      events.push({ type: "error", reason: "aborted", error: assistant([], fakeChapterWriterModel, "aborted") });
      return eventStream(events);
    }
    const isFirstCall = calls.length === 1;
    if (isFirstCall) {
      const content: AssistantMessage["content"] = scenario === "malformed"
        ? [{ type: "toolCall", id: "chapter-call-1", name: "request_chapter", arguments: { taskId: options.contextPackage.taskId } }]
        : [{ type: "toolCall", id: "chapter-call-1", name: "request_chapter", arguments: candidateIntent }];
      events.push({ type: "done", reason: "toolUse", message: assistant(content, fakeChapterWriterModel, "toolUse") });
    } else {
      events.push({ type: "done", reason: "stop", message: assistant([{ type: "text", text: "候选章节已提交，等待连续性门禁与人工验收。" }], fakeChapterWriterModel) });
    }
    return eventStream(events);
  };

  return { streamFn, model: fakeChapterWriterModel, calls, candidateIntent };
}

export interface FakeProposalModelInput {
  readonly projectId: string;
  readonly conversationId: string;
  readonly chapterNumber: number;
  readonly chapterBrief: string;
  readonly acceptanceCriteria: readonly string[];
  readonly sourceMessageIds: readonly string[];
  readonly idempotencyKey: string;
}

export interface FakeProposalModelHarness {
  readonly streamFn: NonNullable<AgentCoreOptions["streamFn"]>;
  readonly model: Model<"openai-completions">;
  readonly calls: Context[];
}

export function createFakeProposalModel(input: FakeProposalModelInput): FakeProposalModelHarness {
  const calls: Context[] = [];
  const streamFn: NonNullable<AgentCoreOptions["streamFn"]> = (_model, context) => {
    calls.push(context);
    if (calls.length === 1) {
      return eventStream([
        { type: "start", partial: assistant([], fakeChapterWriterModel) },
        {
          type: "done",
          reason: "toolUse",
          message: assistant([
            {
              type: "toolCall",
              id: "proposal-call-1",
              name: "propose_goal",
              arguments: {
                projectId: input.projectId,
                conversationId: input.conversationId,
                chapterNumber: input.chapterNumber,
                chapterBrief: input.chapterBrief,
                acceptanceCriteria: [...input.acceptanceCriteria],
                sourceMessageIds: [...input.sourceMessageIds],
                idempotencyKey: input.idempotencyKey,
              },
            },
          ], fakeChapterWriterModel, "toolUse"),
        },
      ]);
    }
    return eventStream([
      { type: "start", partial: assistant([], fakeChapterWriterModel) },
      {
        type: "done",
        reason: "stop",
        message: assistant(
          [{ type: "text", text: "目标提案已提交，等待用户确认后再创建生产任务。" }],
          fakeChapterWriterModel,
        ),
      },
    ]);
  };
  return { streamFn, model: fakeChapterWriterModel, calls };
}

function eventStream(events: readonly AssistantMessageEvent[]): AssistantMessageEventStream {
  return {
    async *[Symbol.asyncIterator](): AsyncGenerator<AssistantMessageEvent> {
      for (const event of events) yield event;
    },
  } as unknown as AssistantMessageEventStream;
}

function assistant(
  content: AssistantMessage["content"],
  model: Model<"openai-completions">,
  stopReason: AssistantMessage["stopReason"] = "stop",
  errorMessage?: string,
): AssistantMessage {
  return {
    role: "assistant",
    content,
    api: model.api,
    provider: model.provider,
    model: model.id,
    usage: {
      input: 0,
      output: 0,
      cacheRead: 0,
      cacheWrite: 0,
      totalTokens: 0,
      cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 },
    },
    stopReason,
    ...(errorMessage === undefined ? {} : { errorMessage }),
    timestamp: 0,
  };
}

export function isFakeModelStream(value: unknown): value is AssistantMessageEventStream {
  return typeof value === "object" && value !== null && Symbol.asyncIterator in value;
}
