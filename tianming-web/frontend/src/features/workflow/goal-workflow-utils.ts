export function draftContent(contentJson: string) {
  try {
    const parsed = JSON.parse(contentJson) as { draftContent?: string };
    return parsed.draftContent ?? contentJson;
  } catch {
    return contentJson;
  }
}
