import type { AssistantBlock, RunningToolCall } from "@deepseek-ai/dsh-client-runtime/client";

export type MutationToolName = "write" | "edit" | "str_replace_editor";

export interface FileMutationActivity {
  readonly callId: string;
  readonly name: MutationToolName;
  readonly argsRaw: string;
  readonly stage: "streaming" | "running";
  readonly path: string | undefined;
  readonly operation: "replace-file" | "replace-text" | "insert-text" | undefined;
  readonly oldText: string | undefined;
  readonly newText: string | undefined;
}

interface JsonStringPrefix {
  readonly value: string;
  readonly complete: boolean;
}

export type WorkbenchMode = "story" | "drama";

const STORY_DIRECTORIES = new Set(["正文", "大纲", "设定", "追踪", "对标", "参考资料"]);
const DRAMA_DIRECTORIES = new Set(["输入", "项目开发", "设定集", "剧集", "交付", "创作者决策", "审查"]);
const EDITABLE_EXTENSION = /\.(?:md|txt|json|jsonl)$/iu;

function decodeEscape(character: string): string | undefined {
  switch (character) {
    case "\"": return "\"";
    case "\\": return "\\";
    case "/": return "/";
    case "b": return "\b";
    case "f": return "\f";
    case "n": return "\n";
    case "r": return "\r";
    case "t": return "\t";
    default: return undefined;
  }
}

/** Read a JSON string even while the model is still streaming its closing quote. */
export function jsonStringPrefix(raw: string, key: string): JsonStringPrefix | undefined {
  const match = new RegExp(`"${key.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&")}"\\s*:\\s*"`, "u").exec(raw);
  if (match === null) return undefined;
  let value = "";
  for (let index = match.index + match[0].length; index < raw.length; index += 1) {
    const character = raw[index] ?? "";
    if (character === "\"") return { value, complete: true };
    if (character !== "\\") { value += character; continue; }
    const escape = raw[index + 1];
    if (escape === undefined) return { value, complete: false };
    if (escape === "u") {
      const hex = raw.slice(index + 2, index + 6);
      if (!/^[\da-f]{4}$/iu.test(hex)) return { value, complete: false };
      value += String.fromCharCode(Number.parseInt(hex, 16));
      index += 5;
      continue;
    }
    const decoded = decodeEscape(escape);
    if (decoded === undefined) return { value, complete: false };
    value += decoded;
    index += 1;
  }
  return { value, complete: false };
}

function completedString(raw: string, key: string): string | undefined {
  const value = jsonStringPrefix(raw, key);
  return value?.complete === true ? value.value : undefined;
}

function mutation(name: string, callId: string, argsRaw: string, stage: FileMutationActivity["stage"]): FileMutationActivity | undefined {
  if (name === "write") {
    return {
      callId, name, argsRaw, stage,
      path: jsonStringPrefix(argsRaw, "file_path")?.value,
      operation: "replace-file",
      oldText: undefined,
      newText: jsonStringPrefix(argsRaw, "content")?.value
    };
  }
  if (name === "edit") {
    return {
      callId, name, argsRaw, stage,
      path: jsonStringPrefix(argsRaw, "file_path")?.value,
      operation: "replace-text",
      oldText: completedString(argsRaw, "old_string"),
      newText: jsonStringPrefix(argsRaw, "new_string")?.value
    };
  }
  if (name !== "str_replace_editor") return undefined;
  const command = completedString(argsRaw, "command");
  if (command === "view") return undefined;
  const path = jsonStringPrefix(argsRaw, "path")?.value;
  if (command === "create") {
    return {
      callId, name, argsRaw, stage, path,
      operation: "replace-file",
      oldText: undefined,
      newText: jsonStringPrefix(argsRaw, "file_text")?.value
    };
  }
  if (command === "str_replace") {
    return {
      callId, name, argsRaw, stage, path,
      operation: "replace-text",
      oldText: completedString(argsRaw, "old_str"),
      newText: jsonStringPrefix(argsRaw, "new_str")?.value
    };
  }
  if (command === "insert") {
    return {
      callId, name, argsRaw, stage, path,
      operation: "insert-text",
      oldText: undefined,
      newText: jsonStringPrefix(argsRaw, "new_str")?.value
    };
  }
  // The command field itself may still be streaming. Returning the path lets
  // the tree focus as soon as enough arguments have arrived.
  return { callId, name, argsRaw, stage, path, operation: undefined, oldText: undefined, newText: undefined };
}

/** Prefer an executing call over the latest streamed tool-call block. */
export function activeFileMutation(
  partialBlocks: readonly AssistantBlock[],
  runningCalls: readonly RunningToolCall[]
): FileMutationActivity | undefined {
  for (let index = runningCalls.length - 1; index >= 0; index -= 1) {
    const call = runningCalls[index];
    if (call === undefined) continue;
    const value = mutation(call.name, call.callId, call.argsRaw, "running");
    if (value !== undefined) return value;
  }
  for (let index = partialBlocks.length - 1; index >= 0; index -= 1) {
    const block = partialBlocks[index];
    if (block?.kind !== "tool-call") continue;
    const value = mutation(block.name, block.callId, block.argsRaw, "streaming");
    if (value !== undefined) return value;
  }
  return undefined;
}

/** Convert a DSH tool path to the creative-relative path accepted by the narrow route. */
export function creativeRelativePath(path: string | undefined, cwd: string | undefined): string | undefined {
  if (path === undefined || path === "") return undefined;
  const normalized = path.replaceAll("\\", "/");
  const root = cwd?.replaceAll("\\", "/").replace(/\/$/u, "");
  const relative = root !== undefined && normalized.startsWith(`${root}/`)
    ? normalized.slice(root.length + 1)
    : normalized.replace(/^\.\//u, "").replace(/^\//u, "");
  const [directory] = relative.split("/", 1);
  const creative = directory !== undefined && (STORY_DIRECTORIES.has(directory) || DRAMA_DIRECTORIES.has(directory));
  if ((!creative && relative !== "short-drama.json") || !EDITABLE_EXTENSION.test(relative)) return undefined;
  if (relative.split("/").some((part) => part === ".." || part === "")) return undefined;
  return relative;
}

export function workbenchModeForPath(path: string | undefined): WorkbenchMode | undefined {
  if (path === "short-drama.json") return "drama";
  const directory = path?.split("/", 1)[0];
  if (directory !== undefined && STORY_DIRECTORIES.has(directory)) return "story";
  if (directory !== undefined && DRAMA_DIRECTORIES.has(directory)) return "drama";
  return undefined;
}

/** Compatibility alias for consumers that only accept novel files. */
export function storyRelativePath(path: string | undefined, cwd: string | undefined): string | undefined {
  const relative = creativeRelativePath(path, cwd);
  return workbenchModeForPath(relative) === "story" ? relative : undefined;
}

/** Project a streamed mutation over the last authoritative disk content. */
export function previewMutation(activity: FileMutationActivity, base: string): string | undefined {
  if (activity.operation === "replace-file") return activity.newText;
  if (activity.operation === "replace-text") {
    if (activity.oldText === undefined || activity.newText === undefined) return undefined;
    const at = base.indexOf(activity.oldText);
    return at < 0 ? undefined : `${base.slice(0, at)}${activity.newText}${base.slice(at + activity.oldText.length)}`;
  }
  if (activity.operation === "insert-text") {
    if (activity.newText === undefined) return undefined;
    const rawLine = /"insert_line"\s*:\s*(\d+)/u.exec(activity.argsRaw)?.[1];
    if (rawLine === undefined) return undefined;
    const line = Number.parseInt(rawLine, 10);
    const parts = base.split("\n");
    const at = Math.max(0, Math.min(parts.length, line));
    parts.splice(at, 0, activity.newText);
    return parts.join("\n");
  }
  return undefined;
}
