import type { AgentCoreTool } from "@tianming/agent-core";

export const CHAPTER_WRITER_ROLE_NAME = "chapter-writer" as const;

export interface NovelRole {
  readonly name: string;
  readonly description: string;
  readonly allowedTools: readonly string[];
  readonly maxDepth: number;
  readonly completionStrategy: "natural_stop";
  readonly systemPrompt: string;
}

export const chapterWriterRole: NovelRole = {
  name: CHAPTER_WRITER_ROLE_NAME,
  description: "Writes one candidate chapter against a frozen novel context.",
  allowedTools: ["read_context", "request_chapter"],
  maxDepth: 1,
  completionStrategy: "natural_stop",
  systemPrompt: [
    "You are the chapter-writer role.",
    "Write one candidate chapter only; do not merge Canon or mutate project state.",
    "Use only the frozen context and the request_chapter tool.",
    "The host owns review, acceptance, and Canon state transitions.",
  ].join(" "),
};

export function filterRoleTools(role: NovelRole, tools: readonly AgentCoreTool[]): AgentCoreTool[] {
  const allowed = new Set(role.allowedTools);
  return tools.filter((tool) => allowed.has(tool.name));
}

export function assertRoleTools(role: NovelRole, tools: readonly AgentCoreTool[]): void {
  const unknown = tools.find((tool) => !role.allowedTools.includes(tool.name));
  if (unknown) {
    throw new Error(`Tool ${unknown.name} is not allowed for role ${role.name}.`);
  }
}
