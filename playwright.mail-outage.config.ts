import { defineConfig } from '@playwright/test';
import release from './playwright.config';
export default defineConfig({ ...release, testMatch: 'core-mail-outage.case.ts',
  outputDir: 'test-results/core-mail-outage' });
