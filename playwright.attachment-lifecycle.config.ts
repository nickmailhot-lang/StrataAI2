import { defineConfig } from '@playwright/test';
import release from './playwright.config';
export default defineConfig({ ...release, testMatch: 'attachment-source-archive-pipeline.case.ts',
  outputDir: 'test-results/attachment-source-archive-pipeline' });
