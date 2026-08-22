import { describe, expect, it } from "vitest";
import { workspaceContentVersion } from "../src/workspace-route.js";

describe("workspace content versions", () => {
  it("are stable for identical bytes and change with file content", () => {
    expect(workspaceContentVersion("雨夜\n")).toBe(workspaceContentVersion(Buffer.from("雨夜\n")));
    expect(workspaceContentVersion("雨夜\n")).not.toBe(workspaceContentVersion("雨夜。\n"));
  });
});
