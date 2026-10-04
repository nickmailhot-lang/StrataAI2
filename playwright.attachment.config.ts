import { defineConfig } from '@playwright/test';
import release from './playwright.config';
export default defineConfig({ ...release, testMatch: 'board-background-pipeline.case.ts',
  outputDir: 'test-results/attachment-pipeline' });
