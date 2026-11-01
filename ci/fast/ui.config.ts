import { defineConfig } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export default defineConfig({
  testDir: '.',
  testMatch: 'ui.smoke.spec.ts',
  retries: 0,
  workers: 1,
  timeout: 30_000,
  expect: { timeout: 5_000 },
  outputDir: process.env.FAST_CI_UI_RESULTS ?? join(tmpdir(), 'strataai-fast-ui-results'),
  reporter: [['list']],
  use: { baseURL: 'http://127.0.0.1:4187', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: {
    command: 'npm exec --workspace @strataai/web -- vite preview --host 127.0.0.1 --port 4187 --strictPort',
    url: 'http://127.0.0.1:4187/login',
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
