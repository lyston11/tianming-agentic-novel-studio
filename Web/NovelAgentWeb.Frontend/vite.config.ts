import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3002,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5002',
        changeOrigin: true,
        secure: false,
        ws: true,
        configure: (proxy) => {
          proxy.on('error', (err) => {
            console.log('proxy error', err);
          });
          proxy.on('proxyReq', () => {
            console.log('Sending Request to the Target');
          });
          proxy.on('proxyRes', (proxyRes) => {
            console.log('Received Response from the Target:', proxyRes.statusCode);
          });
        },
      },
    },
  },
})
