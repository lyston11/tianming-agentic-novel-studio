import { access, readdir } from "node:fs/promises";
import { isAbsolute, relative, resolve, sep } from "node:path";
import type { Context } from "@deepseek-ai/cordis";
import { createUserMessage } from "@deepseek-ai/dsh-llm";
import type { PostToolDecision, PreToolDecision, ToolExecution } from "@deepseek-ai/dsh-tools";

const MUTATION_TOOLS = new Set(["write", "edit", "str_replace_editor"]);

export interface StoryMutation {
  readonly root: string;
  readonly path: string;
  readonly chapter?: number;
}

function mutationPath(name: string, args: unknown): string | undefined {
  if (!MUTATION_TOOLS.has(name) || typeof args !== "object" || args === null || Array.isArray(args)) return undefined;
  const record = args as Record<string, unknown>;
  if (name === "str_replace_editor") {
    if (!new Set(["create", "str_replace", "insert"]).has(String(record.command))) return undefined;
    return typeof record.path === "string" && record.path.trim() !== "" ? record.path : undefined;
  }
  return typeof record.file_path === "string" && record.file_path.trim() !== "" ? record.file_path : undefined;
}

export function detectStoryMutation(name: string, args: unknown, cwd: string | undefined): StoryMutation | undefined {
  const path = mutationPath(name, args);
  if (cwd === undefined || path === undefined) return undefined;
  const root = resolve(cwd);
  const absolute = isAbsolute(path) ? resolve(path) : resolve(root, path);
  const offset = relative(root, absolute);
  if (offset === ".." || offset.startsWith(`..${sep}`) || isAbsolute(offset)) return undefined;
  const normalized = offset.replaceAll("\\", "/");
  if (!normalized.startsWith("正文/")) return undefined;
  const chapterText = /第0*(\d+)章/u.exec(normalized)?.[1];
  return { root, path: normalized, ...(chapterText === undefined ? {} : { chapter: Number(chapterText) }) };
}

function storyMutation(exec: ToolExecution): StoryMutation | undefined {
  return detectStoryMutation(exec.name, exec.arguments, exec.agent?.session.header.cwd);
}

async function exists(path: string): Promise<boolean> {
  return access(path).then(() => true, () => false);
}

async function hasChapterOutline(root: string, chapter: number): Promise<boolean> {
  const entries = await readdir(resolve(root, "大纲"), { withFileTypes: true }).catch(() => []);
  return entries.some((entry) => entry.isFile()
    && Number(/^细纲_第0*(\d+)章.*\.md$/u.exec(entry.name)?.[1]) === chapter);
}

export async function validateStoryMutation(mutation: StoryMutation): Promise<string | undefined> {
  const hasLongFormLayout = await exists(resolve(mutation.root, "大纲")) || await exists(resolve(mutation.root, "追踪"));
  if (!hasLongFormLayout) return undefined;
  if (!(await exists(resolve(mutation.root, "追踪/_tracking-state.json")))) {
    // Setup/import must be able to bootstrap an existing manuscript before the
    // canonical Tracking file exists. The Skill remains responsible for
    // creating it; hard guards begin once the project has committed Tracking.
    return undefined;
  }
  if (mutation.chapter !== undefined && !(await hasChapterOutline(mutation.root, mutation.chapter))) {
    return `Oh Story 阻止写入第 ${String(mutation.chapter)} 章：未找到对应的 大纲/细纲_第XXX章*.md。请先完成细纲。`;
  }
  return undefined;
}

export async function decideStoryMutation(
  exec: ToolExecution,
  next: () => Promise<PreToolDecision>
): Promise<PreToolDecision> {
  const mutation = storyMutation(exec);
  if (mutation === undefined) return next();
  const reason = await validateStoryMutation(mutation);
  if (reason !== undefined) return { kind: "deny", reason };
  return next();
}

/**
 * Native DSH equivalents of the upstream prose guards. They join DSH's typed
 * tool waterfall, so decisions remain visible in the official approval/tool UI.
 */
export function registerOhStoryHooks(context: Context): void {
  context.on("tools/pre-execute", decideStoryMutation);

  context.on("tools/post-execute", async (exec, result, next): Promise<PostToolDecision> => {
    const mutation = storyMutation(exec);
    const downstream = await next();
    if (mutation === undefined || result.isError || downstream.kind !== "accept") return downstream;
    const reminder = createUserMessage({
      source: { kind: "plugin", plugin: "oh-story" },
      content: [{
        type: "text",
        text: `<oh-story-post-write>正文 ${mutation.path} 已变更。继续当前步骤前核对并更新 _tracking-state.json 及对应派生 Tracking 视图；不要把这条提醒当作用户的新写作要求。</oh-story-post-write>`
      }]
    });
    return {
      ...downstream,
      additionalContexts: [...downstream.additionalContexts ?? [], reminder]
    };
  });
}
