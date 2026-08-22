import { lstat, realpath } from "node:fs/promises";
import { isAbsolute, relative, resolve, sep } from "node:path";

export class PathSecurityError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PathSecurityError";
  }
}

function isInside(root: string, candidate: string): boolean {
  const offset = relative(root, candidate);
  return offset === "" || (!offset.startsWith(`..${sep}`) && offset !== ".." && !isAbsolute(offset));
}

export async function canonicalWorkspaceRoot(root: string): Promise<string> {
  const canonical = await realpath(root);
  const info = await lstat(canonical);
  if (!info.isDirectory()) throw new PathSecurityError("Workspace root must be a directory.");
  return canonical;
}

export async function resolveWorkspacePath(
  root: string,
  relativePath: string,
  options: { readonly allowMissing?: boolean; readonly expect?: "file" | "directory" } = {}
): Promise<string> {
  if (relativePath.includes("\0") || isAbsolute(relativePath) || /^[A-Za-z]:[\\/]/u.test(relativePath)) {
    throw new PathSecurityError("Only workspace-relative paths are allowed.");
  }
  const segments = relativePath.replaceAll("\\", "/").split("/");
  if (segments.some((segment) => segment === ".." || segment === "")) {
    throw new PathSecurityError("Path traversal and empty segments are not allowed.");
  }
  const canonicalRoot = await canonicalWorkspaceRoot(root);
  const candidate = resolve(canonicalRoot, ...segments);
  if (!isInside(canonicalRoot, candidate)) throw new PathSecurityError("Path escapes the workspace.");

  let cursor = canonicalRoot;
  for (let index = 0; index < segments.length; index += 1) {
    cursor = resolve(cursor, segments[index] ?? "");
    try {
      const info = await lstat(cursor);
      if (info.isSymbolicLink()) {
        const target = await realpath(cursor);
        if (!isInside(canonicalRoot, target)) throw new PathSecurityError("Symbolic link escapes the workspace.");
        cursor = target;
      }
      if (index < segments.length - 1 && !info.isDirectory()) throw new PathSecurityError("A path parent is not a directory.");
      if (index === segments.length - 1 && options.expect !== undefined) {
        if (options.expect === "file" && !info.isFile()) throw new PathSecurityError("Expected a regular file.");
        if (options.expect === "directory" && !info.isDirectory()) throw new PathSecurityError("Expected a directory.");
      }
    } catch (error: unknown) {
      if (error instanceof PathSecurityError) throw error;
      const code = error instanceof Error && "code" in error ? String(error.code) : "";
      if (code === "ENOENT" && options.allowMissing === true && index === segments.length - 1) return cursor;
      throw error;
    }
  }
  const finalPath = await realpath(cursor);
  if (!isInside(canonicalRoot, finalPath)) throw new PathSecurityError("Resolved path escapes the workspace.");
  return finalPath;
}
