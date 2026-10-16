import { defineConfig } from '@playwright/test';
import release from './playwright.config';

export default defineConfig({ ...release, testMatch: 'recipient-invitation-clock.case.ts', outputDir: 'test-results/recipient-invitation-clock' });
