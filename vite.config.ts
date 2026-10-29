import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    host: true,
    proxy: { '/ws': { target: 'ws://localhost:8080', ws: true } },
  },
  // the single-file artifact build inlines every chunk; the normal build keeps the 3D renderer on demand
  build: { target: 'es2022', sourcemap: false, rollupOptions: process.env.VITE_SOLO_ONLY ? { output: { inlineDynamicImports: true } } : {} },
});
