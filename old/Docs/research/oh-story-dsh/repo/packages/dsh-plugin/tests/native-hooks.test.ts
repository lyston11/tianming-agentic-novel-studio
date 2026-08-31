import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { ToolExecution } from "@deepseek-ai/dsh-tools";
import { afterEach, describe, expect, it } from "vitest";
import { decideStoryMutation, detectStoryMutation, validateStoryMutation } from "../src/native-hooks.js";

const roots: string[] = [];

afterEach(async () => {
  await Promise.all(roots.splice(0).map((root) => rm(root, { recursive: true, force: true })));
});

async function project(): Promise<string> {
  const root = await mkdtemp(join(tmpdir(), "oh-story-hook-"));
  roots.push(root);
  await mkdir(join(root, "大纲"));
  await mkdir(join(root, "追踪"));
  return root;
}

describe("native DSH prose guards", () => {
  it("recognizes both DSH filesystem tool families and ignores editor views", () => {
    expect(detectStoryMutation("write", { file_path: "正文/第002章.md" }, "/books/demo"))
      .toMatchObject({ path: "正文/第002章.md", chapter: 2 });
    expect(detectStoryMutation("str_replace_editor", {
      command: "str_replace",
      path: "/books/demo/正文/第003章.md"
    }, "/books/demo")).toMatchObject({ path: "正文/第003章.md", chapter: 3 });
    expect(detectStoryMutation("str_replace_editor", {
      command: "view",
      path: "/books/demo/正文/第003章.md"
    }, "/books/demo")).toBeUndefined();
  });

  it("allows setup and import to bootstrap prose before canonical Tracking exists", async () => {
    const root = await project();
    await expect(validateStoryMutation({ root, path: "正文/第002章.md", chapter: 2 }))
      .resolves.toBeUndefined();
  });

  it("requires the matching chapter outline", async () => {
    const root = await project();
    await writeFile(join(root, "追踪", "_tracking-state.json"), "{}\n");
    await expect(validateStoryMutation({ root, path: "正文/第002章.md", chapter: 2 }))
      .resolves.toContain("细纲");
  });

  it("allows a mutation when Tracking and the matching outline exist", async () => {
    const root = await project();
    await writeFile(join(root, "追踪", "_tracking-state.json"), "{}\n");
    await writeFile(join(root, "大纲", "细纲_第002章_回声.md"), "# 第二章\n");
    await expect(validateStoryMutation({ root, path: "正文/第002章.md", chapter: 2 }))
      .resolves.toBeUndefined();
  });

  it("preserves DSH's downstream permission decision instead of forcing ask", async () => {
    const root = await project();
    const exec = {
      name: "write",
      arguments: { file_path: "正文/第002章.md" },
      agent: { session: { header: { cwd: root } } }
    } as unknown as ToolExecution;
    await expect(decideStoryMutation(exec, async () => ({ kind: "allow" })))
      .resolves.toEqual({ kind: "allow" });
  });

  it("does not impose long-form guards on a plain short-story workspace", async () => {
    const root = await mkdtemp(join(tmpdir(), "oh-story-hook-short-"));
    roots.push(root);
    await expect(validateStoryMutation({ root, path: "正文/短篇.md" })).resolves.toBeUndefined();
  });
});
