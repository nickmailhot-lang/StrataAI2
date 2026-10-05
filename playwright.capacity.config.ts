import { defineConfig } from '@playwright/test';
import release from './playwright.config';
export default defineConfig({ ...release, testMatch: 'board-capacity.case.ts', outputDir: 'test-results/board-capacity' });
