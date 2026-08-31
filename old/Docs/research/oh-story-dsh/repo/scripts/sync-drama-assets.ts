import { synchronizeDramaAssets } from "./drama-assets.js";

const manifest = await synchronizeDramaAssets();
process.stdout.write(`Synced ${String(manifest.skills.length)} Drama Skills from ${manifest.upstream.commit.slice(0, 12)}.\n`);
