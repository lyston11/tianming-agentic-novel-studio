import { stat } from "node:fs/promises";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { currentDramaFiles, dramaUpstreamRoot, readDramaManifest } from "./drama-assets.js";

const execFileAsync = promisify(execFile);
const manifest = await readDramaManifest();
const actualFiles = await currentDramaFiles();
if (JSON.stringify(actualFiles) !== JSON.stringify(manifest.files)) {
  throw new Error("Bundled Drama Skills files differ from manifest; run pnpm assets:sync:drama.");
}
if (manifest.skills.length !== 10 || !manifest.skills.every((name) => name === "short-drama" || name.startsWith("short-drama-"))) {
  throw new Error(`Expected 10 pinned Drama Skills, found ${String(manifest.skills.length)}.`);
}
const source = dramaUpstreamRoot();
if (process.env.DRAMA_SKILLS_UPSTREAM_DIR !== undefined && (await stat(source).catch(() => undefined))?.isDirectory()) {
  const { stdout } = await execFileAsync("git", ["-C", source, "rev-parse", "HEAD"], { encoding: "utf8" });
  if (stdout.trim() !== manifest.upstream.commit) throw new Error("Drama Skills upstream commit differs from the pinned manifest.");
}
process.stdout.write(`Drama parity OK: ${String(manifest.skills.length)} bundled skills at ${manifest.upstream.commit.slice(0, 12)}.\n`);
