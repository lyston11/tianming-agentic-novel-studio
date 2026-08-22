import { deepEqual, ok, strictEqual } from "node:assert";
import test from "node:test";
import { Type } from "@sinclair/typebox";
import {
  createAssistantMessageEventStream,
  type Api,
  type AssistantMessage,
  type Context,
  type Model,
  type SimpleStreamOptions,
  type StreamFunction,
} from "@tianming/agent-ai";
import {
  ABORTED_SKIPPED_TEXT,
  AgentCore,
  STEERING_SKIPPED_TEXT,
  type AgentCoreEvent,
  type AgentCoreTool,
} from "../src/index.js";

const fakeModel: Model<Api> = {
  id: "fake-model",
  name: "Fake Model",
  api: "openai-completions",
  provider: "fake",
  baseUrl: "http://127.0.0.1:9",
  reasoning: false,
  input: ["text"],
  cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
  contextWindow: 1024,
  maxTokens: 1024,
};

function assistant(
  content: AssistantMessage["content"],
  stopReason: AssistantMessage["stopReason"] = "stop",
): AssistantMessage {
  return {
    role: "assistant",
    content,
    api: fakeModel.api,
    provider: fakeModel.provider,
    model: fakeModel.id,
    usage: {
      input: 0,
      output: 0,
      cacheRead: 0,
      cacheWrite: 0,
      totalTokens: 0,
      cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, total: 0 },
    },
    stopReason,
    timestamp: 0,
  };
}

function textAssistant(text: string): AssistantMessage {
  return assistant([{ type: "text", text }]);
}

function toolCallAssistant(calls: { id: string; name: string; arguments?: Record<string, unknown> }[]): AssistantMessage {
  return assistant(
    calls.map((call) => ({
      type: "toolCall" as const,
      id: call.id,
      name: call.name,
      arguments: call.arguments ?? {},
    })),
    "toolUse",
  );
}

interface ScriptHarness {
  core: AgentCore;
  modelCalls: Context[];
}

/**
 * Deterministic stream function: the n-th model call replays script[n] as
 * synthetic assistant stream events. No network, no provider.
 */
function scriptedAgent(script: AssistantMessage[], options?: Partial<ConstructorParameters<typeof AgentCore>[0]> & { tools?: AgentCoreTool[]; maxTurns?: number }): ScriptHarness {
  const modelCalls: Context[] = [];
  let callIndex = 0;
  const streamFn: StreamFunction<Api, SimpleStreamOptions> = (_model, context) => {
    modelCalls.push(context);
    const message = script[callIndex] ?? textAssistant("unexpected extra model call");
    callIndex += 1;
    const stream = createAssistantMessageEventStream();
    queueMicrotask(() => {
      stream.push({ type: "start", partial: assistant([]) });
      for (const part of message.content) {
        if (part.type === "text") {
          stream.push({
            type: "text_delta",
            contentIndex: 0,
            delta: part.text,
            partial: assistant([part]),
          });
        }
      }
      if (message.stopReason === "aborted") {
        stream.push({ type: "error", reason: "aborted", error: message });
      } else {
        stream.push({
          type: "done",
          reason: message.stopReason === "toolUse" ? "toolUse" : "stop",
          message,
        });
      }
    });
    return stream;
  };
  const core = new AgentCore({
    systemPrompt: "test system prompt",
    model: fakeModel,
    streamFn,
    ...options,
  });
  return { core, modelCalls };
}

function makeTool(
  name: string,
  execute: AgentCoreTool["execute"],
  parameters: AgentCoreTool["parameters"] = Type.Object({}),
): AgentCoreTool {
  return { name, description: `${name} tool`, parameters, execute };
}

test("natural stop: assistant response without tool calls ends the run", async () => {
  const { core, modelCalls } = scriptedAgent([textAssistant("hello there")]);

  const result = await core.prompt("hi");

  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 1);
  strictEqual(modelCalls.length, 1);
  strictEqual(core.state.isRunning, false);
  strictEqual(core.state.error, null);
  deepEqual(
    core.state.messages.map((message) => message.role),
    ["user", "assistant"],
  );
  const finalAssistant = core.state.messages[1];
  ok(finalAssistant && finalAssistant.role === "assistant");
  deepEqual(finalAssistant.content, [{ type: "text", text: "hello there" }]);
});

test("tool calls continue the loop; next tool-free turn stops naturally", async () => {
  const executed: unknown[] = [];
  const echo = makeTool(
    "echo",
    (args) => {
      executed.push(args);
      return { echoed: args };
    },
    Type.Object({ value: Type.String() }),
  );
  const { core, modelCalls } = scriptedAgent(
    [toolCallAssistant([{ id: "call-1", name: "echo", arguments: { value: "x" } }]), textAssistant("done")],
    { tools: [echo] },
  );

  const result = await core.prompt("use the tool");

  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 2);
  strictEqual(modelCalls.length, 2);
  deepEqual(executed, [{ value: "x" }]);

  // Second model call must observe the serialized tool result message.
  const secondContext = modelCalls[1];
  ok(secondContext);
  const roles = secondContext.messages.map((message) => message.role);
  deepEqual(roles, ["user", "assistant", "toolResult"]);

  const toolResult = core.state.messages[2];
  ok(toolResult && toolResult.role === "toolResult");
  strictEqual(toolResult.toolName, "echo");
  strictEqual(toolResult.isError, false);
});

test("multiple tool calls execute strictly serially in model order", async () => {
  const timeline: string[] = [];
  const makeTrackedTool = (name: string): AgentCoreTool =>
    makeTool(name, async (_args, options) => {
      timeline.push(`start:${name}`);
      // Yield so a parallel implementation would interleave starts.
      await new Promise<void>((resolve) => setImmediate(resolve));
      timeline.push(`end:${name}`);
      options.onUpdate(name);
      return name;
    });
  const { core } = scriptedAgent(
    [
      toolCallAssistant([
        { id: "tc-1", name: "alpha" },
        { id: "tc-2", name: "beta" },
        { id: "tc-3", name: "gamma" },
      ]),
      textAssistant("all done"),
    ],
    { tools: [makeTrackedTool("alpha"), makeTrackedTool("beta"), makeTrackedTool("gamma")] },
  );

  const result = await core.prompt("run all three");

  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 2);
  // Strict serial order: each tool completes before the next one starts.
  deepEqual(timeline, ["start:alpha", "end:alpha", "start:beta", "end:beta", "start:gamma", "end:gamma"]);
  const roles = core.state.messages.map((message) => message.role);
  deepEqual(roles, ["user", "assistant", "toolResult", "toolResult", "toolResult", "assistant"]);
});

test("tool errors become observable error tool results and the loop stays deterministic", async () => {
  const boom = makeTool("boom", () => {
    throw new Error("boom");
  });
  const { core, modelCalls } = scriptedAgent(
    [toolCallAssistant([{ id: "call-1", name: "boom" }]), textAssistant("recovered")],
    { tools: [boom] },
  );

  const result = await core.prompt("try it");

  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 2);
  strictEqual(modelCalls.length, 2);
  const toolResult = core.state.messages[2];
  ok(toolResult && toolResult.role === "toolResult");
  strictEqual(toolResult.isError, true);
  ok((toolResult.content[0]?.type === "text" && toolResult.content[0].text.includes("boom")) === true);

  // Validation failures follow the same observable error path.
  const strict = makeTool(
    "strict",
    () => "never runs",
    Type.Object({ count: Type.Integer({ minimum: 1 }) }),
  );
  const second = scriptedAgent(
    [toolCallAssistant([{ id: "call-2", name: "strict", arguments: { count: -5 } }]), textAssistant("ok")],
    { tools: [strict] },
  );
  const secondResult = await second.core.prompt("go");
  strictEqual(secondResult.reason, "natural_stop");
  const invalidResult = second.core.state.messages[2];
  ok(invalidResult && invalidResult.role === "toolResult");
  strictEqual(invalidResult.isError, true);
  ok(
    invalidResult.content.some(
      (part) => part.type === "text" && part.text.includes("Invalid arguments"),
    ),
  );
});

test("steering at a tool boundary skips remaining tools and enters the next turn", async () => {
  const executed: string[] = [];
  let coreRef: AgentCore | undefined;
  // Steering is queued while the first tool executes.
  const first = makeTool("first", (_args, options) => {
    executed.push("first");
    coreRef?.steer("change course");
    options.onUpdate({ progress: 1 });
    return "one";
  });
  const second = makeTool("second", () => {
    executed.push("second");
    return "two";
  });
  const { core, modelCalls } = scriptedAgent(
    [
      toolCallAssistant([
        { id: "tc-a", name: "first" },
        { id: "tc-b", name: "second" },
      ]),
      textAssistant("acknowledged"),
    ],
    { tools: [first, second] },
  );
  coreRef = core;

  const result = await core.prompt("run both");

  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 2);
  strictEqual(modelCalls.length, 2);
  deepEqual(executed, ["first"]);

  // Both tool calls keep matching results so the context stays well-formed.
  const skippedResult = core.state.messages[3];
  ok(skippedResult && skippedResult.role === "toolResult");
  strictEqual(skippedResult.toolName, "second");
  strictEqual(skippedResult.isError, true);
  ok(
    skippedResult.content.some((part) => part.type === "text" && part.text === STEERING_SKIPPED_TEXT),
  );

  // The steering user message enters the context before the next model turn.
  const roles = core.state.messages.map((message) => message.role);
  deepEqual(roles, ["user", "assistant", "toolResult", "toolResult", "user", "assistant"]);
  const steeringMessage = core.state.messages[4];
  ok(steeringMessage && steeringMessage.role === "user");
  strictEqual(steeringMessage.content, "change course");
  const secondCallContext = modelCalls[1];
  ok(secondCallContext);
  ok(secondCallContext.messages.some((message) => message.role === "user"));
});

test("follow-up messages are consumed only at natural stop points", async () => {
  const echo = makeTool("echo", () => "ok");
  const { core, modelCalls } = scriptedAgent(
    [
      toolCallAssistant([{ id: "call-1", name: "echo" }]),
      textAssistant("turn two answer"),
      textAssistant("final answer"),
    ],
    { tools: [echo] },
  );

  core.followUp("and then?");
  const result = await core.prompt("start");

  // The follow-up did not preempt the tool turn (turn 1), and it extended the
  // run past the turn-two natural stop point into a third turn.
  strictEqual(result.reason, "natural_stop");
  strictEqual(result.turns, 3);
  strictEqual(modelCalls.length, 3);

  deepEqual(
    core.state.messages.map((message) => message.role),
    ["user", "assistant", "toolResult", "assistant", "user", "assistant"],
  );
  const followUpMessage = core.state.messages[4];
  ok(followUpMessage && followUpMessage.role === "user");
  strictEqual(followUpMessage.content, "and then?");
});

test("abort stops tool work and later turns while retaining completed messages", async () => {
  const executed: string[] = [];
  let coreRef: AgentCore | undefined;
  const aborting = makeTool("aborting", () => {
    executed.push("aborting");
    coreRef?.abort();
    return "a";
  });
  const neverRuns = makeTool("never-runs", () => {
    executed.push("never-runs");
    return "b";
  });
  const { core, modelCalls } = scriptedAgent(
    [
      toolCallAssistant([
        { id: "tc-a", name: "aborting" },
        { id: "tc-b", name: "never-runs" },
      ]),
      textAssistant("should never be reached"),
    ],
    { tools: [aborting, neverRuns] },
  );
  coreRef = core;

  const result = await core.prompt("start");

  strictEqual(result.reason, "aborted");
  strictEqual(modelCalls.length, 1);
  deepEqual(executed, ["aborting"]);
  strictEqual(core.state.isRunning, false);

  const skipped = core.state.messages[3];
  ok(skipped && skipped.role === "toolResult");
  strictEqual(skipped.isError, true);
  ok(skipped.content.some((part) => part.type === "text" && part.text === ABORTED_SKIPPED_TEXT));

  // Completed messages are retained: user + assistant + two tool results.
  strictEqual(core.state.messages.length, 4);
});

test("external AbortSignal stops an in-flight model stream", async () => {
  const controller = new AbortController();
  const streamFn: StreamFunction<Api, SimpleStreamOptions> = (_model, _context, options) => {
    const signal = options?.signal;
    const stream = createAssistantMessageEventStream();
    queueMicrotask(() => {
      stream.push({ type: "start", partial: assistant([]) });
    });
    signal?.addEventListener(
      "abort",
      () => {
        queueMicrotask(() => {
          stream.push({ type: "error", reason: "aborted", error: assistant([], "aborted") });
        });
      },
      { once: true },
    );
    return stream;
  };
  const core = new AgentCore({
    systemPrompt: "sys",
    model: fakeModel,
    streamFn,
  });

  const pending = core.prompt("hi", { signal: controller.signal });
  await new Promise<void>((resolve) => setImmediate(resolve));
  controller.abort();
  const result = await pending;

  strictEqual(result.reason, "aborted");
  strictEqual(result.turns, 1);
  strictEqual(core.state.isRunning, false);
  // The partial assistant turn is retained as the last message.
  const last = core.state.messages.at(-1);
  ok(last && last.role === "assistant");
  strictEqual(last.stopReason, "aborted");
});

test("max turns terminates an unbounded tool loop with max_turns_exceeded", async () => {
  // Every model turn answers with another tool call: an unbounded loop.
  const loopingScript = Array.from({ length: 8 }, (_, i) =>
    toolCallAssistant([{ id: `call-loop-${i}`, name: "loop" }]),
  );
  const loopTool = makeTool("loop", () => "again");
  const { core, modelCalls } = scriptedAgent(loopingScript, { tools: [loopTool], maxTurns: 3 });

  const result = await core.prompt("keep going");

  strictEqual(result.reason, "max_turns_exceeded");
  strictEqual(result.turns, 3);
  strictEqual(modelCalls.length, 3);
  strictEqual(core.state.isRunning, false);
});

test("event order is stable across a complete tool-then-stop run", async () => {
  const worker = makeTool("worker", (_args, options) => {
    options.onUpdate("halfway");
    return "finished";
  });
  const { core } = scriptedAgent(
    [toolCallAssistant([{ id: "tc-1", name: "worker" }]), textAssistant("fin")],
    { tools: [worker] },
  );

  const observed: string[] = [];
  const describe = (event: AgentCoreEvent): string => {
    switch (event.type) {
      case "agent_start":
        return "agent_start";
      case "agent_end":
        return `agent_end:${event.reason}`;
      case "turn_start":
        return `turn_start:${event.turn}`;
      case "turn_end":
        return `turn_end:${event.turn}`;
      case "message_start":
        return `message_start:${event.message.role}`;
      case "message_end":
        return `message_end:${event.message.role}`;
      case "message_update":
        return `message_update:${event.assistantMessageEvent.type}`;
      case "tool_execution_start":
        return `tool_execution_start:${event.toolName}`;
      case "tool_execution_update":
        return `tool_execution_update:${event.toolName}`;
      case "tool_execution_end":
        return `tool_execution_end:${event.toolName}:${event.isError ? "error" : "ok"}`;
    }
  };
  core.subscribe((event) => observed.push(describe(event)));

  const result = await core.prompt("hi");

  strictEqual(result.reason, "natural_stop");
  deepEqual(observed, [
    "agent_start",
    "message_start:user",
    "message_end:user",
    "turn_start:1",
    "message_start:assistant",
    "message_end:assistant",
    "tool_execution_start:worker",
    "tool_execution_update:worker",
    "tool_execution_end:worker:ok",
    "message_start:toolResult",
    "message_end:toolResult",
    "turn_end:1",
    "turn_start:2",
    "message_start:assistant",
    "message_update:text_delta",
    "message_end:assistant",
    "turn_end:2",
    "agent_end:natural_stop",
  ]);
});
