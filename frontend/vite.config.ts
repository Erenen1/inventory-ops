/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // Same-origin in development too: the API is proxied, so cookies and CSRF behave as in production.
    proxy: { '/api': { target: process.env.VITE_API_PROXY ?? 'http://127.0.0.1:5080', changeOrigin: false } },
  },
  build: {
    sourcemap: false,
    // The shared vendor chunk (~715 kB, ~220 kB gzip) is cached across releases; screens are lazy-loaded.
    chunkSizeWarningLimit: 800,
    rollupOptions: {
      output: {
        // Third-party code in one long-lived chunk; Recharts separately because only the dashboard needs it.
        // (Finer vendor splits created circular chunk initialisation with React.)
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined;
          return /recharts|d3-|victory-vendor/.test(id) ? 'charts' : 'vendor';
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
