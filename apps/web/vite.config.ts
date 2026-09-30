import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import { createBuildIdentity } from './src/app/buildIdentity.ts';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VITE_');
  const buildIdentity = createBuildIdentity(env.VITE_STRATAAI_BUILD_REVISION, env.VITE_STRATAAI_BUILD_VERSION);
  return {
  plugins: [react(), {
    name: 'strataai-build-identity',
    apply: 'build',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'build-metadata.json', source: JSON.stringify(buildIdentity) });
    },
  }],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
      '^/(auth|me|organizations|boards|lists|cards|invitations)(/|$)': {
        target: 'http://localhost:8080',
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
