import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import type { Plugin } from 'vite';

// The release number lives in the repository's VERSION file (see docs/VERSIONING.md).
const versionFile = fileURLToPath(new URL('../VERSION', import.meta.url));
const appVersion = existsSync(versionFile) ? readFileSync(versionFile, 'utf8').trim() : '0.0.0-dev';

/** Every file of the build, for the technician view's service worker (public/tech-sw.js) to keep on the phone. */
function techPrecache(): Plugin {
  let outDir = 'dist';
  return {
    name: 'tech-precache',
    apply: 'build',
    configResolved(c) { outDir = resolve(c.root, c.build.outDir); },
    writeBundle() {
      const walk = (dir: string): string[] => readdirSync(dir).flatMap((f) => {
        const p = join(dir, f);
        return statSync(p).isDirectory() ? walk(p) : [relative(outDir, p).split('\\').join('/')];
      });
      const files = walk(outDir).filter((f) => !f.endsWith('.map') && f !== 'tech-precache.json' && f !== 'tech-sw.js' && !f.startsWith('.'));
      // Every build gets its own version, so phones replace their copy with the new release.
      const version = `${appVersion}-${Date.now().toString(36)}`;
      writeFileSync(join(outDir, 'tech-precache.json'), JSON.stringify({ version, files }));
    },
  };
}

export default defineConfig({
  plugins: [react(), techPrecache()],
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
