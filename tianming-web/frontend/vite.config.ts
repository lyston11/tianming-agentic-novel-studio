import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'
import { loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, '')
  const configured = (key: string) => (env[key] || process.env[key] || '').trim()

  // The dev server proxies /api to the legacy ASP.NET backend so the browser
  // always issues same-origin requests. Port 3002/5002 are fixed by the root
  // README and must not change.
  const backendOrigin = configured('TIANMING_BACKEND_ORIGIN') || 'http://127.0.0.1:5002'

  return {
    plugins: [react(), tailwindcss()],
    server: {
      host: configured('TIANMING_LISTEN_HOST') || '127.0.0.1',
      port: 3002,
      strictPort: true,
      proxy: {
        '/api': {
          target: backendOrigin,
          changeOrigin: true,
          secure: false,
          ws: true,
        },
      },
    },
    build: {
      chunkSizeWarningLimit: 800,
      // Route code stays separate from large, slow-changing dependencies.
      // The react/motion/markdown groups each hold cyclic module graphs; if
      // rolldown scatters them across chunks, shared constants become
      // undefined at module-init and whole routes white-screen. Keep each
      // family in exactly ONE chunk (learngraph-proven configuration).
      rolldownOptions: {
        output: {
          codeSplitting: {
            groups: [
              {
                name: 'vendor',
                test: /node_modules[\\/]/,
                priority: 10,
                entriesAware: true,
                minSize: 20_000,
                maxSize: 320_000,
              },
              {
                name: 'react',
                test: /node_modules[\\/](react|react-dom|react-is|react-router|react-router-dom|scheduler)[\\/]/,
                priority: 30,
                entriesAware: false,
                minSize: 0,
                maxSize: Number.POSITIVE_INFINITY,
              },
              {
                name: 'motion',
                test: /node_modules[\\/](motion|framer-motion|motion-dom|motion-utils)[\\/]/,
                priority: 30,
                entriesAware: false,
                minSize: 0,
                maxSize: Number.POSITIVE_INFINITY,
              },
              {
                name: 'markdown-render',
                test: /node_modules[\\/](streamdown|micromark(-[a-z0-9-]+)?|mdast-util-[a-z0-9-]+|hast-util-[a-z0-9-]+|unist-util-[a-z0-9-]+|remark(-[a-z0-9-]+)?|rehype(-[a-z0-9-]+)?|unified|vfile(-[a-z0-9-]+)?|markdown-table|lowlight|refractor|character-entities(-legacy)?|decode-named-character-reference|property-information|space-separated-tokens|comma-separated-tokens|html-void-elements|web-namespaces|zwitch|trim-lines|ccount|longest-streak|escape-string-regexp|bail|extend|trough|is-plain-obj|inline-style-parser|style-to-object|style-to-js|html-url-attributes|estree-util-[a-z0-9-]+|@ungap|remend|rehype-harden)[\\/]/,
                priority: 30,
                entriesAware: false,
                minSize: 0,
                maxSize: Number.POSITIVE_INFINITY,
              },
            ],
          },
        },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      clearMocks: true,
      mockReset: true,
      restoreMocks: true,
      globals: false,
    },
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
  }
})
