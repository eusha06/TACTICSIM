import { defineConfig } from 'vitest/config';

// Builds to the ASP.NET wwwroot, served by the Razor page /play.
// Fixed entry names let Razor add its own cache-busting version hash.
export default defineConfig({
  base: '/game/',
  build: {
    outDir: '../src/TacticSim.Web/wwwroot/game',
    emptyOutDir: true,
    chunkSizeWarningLimit: 2000,
    rollupOptions: {
      input: 'src/main.ts',
      output: {
        entryFileNames: 'game.js',
        chunkFileNames: 'game-[name].js',
        assetFileNames: (asset) => (asset.names?.[0]?.endsWith('.css') ? 'game.css' : 'assets/[name]-[hash][extname]'),
      },
    },
  },
  // npm run dev: Vite on :5173, API calls proxied to the .NET server on :5178.
  server: { proxy: { '/api': 'http://localhost:5178' } },
  test: { include: ['test/**/*.test.ts'] },
});
