/**
 * @tianming/agent-ai — project model-call boundary.
 *
 * First-stage adapter over `@mariozechner/pi-ai@0.57.1`. This package is the
 * ONLY place in the Tianming Agent stack that imports pi-ai directly; the
 * agent core and novel-domain layers import these types and helpers instead.
 * Provider registries, token accounting, OAuth, HTTP providers, and model
 * discovery stay inside pi-ai and are intentionally not re-implemented here.
 */
import { streamSimple as piStreamSimple } from "@mariozechner/pi-ai";
import type {
  Api,
  AssistantMessageEventStream,
  Context,
  Model,
  SimpleStreamOptions,
} from "@mariozechner/pi-ai";
import { Value } from "@sinclair/typebox/value";
import type { Static, TSchema } from "@sinclair/typebox";

// --- Controlled type surface -------------------------------------------------

export type {
  Api,
  AssistantMessage,
  AssistantMessageEvent,
  AssistantMessageEventStream,
  Context,
  ImageContent,
  Message,
  Model,
  SimpleStreamOptions,
  StopReason,
  StreamFunction,
  StreamOptions,
  TextContent,
  ThinkingContent,
  Tool,
  ToolCall,
  ToolResultMessage,
  Usage,
  UserMessage,
} from "@mariozechner/pi-ai";

export { createAssistantMessageEventStream } from "@mariozechner/pi-ai";

// --- Controlled runtime surface ----------------------------------------------

/**
 * Controlled exit for pi-ai's simple streaming entry point. Hosts may pass a
 * custom stream function into the agent core for deterministic tests or
 * alternate transports; production callers use this wrapper unchanged.
 */
export function streamSimple(
  model: Model<Api>,
  context: Context,
  options?: SimpleStreamOptions,
): AssistantMessageEventStream {
  return piStreamSimple(model, context, options);
}

// --- Tool argument validation ------------------------------------------------

export interface ToolArgumentValidation<T extends TSchema = TSchema> {
  ok: boolean;
  value: Static<T> | undefined;
  errors: string[];
}

/**
 * Validate model-supplied tool arguments against a TypeBox tool schema.
 * Returns all validation errors so callers can surface them as an
 * observable error tool result instead of throwing mid-stream.
 */
export function validateToolArguments<T extends TSchema>(
  schema: T,
  value: unknown,
): ToolArgumentValidation<T> {
  if (Value.Check(schema, value)) {
    return { ok: true, value: value as Static<T>, errors: [] };
  }
  const errors = [...Value.Errors(schema, value)].map(
    (error) => `${error.path || "/"}: ${error.message}`,
  );
  return { ok: false, value: undefined, errors };
}
