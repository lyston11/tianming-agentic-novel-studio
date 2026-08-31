import { ok, strictEqual } from "node:assert";
import test from "node:test";
import {
  createAssistantMessageEventStream,
  streamSimple,
  validateToolArguments,
  type AssistantMessage,
  type Model,
  type Api,
} from "../src/index.js";
import { Type } from "@sinclair/typebox";

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

export function fakeAssistant(
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

test("assistant event stream accepts synthetic events without network", async () => {
  const stream = createAssistantMessageEventStream();
  const observedTypes: string[] = [];
  const consuming = (async () => {
    for await (const event of stream) {
      observedTypes.push(event.type);
    }
  })();

  const final = fakeAssistant([{ type: "text", text: "hello" }]);
  stream.push({ type: "start", partial: fakeAssistant([]) });
  stream.push({
    type: "text_delta",
    contentIndex: 0,
    delta: "hello",
    partial: fakeAssistant([{ type: "text", text: "hello" }]),
  });
  stream.push({ type: "done", reason: "stop", message: final });

  const result = await stream.result();
  await consuming;

  strictEqual(result.stopReason, "stop");
  strictEqual(result.content[0]?.type, "text");
  ok(observedTypes.includes("start"));
  ok(observedTypes.includes("text_delta"));
  ok(observedTypes.includes("done"));
});

test("streamSimple controlled exit is available without contacting providers", () => {
  strictEqual(typeof streamSimple, "function");
});

test("validateToolArguments checks TypeBox schemas and reports all errors", () => {
  const schema = Type.Object({
    chapter: Type.Integer({ minimum: 1 }),
    title: Type.String(),
  });

  const valid = validateToolArguments(schema, { chapter: 3, title: "开端" });
  strictEqual(valid.ok, true);
  strictEqual(valid.value?.chapter, 3);

  const invalid = validateToolArguments(schema, { chapter: -1 });
  strictEqual(invalid.ok, false);
  strictEqual(invalid.value, undefined);
  ok(invalid.errors.length >= 2);
});
