import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { readFile } from "node:fs/promises";
import { getModels, type KnownProvider } from "@mariozechner/pi-ai";
import type { ConversationRuntimeRequest } from "./contracts.js";
import { HttpAspNetAgentApi } from "./internal-api.js";
import { NovelPiRuntime, type RuntimeStreamEvent } from "./runtime.js";

const MAX_REQUEST_BYTES = 1_048_576;

export async function createRuntimeFromEnvironment(): Promise<NovelPiRuntime> {
  const provider = required("PI_MODEL_PROVIDER") as KnownProvider;
  const modelId = required("PI_MODEL_ID");
  const model = getModels(provider).find((candidate) => candidate.id === modelId);
  if (!model) throw new Error(`Unknown Pi model: ${provider}/${modelId}`);

  const discoverySkill = await readFile(new URL("../resources/project-discovery.md", import.meta.url), "utf8");
  const api = new HttpAspNetAgentApi(
    required("PI_RUNTIME_CALLBACK_BASE_URL"),
    required("PI_RUNTIME_INTERNAL_API_KEY"),
  );
  return new NovelPiRuntime({
    model,
    api,
    discoverySkill,
    getApiKey: async () => process.env.PI_MODEL_API_KEY,
    maxToolCalls: parsePositiveInteger(process.env.PI_RUNTIME_MAX_TOOL_CALLS, 24),
  });
}

export function createRuntimeServer(runtime: NovelPiRuntime) {
  return createServer(async (request, response) => {
    if (request.method === "GET" && request.url === "/health") {
      writeJson(response, 200, { status: "ok" });
      return;
    }
    if (request.method !== "POST" || request.url !== "/v1/conversation-turn") {
      writeJson(response, 404, { error: "not_found" });
      return;
    }

    try {
      const input = validateRequest(await readJson(request));
      response.writeHead(200, {
        "content-type": "application/x-ndjson; charset=utf-8",
        "cache-control": "no-store",
      });
      const emit = (event: RuntimeStreamEvent) => response.write(`${JSON.stringify(event)}\n`);
      await runtime.run(input, emit);
      response.end();
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (!response.headersSent) {
        writeJson(response, message === "request_too_large" ? 413 : 400, { error: message });
      } else {
        response.write(`${JSON.stringify({ type: "failed", error: message })}\n`);
        response.end();
      }
    }
  });
}

async function readJson(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    size += buffer.length;
    if (size > MAX_REQUEST_BYTES) throw new Error("request_too_large");
    chunks.push(buffer);
  }
  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function validateRequest(value: unknown): ConversationRuntimeRequest {
  if (!value || typeof value !== "object") throw new Error("invalid_request");
  const request = value as Partial<ConversationRuntimeRequest>;
  if (!request.userId || !request.sessionId || !request.message || !request.correlationId) {
    throw new Error("invalid_request");
  }
  if (!request.binding || !Array.isArray(request.durableMessages) || !Array.isArray(request.allowedProjectTools)) {
    throw new Error("invalid_request");
  }
  return request as ConversationRuntimeRequest;
}

function writeJson(response: ServerResponse, status: number, body: unknown): void {
  response.writeHead(status, { "content-type": "application/json; charset=utf-8" });
  response.end(JSON.stringify(body));
}

function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function parsePositiveInteger(value: string | undefined, fallback: number): number {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const runtime = await createRuntimeFromEnvironment();
  const port = parsePositiveInteger(process.env.PI_RUNTIME_PORT, 4317);
  createRuntimeServer(runtime).listen(port, "0.0.0.0", () => {
    process.stdout.write(`Tianming Pi Runtime listening on ${port}\n`);
  });
}
