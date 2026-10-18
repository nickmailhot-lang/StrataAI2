import type { Locator, Page } from '@playwright/test';
import { openAdmittedReview } from './keyboardAdmission';

// Read-only entry: observe the permitted destination after native disclosure.
// The rollback build already exposes it inline; no activation occurs there.
export async function openOrganizationActions(page: Page, destination: Locator) {
  await openAdmittedReview(page.locator('summary').filter({ hasText: /^Organization actions$/ }), destination);
}
