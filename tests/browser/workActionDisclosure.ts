import type { Page } from '@playwright/test';
import { expect } from './releaseTest';

// Follow the same named, keyboard-operated entry point as a participant. Keep
// each caller's original role, disabled, receipt and recovery assertions intact.
export async function disclosedWorkControl(page: Page, label: string, role: 'button' | 'link', options: { name: string; exact?: boolean; includeHidden?: boolean }) {
  if (!options.includeHidden && process.env.STRATAAI_E2E_WORK_UX !== '0') {
    const summary = page.locator('details > summary').filter({ hasText: new RegExp(`^${label}$`) });
    await expect(summary).toBeVisible();
    if (!await summary.evaluate(element => element.parentElement!.hasAttribute('open'))) await summary.press('Enter');
  }
  return page.getByRole(role, options);
}
