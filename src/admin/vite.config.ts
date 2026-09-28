import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

const contractsDir = fileURLToPath(new URL('../../docs/contracts', import.meta.url))

// FR-035: the admin app is built into the host's wwwroot at image build time. No Node
// process runs at runtime.
// FR-043: the app is served under /admin, so every asset URL must be prefixed.
// FR-046: Vite's content-hashed filenames are what make an upgrade unable to serve a
// stale mix of old and new assets.
export default defineConfig({
  base: '/admin/',
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // 011 ED-08: the page schema and design token contracts are imported from where they
      // live, never copied, so the editor validates against the exact file the server embeds.
      // In the image build the Dockerfile copies docs/contracts/ to the same relative place.
      '@contracts': contractsDir,
    },
  },
  build: {
    outDir: '../QuickBuilder.Host/wwwroot/admin',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name].[hash].js',
        chunkFileNames: 'assets/[name].[hash].js',
        assetFileNames: 'assets/[name].[hash][extname]',
      },
    },
  },
  server: {
    // The contracts sit outside the app root; the dev server may serve them (ED-08).
    fs: { allow: ['.', contractsDir] },
    // Dev only: proxy the API to the running host so `npm run dev` works against it.
    proxy: {
      '/api': 'http://127.0.0.1:8080',
      '/health': 'http://127.0.0.1:8080',
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['tests/**/*.spec.ts'],
  },
})
