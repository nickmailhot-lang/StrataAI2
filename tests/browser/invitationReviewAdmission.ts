import { expect, type Locator } from '@playwright/test';
import { openAdmittedReview } from './keyboardAdmission';

// Only a read-only review may reopen after fresh authority retires it. Observe
// the product's original safe default focus; never focus Cancel on its behalf
// or retry a confirmation/revocation. Keep the original five-second budget.
export async function openFocusedInvitationReview(launcher: Locator, dialog: Locator) {
  const cancel = dialog.getByRole('button', { name: 'Cancel', exact: true });
  await expect(async () => {
    await openAdmittedReview(launcher, dialog);
    await expect(cancel).toBeFocused({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
}
