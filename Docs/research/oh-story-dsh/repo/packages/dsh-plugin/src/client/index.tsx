import type { AssistantBlock, ClientContext, RunningToolCall } from "@deepseek-ai/dsh-client-runtime/client";
import type { ConvViewProps } from "@deepseek-ai/dsh-client-ui-conversation/client";
import type { ToolCallViewProps } from "@deepseek-ai/dsh-client-ui-tool/client";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  activeFileMutation,
  creativeRelativePath,
  previewMutation,
  workbenchModeForPath,
  type WorkbenchMode,
  type FileMutationActivity
} from "./file-activity.js";
import { buildFileTree, type FileTreeNode } from "./file-tree.js";
import { JsonlPreview } from "./jsonl-preview.js";
import { MarkdownPreview } from "./markdown-preview.js";
import styles from "./plugin.css?inline";

export const name = "oh-story";
export const inject = ["slots"];

interface WorkspaceFile { readonly path: string; readonly bytes: number }
interface WorkspacePayload {
  readonly cwd: string;
  readonly files: readonly WorkspaceFile[];
  readonly shortDrama: Record<string, unknown> | null;
  readonly mode: "dsh-session";
}
interface FilePayload {
  readonly path: string;
  readonly content: string;
  readonly bytes: number;
  readonly version: string;
}
interface FileBuffer {
  readonly content: string;
  readonly saved: string;
  readonly source: "disk" | "human" | "agent";
  readonly version: string;
}

const GROUP_ORDER: Readonly<Record<WorkbenchMode, readonly string[]>> = {
  story: ["正文", "大纲", "设定", "追踪", "对标", "参考资料"],
  drama: ["项目", "输入", "项目开发", "设定集", "剧集", "审查", "创作者决策", "交付"]
};

function groupForPath(path: string): string {
  return path === "short-drama.json" ? "项目" : path.split("/", 1)[0] ?? "其他";
}

function preferredFile(files: readonly WorkspaceFile[], mode: WorkbenchMode): string | undefined {
  const matching = files.filter((file) => workbenchModeForPath(file.path) === mode);
  const preferences = mode === "story"
    ? [/^正文\/.*\.md$/u, /^大纲\/.*\.md$/u, /\.md$/u]
    : [/^剧集\/EP0*1\/screenplay\.md$/iu, /^剧集\/.*\/screenplay\.md$/iu, /^项目开发\/creative-brief\.md$/u, /^输入\/.*\.md$/u, /\.md$/u, /^short-drama\.json$/u];
  for (const pattern of preferences) {
    const match = matching.find((file) => pattern.test(file.path));
    if (match !== undefined) return match.path;
  }
  return matching[0]?.path;
}

const EMPTY_BLOCKS: readonly AssistantBlock[] = [];
const EMPTY_CALLS: readonly RunningToolCall[] = [];

function endpoint(path: string, sessionId: string, file?: string): string {
  const url = new URL(`/oh-story/${path}`, globalThis.location.origin);
  url.searchParams.set("sessionId", sessionId);
  if (file !== undefined) url.searchParams.set("path", file);
  return url.toString();
}

async function json<T>(response: Response): Promise<T> {
  const value = await response.json() as T & { readonly error?: string };
  if (!response.ok) throw new Error(value.error ?? `HTTP ${String(response.status)}`);
  return value;
}

function FileTreeNodes({
  nodes,
  depth,
  expanded,
  selected,
  activityPath,
  onToggle,
  onSelect
}: {
  readonly nodes: readonly FileTreeNode[];
  readonly depth: number;
  readonly expanded: Readonly<Record<string, boolean>>;
  readonly selected: string | undefined;
  readonly activityPath: string | undefined;
  readonly onToggle: (path: string, open: boolean) => void;
  readonly onSelect: (path: string) => void;
}) {
  return <>{nodes.map((node) => {
    if (node.kind === "file") return <button
      type="button"
      key={node.path}
      style={{ paddingLeft: `${String(14 + depth * 14)}px` }}
      data-file-path={node.path}
      data-agent-target={node.path === activityPath || undefined}
      aria-current={node.path === selected ? "page" : undefined}
      onClick={() => { onSelect(node.path); }}
    >{node.name}</button>;
    const open = expanded[node.path] ?? selected?.startsWith(`${node.path}/`) === true;
    return <details className="oh-story-file-folder" key={node.path} open={open} onToggle={(event) => { onToggle(node.path, event.currentTarget.open); }}>
      <summary style={{ paddingLeft: `${String(7 + depth * 14)}px` }}>{node.name}<span>{node.fileCount}</span></summary>
      <FileTreeNodes
        nodes={node.children}
        depth={depth + 1}
        expanded={expanded}
        selected={selected}
        activityPath={activityPath}
        onToggle={onToggle}
        onSelect={onSelect}
      />
    </details>;
  })}</>;
}

function useWorkspace(sessionId: string): {
  readonly workspace: WorkspacePayload | undefined;
  readonly error: string | undefined;
  readonly reload: () => void;
} {
  const [version, setVersion] = useState(0);
  const [workspace, setWorkspace] = useState<WorkspacePayload>();
  const [error, setError] = useState<string>();
  const reload = useCallback(() => { setVersion((value) => value + 1); }, []);
  useEffect(() => {
    const controller = new AbortController();
    setError(undefined);
    void fetch(endpoint("workspace", sessionId), { signal: controller.signal })
      .then((response) => json<WorkspacePayload>(response))
      .then(setWorkspace)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
      });
    return () => { controller.abort(); };
  }, [sessionId, version]);
  return { workspace, error, reload };
}

function CreativeWorkbench({ sessionId, useSession }: Pick<ConvViewProps, "sessionId" | "useSession">) {
  const { workspace, error, reload } = useWorkspace(sessionId);
  const partialBlocks = useSession((value) => value.partial?.blocks ?? EMPTY_BLOCKS);
  const runningCalls = useSession((value) => value.runningCalls ?? EMPTY_CALLS);
  const activity = useMemo(() => activeFileMutation(partialBlocks, runningCalls), [partialBlocks, runningCalls]);
  const activityPath = creativeRelativePath(activity?.path, workspace?.cwd);
  const activityMode = workbenchModeForPath(activityPath);
  const [workbench, setWorkbench] = useState<WorkbenchMode>("story");
  const initializedWorkbench = useRef(false);
  const [selected, setSelected] = useState<string>();
  const [buffers, setBuffers] = useState<Record<string, FileBuffer>>({});
  const buffersRef = useRef<Record<string, FileBuffer>>({});
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [fileError, setFileError] = useState<string>();
  const [conflict, setConflict] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const surfaceRef = useRef<HTMLDivElement>(null);
  const navRef = useRef<HTMLElement>(null);
  const lastActivity = useRef<FileMutationActivity>();
  const settleTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const buffer = selected === undefined ? undefined : buffers[selected];
  const dirty = buffer?.source === "human" && buffer.content !== buffer.saved;
  const selectedLower = selected?.toLocaleLowerCase();
  const markdown = selectedLower?.endsWith(".md") === true;
  const jsonl = selectedLower?.endsWith(".jsonl") === true;
  const structured = jsonl || selectedLower?.endsWith(".json") === true;
  const previewable = markdown || jsonl;
  const [editorMode, setEditorMode] = useState<"preview" | "source">("preview");

  useEffect(() => { buffersRef.current = buffers; }, [buffers]);

  useEffect(() => {
    if (workspace === undefined || initializedWorkbench.current) return;
    const hasStory = workspace.files.some((file) => workbenchModeForPath(file.path) === "story");
    if (workspace.shortDrama !== null && !hasStory) setWorkbench("drama");
    initializedWorkbench.current = true;
  }, [workspace]);

  useEffect(() => {
    if (activityMode !== undefined) setWorkbench(activityMode);
  }, [activityMode]);

  useEffect(() => {
    setEditorMode(previewable ? "preview" : "source");
  }, [previewable, selected]);

  useEffect(() => {
    if (activityPath !== undefined) { setSelected(activityPath); return; }
    if (selected !== undefined && (
      (workspace?.files.some((file) => file.path === selected) ?? false)
      || buffers[selected] !== undefined
    ) && workbenchModeForPath(selected) === workbench) return;
    setSelected(workspace === undefined ? undefined : preferredFile(workspace.files, workbench));
  }, [activityPath, buffers, selected, workbench, workspace]);

  useEffect(() => {
    if (selected === undefined) return;
    const exists = workspace?.files.some((file) => file.path === selected) ?? false;
    if (activityPath === selected && !exists) return;
    const controller = new AbortController();
    setFileError(undefined);
    void fetch(endpoint("file", sessionId, selected), { signal: controller.signal })
      .then((response) => json<FilePayload>(response))
      .then((file) => {
        const existing = buffersRef.current[file.path];
        if (existing?.source === "human" && existing.content !== existing.saved) {
          setConflict(`${file.path} 已被 Agent 更新；你的未保存版本仍保留。`);
          return;
        }
        setBuffers((current) => ({
          ...current,
          [file.path]: { content: file.content, saved: file.content, source: "disk", version: file.version }
        }));
      })
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setFileError(reason instanceof Error ? reason.message : String(reason));
      });
    return () => { controller.abort(); };
  }, [activityPath, refreshVersion, selected, sessionId, workspace?.files]);

  useEffect(() => {
    if (activity === undefined || activityPath === undefined) return;
    setSelected(activityPath);
    const existing = buffers[activityPath];
    if (existing?.source === "human" && existing.content !== existing.saved) {
      setConflict(`${activityPath} 正由 Agent 修改；你的未保存版本已锁定，不会被覆盖。`);
      return;
    }
    const next = previewMutation(activity, existing?.saved ?? "");
    if (next === undefined) return;
    setConflict(undefined);
    setBuffers((current) => {
      const currentBuffer = current[activityPath];
      if (currentBuffer?.source === "agent" && currentBuffer.content === next) return current;
      return {
        ...current,
        [activityPath]: {
          content: next,
          saved: currentBuffer?.saved ?? "",
          source: "agent",
          version: currentBuffer?.version ?? ""
        }
      };
    });
  }, [activity, activityPath, buffers]);

  useEffect(() => {
    if (activity !== undefined) {
      if (settleTimer.current !== undefined) clearTimeout(settleTimer.current);
      lastActivity.current = activity;
      return;
    }
    if (lastActivity.current === undefined) return;
    settleTimer.current = setTimeout(() => {
      lastActivity.current = undefined;
      reload();
      setRefreshVersion((value) => value + 1);
    }, 180);
    return () => {
      if (settleTimer.current !== undefined) clearTimeout(settleTimer.current);
    };
  }, [activity, reload]);

  useEffect(() => {
    if (selected === undefined) return;
    for (const button of navRef.current?.querySelectorAll<HTMLButtonElement>("button[data-file-path]") ?? []) {
      if (button.dataset.filePath === selected) {
        button.scrollIntoView({ block: "nearest" });
        break;
      }
    }
  }, [selected]);

  useEffect(() => {
    if (activity !== undefined || workspace === undefined) return;
    const sessionSurface = surfaceRef.current?.parentElement;
    if (sessionSurface === undefined || sessionSurface === null) return;
    const knownPaths = new Set(workspace.files.map((file) => file.path));
    const followOfficialFileLink = (event: MouseEvent): void => {
      const origin = event.target;
      if (!(origin instanceof Element)) return;
      const control = origin.closest<HTMLElement>("button, a");
      if (control === null || control.closest(".oh-story-split-surface") !== null) return;
      const candidates = [control.title, control.getAttribute("aria-label"), control.textContent];
      for (const candidate of candidates) {
        const path = creativeRelativePath(candidate?.trim().replace(/^Open\s+/u, ""), workspace.cwd);
        if (path === undefined || !knownPaths.has(path)) continue;
        event.preventDefault();
        event.stopPropagation();
        setConflict(undefined);
        setWorkbench(workbenchModeForPath(path) ?? "story");
        setSelected(path);
        break;
      }
    };
    sessionSurface.addEventListener("click", followOfficialFileLink, true);
    return () => { sessionSurface.removeEventListener("click", followOfficialFileLink, true); };
  }, [activity, workspace]);

  const save = useCallback(async () => {
    if (selected === undefined || buffer === undefined) return;
    const submitted = buffer;
    setSaving(true);
    setFileError(undefined);
    try {
      const file = await json<FilePayload>(await fetch(endpoint("file", sessionId, selected), {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ content: submitted.content, baseVersion: submitted.version })
      }));
      setBuffers((current) => {
        const latest = current[selected];
        if (latest === undefined) return current;
        const unchanged = latest.content === submitted.content;
        return {
          ...current,
          [selected]: {
            content: unchanged ? file.content : latest.content,
            saved: file.content,
            source: unchanged ? "disk" : "human",
            version: file.version
          }
        };
      });
      setConflict(undefined);
      reload();
    } catch (reason) {
      setFileError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      setSaving(false);
    }
  }, [buffer, reload, selected, sessionId]);

  const groups = useMemo(() => {
    const value = new Map<string, WorkspaceFile[]>();
    const all = [...(workspace?.files ?? [])].filter((file) => workbenchModeForPath(file.path) === workbench);
    if (activityPath !== undefined && !all.some((file) => file.path === activityPath)) all.push({ path: activityPath, bytes: 0 });
    all.sort((left, right) => left.path.localeCompare(right.path, "zh-Hans-CN"));
    for (const file of all) {
      const directory = groupForPath(file.path);
      const files = value.get(directory) ?? [];
      files.push(file);
      value.set(directory, files);
    }
    const order = GROUP_ORDER[workbench];
    return [...value.entries()].sort(([left], [right]) => {
      const leftIndex = order.indexOf(left);
      const rightIndex = order.indexOf(right);
      return (leftIndex < 0 ? order.length : leftIndex) - (rightIndex < 0 ? order.length : rightIndex)
        || left.localeCompare(right, "zh-Hans-CN");
    });
  }, [activityPath, workbench, workspace]);

  const selectWorkbench = (next: WorkbenchMode): void => {
    setWorkbench(next);
    setConflict(undefined);
    const target = workspace === undefined ? undefined : preferredFile(workspace.files, next);
    setSelected(target);
  };
  const selectedGroup = selected === undefined ? undefined : groupForPath(selected);
  const toggleGroup = (key: string, open: boolean): void => {
    setExpanded((current) => ({ ...current, [key]: open }));
  };

  return <div ref={surfaceRef} className="oh-story-split-surface">
    <style>{styles}</style>
    <aside className="oh-story-tree">
      <div className="oh-story-brand">
        <span>✦ Oh Story</span>
        <button type="button" onClick={reload} title="刷新">↻</button>
      </div>
      <div className="oh-story-mode-tabs" role="tablist" aria-label="创作工作台">
        <button type="button" role="tab" aria-selected={workbench === "story"} onClick={() => { selectWorkbench("story"); }}>小说</button>
        <button type="button" role="tab" aria-selected={workbench === "drama"} onClick={() => { selectWorkbench("drama"); }}>短剧</button>
      </div>
      {error !== undefined && <div className="oh-story-error">{error}</div>}
      <nav ref={navRef} aria-label={workbench === "story" ? "小说项目文件" : "短剧项目文件"}>
        {groups.map(([directory, files]) => {
          const groupOpen = expanded[directory] ?? selectedGroup === directory;
          return <details className="oh-story-file-group" key={directory} open={groupOpen} onToggle={(event) => { toggleGroup(directory, event.currentTarget.open); }}>
            <summary>{directory}<span>{files.length}</span></summary>
            <FileTreeNodes
              nodes={buildFileTree(files, directory)}
              depth={1}
              expanded={expanded}
              selected={selected}
              activityPath={activityPath}
              onToggle={toggleGroup}
              onSelect={setSelected}
            />
          </details>;
        })}
      </nav>
    </aside>
    <main className="oh-story-editor">
      <header>
        <span title={selected}>{selected ?? `在当前 DSH workspace 中选择${workbench === "story" ? "小说" : "短剧"}文件`}</span>
        <div className="oh-story-editor-actions">
          {previewable && <div className="oh-story-editor-tabs" role="tablist" aria-label={markdown ? "Markdown 查看方式" : "JSONL 查看方式"}>
            <button type="button" role="tab" aria-selected={editorMode === "preview"} onClick={() => { setEditorMode("preview"); }}>预览</button>
            <button type="button" role="tab" aria-selected={editorMode === "source"} onClick={() => { setEditorMode("source"); }}>源码</button>
          </div>}
          {(dirty || saving) && <button className="oh-story-save" type="button" disabled={saving} onClick={() => { void save(); }}>
            {saving ? "保存中…" : "保存"}
          </button>}
        </div>
      </header>
      {activityPath === selected && <div className="oh-story-stream" data-stage={activity?.stage}>● {activity?.stage === "running" ? "Agent 正在应用修改" : "Agent 正在生成文件内容"}</div>}
      {conflict !== undefined && <div className="oh-story-conflict">{conflict}</div>}
      {fileError !== undefined && <div className="oh-story-error">{fileError}</div>}
      {selected === undefined
        ? <div className="oh-story-empty">{workbench === "story"
            ? <>当前 workspace 还没有小说文件。可在右侧 Chat 中运行 <code>/story-setup</code>。</>
            : <>当前 workspace 还没有短剧项目。可在右侧 Chat 中运行 <code>/short-drama</code>。</>}</div>
        : buffer === undefined
          ? <div className="oh-story-empty">正在加载 {selected}…</div>
        : previewable && editorMode === "preview"
          ? markdown
            ? <MarkdownPreview content={buffer.content} label={selected} />
            : <JsonlPreview content={buffer.content} label={selected} />
          : <textarea
            value={buffer.content}
            data-format={structured ? "structured" : "prose"}
            onChange={(event) => {
              const content = event.target.value;
              setBuffers((current) => ({
                ...current,
                [selected]: {
                  content,
                  saved: current[selected]?.saved ?? "",
                  source: "human",
                  version: current[selected]?.version ?? ""
                }
              }));
            }}
            onKeyDown={(event) => {
              if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === "s") {
                event.preventDefault();
                void save();
              }
            }}
            spellCheck={!structured}
            aria-label={selected}
          />}
    </main>
  </div>;
}

/**
 * DSH's stable data-slot anchor is the supported addressable styling seam.
 * Portal the story surface beside the still-mounted official active view;
 * Chat, its child slots, store, composer, Todo, approvals, and scroll logic
 * remain owned by the official conversation plugin.
 */
function CreativeSplitBridge(props: Pick<ConvViewProps, "sessionId" | "useSession">) {
  const marker = useRef<HTMLSpanElement>(null);
  const [target, setTarget] = useState<HTMLElement>();
  useLayoutEffect(() => {
    const root = marker.current?.closest<HTMLElement>("[data-phase]");
    const anchor = root?.querySelector<HTMLElement>("[data-conversation-scroll] > [data-slot='conversation.session']");
    setTarget(anchor ?? undefined);
  }, [props.sessionId]);
  useLayoutEffect(() => {
    const scroller = target?.parentElement;
    if (scroller === undefined || scroller === null) return;
    const publishHeight = (): void => {
      scroller.style.setProperty("--oh-story-scroll-height", `${String(scroller.clientHeight)}px`);
    };
    publishHeight();
    const observer = new ResizeObserver(publishHeight);
    observer.observe(scroller);
    return () => {
      observer.disconnect();
      scroller.style.removeProperty("--oh-story-scroll-height");
    };
  }, [target]);
  return <>
    <span ref={marker} className="oh-story-bridge-marker" aria-hidden />
    {target === undefined ? null : createPortal(<CreativeWorkbench key={props.sessionId} {...props} />, target)}
  </>;
}

function argsOf(block: ToolCallViewProps["block"]): Record<string, unknown> {
  const raw = ("kind" in block ? block.call?.argsRaw : block.argsRaw) ?? "{}";
  try {
    const value = JSON.parse(raw) as unknown;
    return typeof value === "object" && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
  } catch { return {}; }
}

function resultOf(block: ToolCallViewProps["block"]): string | undefined {
  if (!("kind" in block)) return undefined;
  return block.content.map((item) => item.type === "text" ? item.text : JSON.stringify(item, null, 2)).join("\n");
}

function RoleToolView({ block, inspect }: ToolCallViewProps) {
  const args = argsOf(block);
  const role = typeof args.role === "string" ? args.role : "story-role";
  const output = resultOf(block);
  const state = !("kind" in block) ? "running" : block.isError ? "error" : "done";
  return <details className="oh-story-role" data-state={state}>
    <style>{styles}</style>
    <summary><span>✦ Role</span><strong>{role}</strong><em>{state === "running" ? "运行中" : state === "error" ? "失败" : "完成"}</em></summary>
    {output !== undefined && <pre>{output}</pre>}
    {inspect !== undefined && <button type="button" onClick={inspect}>在轨迹中检查</button>}
  </details>;
}

/** Register only official DSH surfaces; the split bridge never replaces Chat. */
export function apply(context: ClientContext): void {
  context.slots.inject("conversation.session.header.actions", () => context.slots.register({
    name: "conversation.session.header.actions",
    id: "oh-story-workspace",
    order: -100
  }, CreativeSplitBridge));
  context.slots.inject("tool.call.toolview", () => context.slots.register({
    name: "tool.call.toolview",
    key: "oh_story_role"
  }, RoleToolView));
}

export default { name, inject, apply };
