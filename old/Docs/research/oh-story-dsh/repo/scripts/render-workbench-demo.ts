import { mkdtemp, rm } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const workbench = process.argv[2];
if (workbench !== "story" && workbench !== "drama" && workbench !== "all") throw new Error("Usage: render-workbench-demo.ts <story|drama|all>");

const frames = await mkdtemp(join(tmpdir(), `oh-story-dsh-${workbench}-demo-`));
const targets = workbench === "all" ? ["story", "drama"] as const : [workbench];

function run(command: string, args: readonly string[], env = process.env): void {
  const result = spawnSync(command, args, { cwd: repositoryRoot, env, encoding: "utf8", stdio: "inherit" });
  if (result.status !== 0) throw new Error(`Command failed: ${command} ${args.join(" ")}`);
}

try {
  run("pnpm", ["test:dsh"], {
    ...process.env,
    OH_STORY_DEMO_FRAMES_DIR: frames,
    OH_STORY_DEMO_USE_REAL_DEEPSEEK: "1"
  });
  for (const target of targets) {
    const output = resolve(repositoryRoot, target === "story"
      ? "docs/images/oh-story-dsh-demo.gif"
      : "docs/images/short-drama-dsh-demo.gif");
    run("ffmpeg", [
      "-v", "error",
      "-framerate", "1/2",
      "-start_number", "1",
      "-i", join(frames, `${target}-%02d.png`),
      "-filter_complex",
      "scale=1200:-2:flags=lanczos,split[original][palette];[palette]palettegen=max_colors=128:stats_mode=diff[p];[original][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle",
      "-loop", "0",
      "-y",
      output
    ]);
    process.stdout.write(`Rendered ${output}\n`);
  }
} finally {
  await rm(frames, { recursive: true, force: true });
}
