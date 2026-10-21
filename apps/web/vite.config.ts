import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import { createBuildIdentity } from './src/app/buildIdentity.ts';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VITE_');
  const apiTarget = env.VITE_STRATAAI_API_TARGET || 'http://localhost:8080';
  const buildIdentity = createBuildIdentity(env.VITE_STRATAAI_BUILD_REVISION, env.VITE_STRATAAI_BUILD_VERSION);
  return {
  plugins: [react(), {
    name: 'strataai-build-identity',
    apply: 'build',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'build-metadata.json', source: JSON.stringify(buildIdentity) });
    },
  }],
  // The lazy Board consumer must share the initial optimized React graph.
  // Discovering its new icon mid-navigation can invalidate a cold dev graph.
  optimizeDeps: { include: ['@mui/icons-material/DragIndicator'] },
  server: {
    port: 5173,
    proxy: {
      '/organizations/live': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
      '/notifications/live': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
      '/invitations/live': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
      '/me/live': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
      '/boards/live': {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
      '^/(auth|me|organizations|boards|lists|cards|attachments|labels|watch|search|navigation|invitations)([/?]|$)': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
  test: {
    include: ['src/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
    css: true,
  },
  };
});
