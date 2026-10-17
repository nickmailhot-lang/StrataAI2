import { expect, type Page } from './releaseTest';

// Opening this form has no write side effect. A live admission recheck can
// disable the launcher between focus and Enter, so confirm permitted entry
// using the existing Board fixture's bounded five-second pattern. Create
// submissions remain single explicit activations and are never replayed here.
export async function openCardCreation(page: Page, listName: string) {
  const dialog = page.getByRole('dialog', { name: 'Add card', exact: true });
  await expect(async () => {
    if (await dialog.isVisible()) return;
    const launcher = page.getByRole('button', { name: `Add card to ${listName}`, exact: true });
    if (await launcher.isVisible() && await launcher.isEnabled({ timeout: 500 })) {
      await launcher.focus({ timeout: 500 });
      await expect(launcher).toBeFocused({ timeout: 500 });
      await expect(launcher).toBeEnabled({ timeout: 500 });
      await launcher.press('Enter', { timeout: 500 });
    }
    await expect(dialog).toBeVisible({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
  return dialog;
}
