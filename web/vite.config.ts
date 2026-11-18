import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { existsSync, readFileSync } from 'node:fs';

// The release number lives in the repository's VERSION file (see docs/VERSIONING.md).
const versionFile = fileURLToPath(new URL('../VERSION', import.meta.url));
const appVersion = existsSync(versionFile) ? readFileSync(versionFile, 'utf8').trim() : '0.0.0-dev';

export default defineConfig({
  plugins: [react()],
  define: { __APP_VERSION__: JSON.stringify(appVersion) },
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    proxy: { '/api': { target: process.env.API_URL ?? 'http://127.0.0.1:5080', changeOrigin: false } },
  },
  build: {
    outDir: 'dist',
    chunkSizeWarningLimit: 1500,
    rollupOptions: { output: { manualChunks: { mantine: ['@mantine/core', '@mantine/hooks', '@mantine/dates', '@mantine/form'], charts: ['recharts', '@mantine/charts'] } } },
  },
});
