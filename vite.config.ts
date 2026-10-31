import { execSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { defineConfig, type Plugin } from 'vite';

/** Short id of this build: the commit when there is one. It names the offline cache and shows in Menu → About. */
function buildId(): string {
  const fromEnv = process.env.VERCEL_GIT_COMMIT_SHA || process.env.GITHUB_SHA;
  if (fromEnv) return fromEnv.slice(0, 7);
  try {
    return execSync('git rev-parse --short HEAD', { stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim();
  } catch {
    return 'dev';
  }
}
const BUILD = buildId();

/** Public address of the site, when the build knows it: link previews need absolute addresses. */
function siteUrl(): string {
  const v = process.env.SITE_URL || process.env.VERCEL_PROJECT_PRODUCTION_URL || '';
  if (!v) return '';
  return (/^https?:\/\//.test(v) ? v : `https://${v}`).replace(/\/+$/, '');
}

const DESCRIPTION = 'Found cities, discover the ages and outwit your friends. A turn-based strategy game for 1–8 players that runs in your browser: solo, hot-seat or online.';

function jericho(): Plugin {
  const site = siteUrl();
  let outDir = 'dist';
  return {
    name: 'jericho-pages',
    configResolved(c) { outDir = c.build.outDir; },
    transformIndexHtml() {
      const abs = (p: string) => (site ? `${site}/${p}` : p);
      const meta = (attrs: Record<string, string>) => ({ tag: 'meta', attrs, injectTo: 'head' as const });
      return [
        meta({ name: 'description', content: DESCRIPTION }),
        meta({ property: 'og:type', content: 'website' }),
        meta({ property: 'og:site_name', content: 'JERICHO' }),
        meta({ property: 'og:title', content: 'JERICHO — build an empire, play with friends' }),
        meta({ property: 'og:description', content: DESCRIPTION }),
        meta({ property: 'og:image', content: abs('og.jpg') }),
        meta({ property: 'og:image:width', content: '1200' }),
        meta({ property: 'og:image:height', content: '630' }),
        meta({ property: 'og:image:alt', content: 'JERICHO: an ancient walled town among palms' }),
        ...(site ? [meta({ property: 'og:url', content: `${site}/` })] : []),
        meta({ name: 'twitter:card', content: 'summary_large_image' }),
        meta({ name: 'twitter:title', content: 'JERICHO — build an empire, play with friends' }),
        meta({ name: 'twitter:description', content: DESCRIPTION }),
        meta({ name: 'twitter:image', content: abs('og.jpg') }),
      ];
    },
    // the offline cache is named after the build, so a new build starts clean and the old one is dropped
    writeBundle() {
      const file = join(outDir, 'sw.js');
      try {
        writeFileSync(file, readFileSync(file, 'utf8').replaceAll('__BUILD_ID__', BUILD));
      } catch { /* no service worker in this build */ }
    },
  };
}

export default defineConfig({
  plugins: [jericho()],
  define: { __BUILD_ID__: JSON.stringify(BUILD) },
  server: {
    host: true,
    proxy: { '/ws': { target: 'ws://localhost:8080', ws: true }, '/healthz': { target: 'http://localhost:8080' } },
  },
  // the single-file artifact build inlines every chunk; the normal build keeps the 3D renderer on demand
  build: { target: 'es2022', sourcemap: false, rollupOptions: process.env.VITE_SOLO_ONLY ? { output: { inlineDynamicImports: true } } : {} },
});
