import { mkdtemp, mkdir, realpath, rm, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { canonicalWorkspaceRoot, PathSecurityError, resolveWorkspacePath } from "../src/workspace-security.js";

describe("workspace path security", () => {
  it("resolves files and missing write targets inside the workspace", async () => {
    const root = await mkdtemp(join(tmpdir(), "oh-story-path-"));
    try {
      await mkdir(join(root, "剧集"));
      await writeFile(join(root, "剧集", "第01集.md"), "雨夜");
      const canonical = await canonicalWorkspaceRoot(root);
      expect(canonical).toBe(await realpath(root));
      await expect(resolveWorkspacePath(root, "剧集/第01集.md", { expect: "file" }))
        .resolves.toBe(join(canonical, "剧集", "第01集.md"));
      await expect(resolveWorkspacePath(root, "剧集/第02集.md", { allowMissing: true }))
        .resolves.toBe(join(canonical, "剧集", "第02集.md"));
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  });

  it("rejects traversal, absolute paths and symlink escape", async () => {
    const root = await mkdtemp(join(tmpdir(), "oh-story-path-"));
    const outside = await mkdtemp(join(tmpdir(), "oh-story-outside-"));
    try {
      await mkdir(join(root, "正文"));
      await writeFile(join(outside, "secret.md"), "secret");
      await symlink(join(outside, "secret.md"), join(root, "正文", "escape.md"));
      await expect(resolveWorkspacePath(root, "../secret.md")).rejects.toBeInstanceOf(PathSecurityError);
      await expect(resolveWorkspacePath(root, "/tmp/secret.md")).rejects.toBeInstanceOf(PathSecurityError);
      await expect(resolveWorkspacePath(root, "C:\\outside\\secret.md")).rejects.toBeInstanceOf(PathSecurityError);
      await expect(resolveWorkspacePath(root, "正文/escape.md")).rejects.toBeInstanceOf(PathSecurityError);
    } finally {
      await rm(root, { recursive: true, force: true });
      await rm(outside, { recursive: true, force: true });
    }
  });
});
