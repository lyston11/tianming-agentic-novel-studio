import { useEffect, useState, type ReactNode } from 'react';

/**
 * Lazy-loaded Streamdown markdown renderer (learngraph pattern).
 *
 * The markdown parse chain is heavy; it lives in the markdown-render chunk
 * and loads on first render. While loading (or if the import fails) the raw
 * text renders as preformatted fallback. @streamdown/math is intentionally
 * not installed — a novel studio has no use for LaTeX, and it drags KaTeX in.
 */
type StreamdownLike = (props: Record<string, unknown>) => ReactNode;
type StreamdownModule = {
  Streamdown: StreamdownLike;
  plugins: Record<string, unknown>;
};

function loadStreamdown(): Promise<StreamdownModule> {
  return Promise.all([
    import('streamdown'),
    import('@streamdown/cjk'),
    import('@streamdown/code'),
  ]).then(([streamdown, cjk, code]) => ({
    Streamdown: streamdown.Streamdown as StreamdownLike,
    plugins: {
      cjk: cjk.cjk,
      code: code.code,
    },
  }));
}

let streamdownCached: Promise<StreamdownModule> | undefined;

export function MarkdownContent({ children, ...props }: { children: ReactNode; [key: string]: unknown }) {
  const [module, setModule] = useState<StreamdownModule | null>(null);

  useEffect(() => {
    let cancelled = false;
    streamdownCached ??= loadStreamdown();
    streamdownCached
      .then((loaded) => {
        if (!cancelled) setModule(loaded);
      })
      .catch(() => {
        if (!cancelled) setModule(null);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!module) {
    return <div className="whitespace-pre-wrap">{children}</div>;
  }

  const { Streamdown, plugins } = module;
  return (
    <Streamdown {...props} plugins={plugins}>
      {children}
    </Streamdown>
  );
}
