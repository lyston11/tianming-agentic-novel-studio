import { Type, type TSchema } from "@sinclair/typebox";
import type { AgentTool, AgentToolResult } from "@mariozechner/pi-agent-core";
import type { AspNetAgentApi, ConversationRuntimeRequest } from "./contracts.js";

interface RuntimeToolHooks {
  beforeToolCall(name: string): void;
  afterToolCall(name: string, result: AgentToolResult<unknown>): Promise<void>;
}

const textResult = (value: unknown): AgentToolResult<unknown> => ({
  content: [{ type: "text", text: JSON.stringify(value) }],
  details: value,
});

export function createRuntimeTools(
  request: ConversationRuntimeRequest,
  api: AspNetAgentApi,
  hooks: RuntimeToolHooks,
): AgentTool<any>[] {
  const tools: AgentTool<any>[] = [];

  const guarded = <TParameters extends TSchema, TDetails>(
    tool: AgentTool<TParameters, TDetails>,
  ): AgentTool<TParameters, TDetails> => ({
    ...tool,
    execute: async (toolCallId, params, signal, onUpdate) => {
      hooks.beforeToolCall(tool.name);
      const result = await tool.execute(toolCallId, params, signal, onUpdate);
      await hooks.afterToolCall(tool.name, result);
      return result;
    },
  });

  const listProjects = guarded({
    name: "list_accessible_projects",
    label: "List accessible projects",
    description: "List minimal metadata for projects the authenticated user may select.",
    parameters: Type.Object({}),
    execute: async () => textResult({ status: "ok", projects: await api.listAccessibleProjects(request) }),
  });

  const activateProject = guarded({
    name: "activate_project_context",
    label: "Activate project context",
    description: "Bind this conversation to a project only after the user explicitly confirms it.",
    parameters: Type.Object({
      projectId: Type.String({ minLength: 1 }),
      confirmed: Type.Boolean({ description: "True only when the user explicitly confirmed this project." }),
    }),
    execute: async (_toolCallId, params) => {
      if (!params.confirmed || !request.sourceUserMessageId) {
        return textResult({
          status: "confirmation_required",
          recoverable: true,
          reason: "Explicit user confirmation in a persisted user message is required.",
        });
      }

      const result = await api.activateProjectContext(request, {
        projectId: params.projectId,
        confirmationSource: `conversation_user_message:${request.sourceUserMessageId}`,
        idempotencyKey: `pi:${request.sessionId}:${request.sourceUserMessageId}:${params.projectId}`,
        expectedBindingVersion: request.binding.version,
      });
      if (result.code === "activated" || result.code === "already_activated") {
        const refreshed = await api.loadContext(request);
        request.binding = refreshed.binding;
        request.systemInstructions = refreshed.systemInstructions;
        request.projectContext = refreshed.projectContext;
        request.allowedProjectTools = refreshed.allowedProjectTools;
        replaceTools(tools, createRuntimeTools(request, api, hooks));
      }
      return textResult({
        status: result.code,
        recoverable: result.code !== "activated" && result.code !== "already_activated",
        ...result,
      });
    },
  });

  const getProjectContext = guarded({
    name: "get_project_context",
    label: "Read project context",
    description: "Read the current authorized, versioned project snapshot for this conversation.",
    parameters: Type.Object({}),
    execute: async () => textResult({
      status: "ok",
      binding: request.binding,
      context: request.projectContext,
      allowedProjectTools: request.allowedProjectTools,
    }),
  });

  if (request.binding.state === "unbound") {
    tools.push(listProjects, activateProject);
  } else {
    tools.push(getProjectContext);
  }
  return tools;
}

function replaceTools(target: AgentTool<any>[], source: AgentTool<any>[]): void {
  target.splice(0, target.length, ...source);
}
