import { createHash, randomUUID } from "node:crypto";
import { mkdir, readFile, readdir, rename, rm, stat, writeFile } from "node:fs/promises";
import type { IncomingMessage, ServerResponse } from "node:http";
import { dirname, extname, join, relative, resolve } from "node:path";
import type { Context } from "@deepseek-ai/cordis";
import type {} from "@deepseek-ai/dsh-host-webserver";
import { SessionId } from "@deepseek-ai/dsh-session";
import { canonicalWorkspaceRoot, resolveWorkspacePath } from "./workspace-security.js";
import { isTrustedWorkspaceRequest } from "./workspace-request-trust.js";

const STORY_DIRECTORIES = ["正文", "大纲", "设定", "追踪", "对标", "参考资料"] as const;
const DRAMA_DIRECTORIES = ["输入", "项目开发", "设定集", "剧集", "交付", "创作者决策", "审查"] as const;
const CREATIVE_DIRECTORIES = [...STORY_DIRECTORIES, ...DRAMA_DIRECTORIES] as const;
const ROOT_FILES = new Set(["short-drama.json"]);
const EDITABLE_EXTENSIONS = new Set([".md", ".txt", ".json", ".jsonl"]);

interface WorkspaceRouteOptions {
  readonly maxBytes: number;
  readonly trustedHosts?: readonly string[];
}
interface WorkspaceFile { readonly path: string; readonly bytes: number }

class WorkspaceHttpError extends Error {
  constructor(readonly status: number, message: string) { super(message); }
}

function send(response: ServerResponse, status: number, value: unknown): void {
  const body = `${JSON.stringify(value)}\n`;
  response.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(body),
    "cache-control": "no-store",
    "x-content-type-options": "nosniff"
  });
  response.end(body);
}

export function workspaceContentVersion(content: string | Buffer): string {
  return createHash("sha256").update(content).digest("hex");
}

async function jsonBody(request: IncomingMessage, maxBytes: number): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of request as AsyncIterable<Uint8Array>) {
    const value = Buffer.from(chunk);
    size += value.byteLength;
    if (size > maxBytes) throw new WorkspaceHttpError(413, "请求内容过大。");
    chunks.push(value);
  }
  try {
    const value = JSON.parse(Buffer.concat(chunks).toString("utf8")) as unknown;
    if (typeof value !== "object" || value === null || Array.isArray(value)) throw new Error();
    return value as Record<string, unknown>;
  } catch {
    throw new WorkspaceHttpError(400, "请求必须是 JSON 对象。");
  }
}

function assertCreativePath(path: string): void {
  if (!EDITABLE_EXTENSIONS.has(extname(path).toLocaleLowerCase())) {
    throw new WorkspaceHttpError(415, "工作台只编辑 Markdown、文本、JSON 和 JSONL 文件。");
  }
  if (path.split("/").some((segment) => segment === "" || segment === "..")) {
    throw new WorkspaceHttpError(403, "文件路径不在创作工作台中。");
  }
  const root = path.split("/", 1)[0];
  if (!CREATIVE_DIRECTORIES.some((directory) => directory === root) && !ROOT_FILES.has(path)) {
    throw new WorkspaceHttpError(403, "文件路径不在创作工作台中。");
  }
}

async function listFiles(root: string): Promise<WorkspaceFile[]> {
  const files: WorkspaceFile[] = [];
  const walk = async (directory: string): Promise<void> => {
    const absolute = await resolveWorkspacePath(root, directory, { expect: "directory" });
    for (const entry of await readdir(absolute, { withFileTypes: true })) {
      if (entry.isSymbolicLink() || entry.name.startsWith(".")) continue;
      const path = `${directory}/${entry.name}`;
      if (entry.isDirectory()) await walk(path);
      else if (entry.isFile() && EDITABLE_EXTENSIONS.has(extname(entry.name).toLocaleLowerCase())) {
        files.push({ path, bytes: (await stat(join(absolute, entry.name))).size });
      }
      if (files.length >= 1_000) return;
    }
  };
  for (const directory of CREATIVE_DIRECTORIES) await walk(directory).catch(() => undefined);
  for (const path of ROOT_FILES) {
    await resolveWorkspacePath(root, path, { expect: "file" })
      .then(async (absolute) => { files.push({ path, bytes: (await stat(absolute)).size }); })
      .catch(() => undefined);
  }
  return files.sort((left, right) => left.path.localeCompare(right.path, "zh-Hans-CN"));
}

function sessionRoot(context: Context, url: URL): Promise<string> {
  const id = url.searchParams.get("sessionId");
  if (id === null || id === "") throw new WorkspaceHttpError(400, "缺少 DSH sessionId。");
  const session = context.sessions.get(SessionId(id));
  if (session === undefined) throw new WorkspaceHttpError(404, "DSH 会话不可用。");
  if (session.header.parentSession !== undefined) throw new WorkspaceHttpError(403, "子 Agent 会话不开放小说编辑器。");
  const cwd = session.header.cwd;
  if (cwd === undefined) throw new WorkspaceHttpError(409, "当前 DSH 会话没有工作目录。");
  return canonicalWorkspaceRoot(cwd);
}

async function handle(context: Context, request: IncomingMessage, response: ServerResponse, options: WorkspaceRouteOptions): Promise<void> {
  try {
    if (!isTrustedWorkspaceRequest(request, options.trustedHosts ?? [])) {
      throw new WorkspaceHttpError(403, "请求来源不受信任。");
    }
    const url = new URL(request.url ?? "/", "http://127.0.0.1");
    if (url.pathname === "/oh-story/workspace" && request.method === "GET") {
      const root = await sessionRoot(context, url);
      const files = await listFiles(root);
      const trackingFile = files.find((file) => file.path === "追踪/_tracking-state.json");
      const tracking: unknown = trackingFile === undefined
        ? null
        : JSON.parse(await readFile(await resolveWorkspacePath(root, trackingFile.path, { expect: "file" }), "utf8")) as unknown;
      const dramaFile = files.find((file) => file.path === "short-drama.json");
      const shortDrama: unknown = dramaFile === undefined
        ? null
        : JSON.parse(await readFile(await resolveWorkspacePath(root, dramaFile.path, { expect: "file" }), "utf8")) as unknown;
      send(response, 200, { cwd: root, files, tracking, shortDrama, mode: "dsh-session" });
      return;
    }
    if (url.pathname === "/oh-story/file" && request.method === "GET") {
      const root = await sessionRoot(context, url);
      const path = url.searchParams.get("path");
      if (path === null) throw new WorkspaceHttpError(400, "缺少文件路径。");
      assertCreativePath(path);
      const absolute = await resolveWorkspacePath(root, path, { expect: "file" });
      const data = await readFile(absolute);
      if (data.byteLength > options.maxBytes) throw new WorkspaceHttpError(413, "文件超过工作台大小限制。");
      send(response, 200, {
        path,
        content: data.toString("utf8"),
        bytes: data.byteLength,
        version: workspaceContentVersion(data)
      });
      return;
    }
    if (url.pathname === "/oh-story/file" && request.method === "PUT") {
      const root = await sessionRoot(context, url);
      const path = url.searchParams.get("path");
      if (path === null) throw new WorkspaceHttpError(400, "缺少文件路径。");
      assertCreativePath(path);
      // A JSON string can expand control bytes up to sixfold (for example
      // `\u0000`), so the transport cap must not reject otherwise valid files.
      const input = await jsonBody(request, options.maxBytes * 6 + 1_024);
      if (typeof input.content !== "string") throw new WorkspaceHttpError(400, "content 必须是字符串。");
      if (typeof input.baseVersion !== "string") throw new WorkspaceHttpError(400, "baseVersion 必须是字符串。");
      if (Buffer.byteLength(input.content) > options.maxBytes) throw new WorkspaceHttpError(413, "文件超过工作台大小限制。");
      // Human edits use this route directly. Model-facing prose policy belongs
      // to DSH's tool waterfall and must never be applied to the editor.
      const absolute = await resolveWorkspacePath(root, path, { expect: "file" });
      const current = await readFile(absolute);
      if (workspaceContentVersion(current) !== input.baseVersion) {
        throw new WorkspaceHttpError(412, "文件已在磁盘上更新。请刷新并合并后再保存。");
      }
      const parent = dirname(absolute);
      await mkdir(parent, { recursive: true });
      const rootReal = await canonicalWorkspaceRoot(root);
      const parentReal = await canonicalWorkspaceRoot(parent);
      const offset = relative(rootReal, parentReal);
      if (offset === ".." || offset.startsWith("../")) throw new WorkspaceHttpError(403, "文件路径离开了 DSH 工作目录。");
      const temporary = resolve(parent, `.${randomUUID()}.oh-story.tmp`);
      try {
        await writeFile(temporary, input.content, { encoding: "utf8", flag: "wx" });
        if (workspaceContentVersion(await readFile(absolute)) !== input.baseVersion) {
          throw new WorkspaceHttpError(412, "文件已在磁盘上更新。请刷新并合并后再保存。");
        }
        await rename(temporary, absolute);
      } finally {
        await rm(temporary, { force: true });
      }
      send(response, 200, {
        path,
        content: input.content,
        bytes: Buffer.byteLength(input.content),
        version: workspaceContentVersion(input.content)
      });
      return;
    }
    send(response, 404, { error: "Oh Story route not found." });
  } catch (error) {
    if (error instanceof WorkspaceHttpError) send(response, error.status, { error: error.message });
    else send(response, 500, { error: error instanceof Error ? error.message : String(error) });
  }
}

/** This narrow route never starts agents, runs, streams, or models. */
export function registerWorkspaceRoute(context: Context, options: WorkspaceRouteOptions): void {
  context.effect(() => context.webServer.register({
    kind: "prefix",
    path: "/oh-story",
    handler: (request, response) => handle(context, request, response, options)
  }), "oh-story: DSH-session workspace API");
}
