import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

// Controlled HTTP responses isolate shell interaction from API/provider setup.
// Real authorization and persisted-workflow acceptance remain in surface-admission
// and navigation-observations; this file does not substitute for those suites.
for (const width of [1280, 768, 390, 320]) {
  test(`UX-001 controlled shell keyboard, reflow and accessibility at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    let admitted = true;
    await page.route('**/*', async route => {
      const request = route.request();
      if (!['fetch', 'xhr'].includes(request.resourceType())) return route.continue();
      const path = new URL(request.url()).pathname;
      if (path.endsWith('/surface-access')) return route.fulfill({ status: admitted ? 200 : 404,
        json: admitted ? { organizationId: 'demo', surface: 'INTERNAL' } : {} });
      if (path === '/boards/demo-board') return route.fulfill({ json: {
        board: { id: 'demo-board', organizationId: 'demo', name: 'Shell test Board', lifecycleState: 'active' },
        lists: [], access: { canView: true, canEdit: false },
      } });
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto('/app/demo/boards/demo-board');
    await expect(page.getByRole('heading', { name: 'Shell test Board', exact: true })).toBeVisible();
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: 'Skip to main content' });
    await expect(skip).toBeFocused(); await skip.press('Enter');
    await expect(page.getByRole('main')).toBeFocused();
    await expect(page).toHaveURL(/\/boards\/demo-board$/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    const trigger = page.getByRole('button', { name: 'Open navigation', exact: true });
    if (width < 600) {
      await trigger.focus(); await trigger.press('Enter');
      const drawer = page.getByRole('navigation', { name: 'Mobile application navigation' });
      await expect(drawer).toBeVisible();
      const close = drawer.getByRole('button', { name: 'Close navigation', exact: true });
      await expect(close).toBeVisible();
      await close.click({ trial: true });
      await expect(drawer.getByRole('link', { name: 'Boards', exact: true })).toHaveAttribute('aria-current', 'page');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.keyboard.press('Escape');
      await expect(drawer).toBeHidden(); await expect(trigger).toBeFocused();
      await trigger.press('Enter');
      await drawer.getByRole('link', { name: 'Notifications', exact: true }).click();
      await expect(page).toHaveURL(/\/app\/demo\/notifications$/); await expect(drawer).toBeHidden();
      await page.goBack(); await expect(page).toHaveURL(/\/boards\/demo-board$/);
      await expect(page.getByRole('heading', { name: 'Shell test Board', exact: true })).toBeVisible();
      // Resizing must release the modal focus trap and restore desktop navigation.
      await trigger.click(); await expect(drawer).toBeVisible();
      await page.setViewportSize({ width: 1280, height: 844 }); await expect(drawer).toHaveCount(0);
      await expect(page.getByRole('navigation', { name: 'Internal application navigation' })).toBeVisible();
      await page.setViewportSize({ width, height: 844 });
      await expect(trigger).toHaveAttribute('aria-expanded', 'false');
      await expect(drawer).toHaveCount(0);
    }
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    if (width < 600) {
      await trigger.click();
      await expect(page.getByRole('navigation', { name: 'Mobile application navigation' })).toBeVisible();
    }
    admitted = false; await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.getByRole('button', { name: 'Open navigation', exact: true })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Internal application navigation' })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Mobile application navigation' })).toHaveCount(0);
  });
}
