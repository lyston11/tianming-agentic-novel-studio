import { synchronizeAssets } from "./knowledge-assets.js";

const manifest = await synchronizeAssets();
process.stdout.write(
  `Synced ${manifest.skills.length} skills, ${manifest.roles.length} roles, ${manifest.files.length} files from ${manifest.upstream.commit.slice(0, 12)}.\n`
);
