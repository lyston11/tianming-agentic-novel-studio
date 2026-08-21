import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { createDramaSkillProvider, createOhStorySkillProvider, parseBundledSkill } from "../src/skill-provider.js";

const skillRoot = resolve(import.meta.dirname, "../../knowledge/oh-story/skills");
const dramaRoot = resolve(import.meta.dirname, "../../knowledge/drama/skills");

describe("Oh Story bundled skill provider", () => {
  it("publishes the complete upstream capability catalog with a DSH bridge", async () => {
    const provider = createOhStorySkillProvider(skillRoot);
    const listed = await provider.list({});
    if (!Array.isArray(listed)) throw new Error("Expected a complete bundled catalog.");
    const candidates = listed;
    expect(candidates).toHaveLength(13);
    expect(candidates.map((candidate) => candidate.name)).toContain("story-long-write");
    expect(candidates.every((candidate) => candidate.source === "bundled" && candidate.invocation.modelInvocable)).toBe(true);
    const selected = candidates.find((candidate) => candidate.name === "story-long-write");
    expect(selected).toBeDefined();
    const skill = await provider.get(selected!, {});
    expect(skill?.content).toContain("# story-long-write");
    expect(skill?.content).toContain("oh_story_role");
    expect(skill?.content).toContain("DSH owns the workspace, model, preset, permissions, Session Log");
    expect(skill?.content).toContain("Keep the upstream writing, Tracking, lint, outline, revision, and quality workflows");
    expect(skill?.content.startsWith("---")).toBe(false);
    const setupCandidate = candidates.find((candidate) => candidate.name === "story-setup");
    const setup = await provider.get(setupCandidate!, {});
    expect(setup?.content).toContain("never deploy Claude/OpenCode/Codex/ZCode/OpenClaw/Reasonix files");
    expect(setup?.content).not.toContain("merge-codex-hooks.py");
    const routeCandidate = candidates.find((candidate) => candidate.name === "story");
    const route = await provider.get(routeCandidate!, {});
    expect(route?.content).toContain("The 小说 workspace is an official DSH conversation view");
    expect(route?.content).not.toContain("dashboard-server.mjs");
    const browserCandidate = candidates.find((candidate) => candidate.name === "browser-cdp");
    const browser = await provider.get(browserCandidate!, {});
    expect(browser?.content).not.toContain("setup-cdp-chrome.js 9222");
    for (const name of ["story-long-scan", "story-short-scan"]) {
      const candidate = candidates.find((value) => value.name === name);
      const scan = await provider.get(candidate!, {});
      expect(scan?.content).toContain("当前 DSH Preset 可见的网页工具");
      expect(scan?.content).not.toContain("rank-scraper.js");
      expect(scan?.content).not.toContain("WebFetch");
      expect(scan?.content).not.toContain("Bearer token");
    }
  });

  it("rejects missing frontmatter", () => {
    expect(() => parseBundledSkill("# no metadata")).toThrow(/frontmatter/u);
  });

  it("rejects candidate paths outside the packaged skill root", async () => {
    const provider = createOhStorySkillProvider(skillRoot);
    await expect(provider.get({
      name: "story-long-write",
      description: "invalid external candidate",
      invocation: { modelInvocable: true, userInvocable: true },
      provider: "oh-story",
      source: "bundled",
      resourceBase: { kind: "directory", path: resolve(skillRoot, "..") },
      rank: 0,
      locator: new URL("file:///tmp/SKILL.md"),
      path: resolve(skillRoot, "../SKILL.md")
    }, {})).rejects.toThrow(/escaped/u);
  });
});

describe("Drama Skills bundled provider", () => {
  it("publishes the complete upstream short-drama workflow through DSH", async () => {
    const provider = createDramaSkillProvider(dramaRoot);
    const listed = await provider.list({});
    if (!Array.isArray(listed)) throw new Error("Expected a complete Drama Skills catalog.");
    expect(listed).toHaveLength(10);
    expect(listed.map((candidate) => candidate.name)).toEqual(expect.arrayContaining([
      "short-drama", "short-drama-write", "short-drama-storyboard", "short-drama-produce"
    ]));
    const routeCandidate = listed.find((candidate) => candidate.name === "short-drama");
    const route = await provider.get(routeCandidate!, {});
    expect(route?.content).toContain("native 短剧 tab");
    const productionCandidate = listed.find((candidate) => candidate.name === "short-drama-produce");
    const production = await provider.get(productionCandidate!, {});
    expect(production?.content).toContain("explicitly confirms the exact current job");
    expect(production?.content).toContain("DSH permissions and approval UI");
  });
});
