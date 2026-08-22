import type { AssistantBlock, RunningToolCall } from "@deepseek-ai/dsh-client-runtime/client";
import { describe, expect, it } from "vitest";
import {
  activeFileMutation,
  creativeRelativePath,
  jsonStringPrefix,
  previewMutation,
  storyRelativePath,
  workbenchModeForPath
} from "../src/client/file-activity.js";

describe("official DSH file activity projection", () => {
  it("decodes a still-streaming JSON string prefix", () => {
    expect(jsonStringPrefix('{"file_path":"正文/第011章.md","content":"雨声\\n越来', "content"))
      .toEqual({ value: "雨声\n越来", complete: false });
    expect(jsonStringPrefix('{"content":"雨声\\n越来。"}', "content"))
      .toEqual({ value: "雨声\n越来。", complete: true });
  });

  it("projects a streaming write call before tool execution starts", () => {
    const blocks: AssistantBlock[] = [{
      kind: "tool-call",
      callId: "write-1",
      name: "write",
      argsRaw: '{"file_path":"正文/第011章.md","content":"第一行\\n第二'
    }];
    const activity = activeFileMutation(blocks, []);
    expect(activity).toMatchObject({
      callId: "write-1",
      stage: "streaming",
      path: "正文/第011章.md",
      operation: "replace-file",
      newText: "第一行\n第二"
    });
    expect(previewMutation(activity!, "旧正文")).toBe("第一行\n第二");
  });

  it("uses the executing DSH call and previews targeted edits", () => {
    const running = [{
      callId: "edit-1",
      name: "edit",
      argsRaw: '{"file_path":"正文/第002章.md","old_string":"旧句","new_string":"新句正在生成',
      turn: 1,
      step: 1,
      time: 1,
      callView: null,
      subCalls: []
    }] as RunningToolCall[];
    const activity = activeFileMutation([], running);
    expect(activity).toMatchObject({ stage: "running", oldText: "旧句", newText: "新句正在生成" });
    expect(previewMutation(activity!, "开头。旧句。结尾。")).toBe("开头。新句正在生成。结尾。");
  });

  it("accepts only editable story paths inside the current DSH workspace", () => {
    const cwd = "/books/demo";
    expect(storyRelativePath("/books/demo/正文/第003章.md", cwd)).toBe("正文/第003章.md");
    expect(storyRelativePath("设定/人物.json", cwd)).toBe("设定/人物.json");
    expect(storyRelativePath("/books/demo/src/app.ts", cwd)).toBeUndefined();
    expect(storyRelativePath("../正文/逃逸.md", cwd)).toBeUndefined();
  });

  it("recognizes short-drama files and chooses the matching workbench", () => {
    const cwd = "/shows/demo";
    expect(creativeRelativePath("/shows/demo/剧集/第01集.md", cwd)).toBe("剧集/第01集.md");
    expect(creativeRelativePath("short-drama.json", cwd)).toBe("short-drama.json");
    expect(creativeRelativePath(".short-drama/private.json", cwd)).toBeUndefined();
    expect(workbenchModeForPath("剧集/第01集.md")).toBe("drama");
    expect(workbenchModeForPath("正文/第001章.md")).toBe("story");
  });
});
