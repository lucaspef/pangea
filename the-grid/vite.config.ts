import { defineConfig } from 'vitest/config';

export default defineConfig({
  // Caminhos relativos: o build funciona servido de qualquer subpasta.
  base: './',
  server: { port: 5173, open: false },
  build: {
    target: 'es2022',
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: { manualChunks: { three: ['three'] } },
    },
  },
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
  },
});
