import assert from "node:assert/strict";
import test, { describe } from "node:test";
import { createAssistantMessageEventStream, type AssistantMessage, type AssistantMessageEvent, type Context, type Message, type Model } from "@mariozechner/pi-ai";
import type { AspNetAgentApi, ActivationRequest, ConversationRuntimeRequest, ConversationRuntimeResponse, ProjectCatalogItem } from "../src/contracts.js";
import { NovelPiRuntime, type RuntimeStreamEvent } from "../src/runtime.js";

const fakeModel: Model<"openai-completions"> = {
  id: "test-model",
  provider: "test-provider",
  api: "openai-completions",
  baseUrl: "https://example.invalid/v1",
  contextWindow: 128_000,
  maxTokens: 4_096,
} as unknown as Model<"openai-completions">;

function usage(): AssistantMessage["usage"] {
  return { input: 1, output: 1, cacheRead: 0, cacheWrite: 0, totalTokens: 2, cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 } };
}

function basePartial(): AssistantMessage {
  return {
    role: "assistant",
    content: [],
    api: "openai-completions",
    provider: "test-provider",
    model: "test-model",
    usage: usage(),
    stopReason: "stop",
    timestamp: Date.now(),
  };
}

function textTurn(text: string) {
  return (llmContext: Context): AssistantMessageEventStreamLike => {
    const stream = createAssistantMessageEventStream();
    const partial = basePartial();
    const final: AssistantMessage = {
      ...partial,
      content: [{ type: "text", text }],
      stopReason: "stop",
    };
    stream.push({ type: "start", partial });
    stream.push({ type: "text_start", contentIndex: 0, partial });
    stream.push({ type: "text_delta", contentIndex: 0, delta: text, partial });
    stream.push({ type: "text_end", contentIndex: 0, content: text, partial: { ...partial, content: [{ type: "text", text }] } });
    stream.push({ type: "done", reason: "stop", message: final });
    stream.end(final);
    void llmContext;
    return stream as AssistantMessageEventStreamLike;
  };
}

function toolCallTurn(toolName: string, args: Record<string, unknown>, followUpText: string) {
  return (): AssistantMessageEventStreamLike => {
    const stream = createAssistantMessageEventStream();
    const partial = basePartial();
    const toolCall = { type: "toolCall" as const, id: "tc_1", name: toolName, arguments: args };
    const withCall: AssistantMessage = { ...partial, content: [toolCall] };
    const final: AssistantMessage = {
      ...withCall,
      content: [toolCall, { type: "text", text: followUpText }],
      stopReason: "toolUse",
    };
    stream.push({ type: "start", partial });
    stream.push({ type: "toolcall_start", contentIndex: 0, partial: withCall });
    stream.push({ type: "toolcall_end", contentIndex: 0, toolCall, partial: withCall });
    stream.push({ type: "done", reason: "toolUse", message: final });
    stream.end(final);
    return stream as AssistantMessageEventStreamLike;
  };
}

type AssistantMessageEventStreamLike = AsyncIterable<AssistantMessageEvent> & { end(result?: AssistantMessage): void; result(): Promise<AssistantMessage> };

interface RecordedApi {
  api: AspNetAgentApi;
  loadContextCalls: number;
  listProjectsCalls: number;
  activations: ActivationRequest[];
  llmContexts: Context[];
}

function recordingApi(options: {
  loadContext?: Pick<ConversationRuntimeRequest, "binding" | "systemInstructions" | "projectContext" | "allowedProjectTools">;
  activationResult?: Partial<ActivationResultShape>;
} = {}): RecordedApi {
  const recorded: RecordedApi = {
    api: undefined as unknown as AspNetAgentApi,
    loadContextCalls: 0,
    listProjectsCalls: 0,
    activations: [],
    llmContexts: [],
  };
  const defaultLoad = {
    binding: { state: "unbound" as const, version: "1" },
    systemInstructions: "sys",
    projectContext: undefined,
    allowedProjectTools: [],
  };
  recorded.api = {
    async loadContext(request) {
      recorded.loadContextCalls++;
      return options.loadContext ?? defaultLoad;
    },
    async listAccessibleProjects(): Promise<ProjectCatalogItem[]> {
      recorded.listProjectsCalls++;
      return [{ projectId: "p1", title: "Project One", status: "active", updatedAt: "2026-08-21T00:00:00Z" }];
    },
    async activateProjectContext(_request, activation) {
      recorded.activations.push(activation);
      const result = { code: "activated", projectId: activation.projectId, bindingVersion: "2", ...(options.activationResult ?? {}) };
      return result as ActivationResultShape;
    },
  };
  return recorded;
}

type ActivationResultShape = Awaited<ReturnType<AspNetAgentApi["activateProjectContext"]>>;

function scriptedRuntime(api: RecordedApi, turns: Array<(ctx: Context) => AssistantMessageEventStreamLike>, sourceUserMessageId?: string): {
  runtime: NovelPiRuntime;
  run: (request: Partial<ConversationRuntimeRequest>) => Promise<{ response: ConversationRuntimeResponse; events: RuntimeStreamEvent[] }>;
} {
  const streamFn: NovelPiRuntimeOptionsStreamFn = ((model: Model<any>, context: Context) => {
    api.llmContexts.push(context);
    const next = turns.shift();
    assert.ok(next, "scripted turn missing");
    return next(context);
  }) as NovelPiRuntimeOptionsStreamFn;
  const runtime = new NovelPiRuntime({
    model: fakeModel,
    api: api.api,
    getApiKey: async () => "test-key",
    discoverySkill: "DISCOVERY_SKILL",
    ...(streamFn ? { streamFn } : {}),
  });
  return {
    runtime,
    async run(request: Partial<ConversationRuntimeRequest>) {
      const events: RuntimeStreamEvent[] = [];
      const full: ConversationRuntimeRequest = {
        userId: "user-1",
        sessionId: "session-1",
        message: "hello",
        attachmentIds: [],
        correlationId: "corr-1",
        ...(sourceUserMessageId ? { sourceUserMessageId } : {}),
        binding: { state: "unbound", version: "1" },
        systemInstructions: "sys",
        durableMessages: [],
        allowedProjectTools: [],
        ...request,
      };
      const response = await runtime.run(full, (event) => events.push(event));
      return { response, events };
    },
  };
}

type NovelPiRuntimeOptionsStreamFn = ConstructorParameters<typeof NovelPiRuntime>[0]["streamFn"];

describe("NovelPiRuntime", () => {
  test("unbound conversation completes with natural stop and durable assistant envelope", async () => {
    const api = recordingApi();
    const { run } = scriptedRuntime(api, [textTurn("Hello! How can I help?")]);
    const { response, events } = await run({});

    assert.equal(response.assistantMessage, "Hello! How can I help?");
    assert.equal(response.decisionKind, "conversation");
    assert.deepEqual(response.tokenDeltas, ["Hello! How can I help?"]);
    assert.equal(response.messages.length, 1);
    const assistant = response.messages[0]!;
    assert.equal(assistant.role, "assistant");
    assert.equal(assistant.customType, "pi.assistant.v1");
    assert.equal(response.checkpoint.runtime, "pi-agent-core");
    assert.ok(events.some((event) => event.type === "completed"));
    assert.equal(api.listProjectsCalls, 0);
    assert.equal(api.activations.length, 0);
    // The system prompt carries the discovery skill and unbound state.
    const system = api.llmContexts[0]?.systemPrompt ?? "";
    assert.ok(system.includes("DISCOVERY_SKILL"));
    assert.ok(system.includes("Unbound"));
  });

  test("activation without persisted confirmation source returns recoverable confirmation_required", async () => {
    const api = recordingApi();
    const { run } = scriptedRuntime(api, [
      toolCallTurn("activate_project_context", { projectId: "p1", confirmed: true }, "Let me check."),
      textTurn("I need your explicit confirmation."),
    ]);
    const { response } = await run({});

    const toolResult = response.messages.find((message) => message.role === "toolResult");
    assert.ok(toolResult, "toolResult envelope expected");
    assert.equal(toolResult.isError, false);
    assert.ok(toolResult.content.includes("confirmation_required"));
    assert.equal(api.activations.length, 0, "activation must not reach ASP.NET without confirmation provenance");
    assert.equal(api.loadContextCalls, 0, "binding must not refresh on confirmation_required");
  });

  test("activation with confirmation provenance binds, refreshes context, and swaps tools", async () => {
    const api = recordingApi({
      loadContext: {
        binding: { state: "bound", projectId: "p1", version: "2" },
        systemInstructions: "sys-bound",
        projectContext: { projectId: "p1" },
        allowedProjectTools: [],
      },
    });
    const { run } = scriptedRuntime(api, [
      toolCallTurn("activate_project_context", { projectId: "p1", confirmed: true }, "Bound."),
      textTurn("Ready to work on Project One."),
    ], "msg-42");
    const { response } = await run({});

    assert.equal(api.activations.length, 1);
    const activation = api.activations[0]!;
    assert.equal(activation.projectId, "p1");
    assert.equal(activation.confirmationSource, "conversation_user_message:msg-42");
    assert.equal(activation.expectedBindingVersion, "1");
    assert.ok(activation.idempotencyKey.includes("session-1"));
    assert.ok(activation.idempotencyKey.includes("msg-42"));

    // Binding refreshed in place and the tool set swapped to the bound set.
    const toolNamesSecondTurn = (api.llmContexts[1]?.tools ?? []).map((tool) => tool.name);
    assert.deepEqual(toolNamesSecondTurn, ["get_project_context"]);
    assert.equal(api.loadContextCalls, 1);

    const toolResult = response.messages.find((message) => message.role === "toolResult");
    assert.ok(toolResult);
    assert.ok(toolResult.content.includes("activated"));
    assert.equal(toolResult.isError, false);
    // Durable transcript preserves the assistant tool-call envelope.
    const assistant = response.messages.find((message) => message.role === "assistant");
    assert.ok(assistant);
    assert.ok(assistant.content.includes("activate_project_context"));
  });

  test("unbound conversation exposes discovery and activation tools only", async () => {
    const api = recordingApi();
    const { run } = scriptedRuntime(api, [textTurn("ok")]);
    await run({});
    const toolNames = (api.llmContexts[0]?.tools ?? []).map((tool) => tool.name);
    assert.deepEqual(toolNames, ["list_accessible_projects", "activate_project_context"]);
  });
});
