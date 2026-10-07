import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  for (const surface of ['INTERNAL', 'PORTAL', 'BOARD']) {
    test(`PRD-03/05/60: ${surface} invitation expiry retires consent at ${viewport.width}px`, async ({ page, context }) => {
      test.setTimeout(90_000);
      await page.setViewportSize(viewport);
      const headers = { 'X-StrataAI-Request': '1' };
      const credentials = { email: `history-expiry-${surface}-${viewport.width}-${Date.now()}@example.test`,
        password: 'browser-history-expiry-correct-horse', displayName: 'Invitation expiry Owner' };
      expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      const created = await context.request.post('/organizations', { headers, data: { name: 'Invitation expiry review' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      let board: string | undefined;
      if (surface === 'BOARD') {
        const result = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Expiry Board' } });
        expect(result.status()).toBe(201); board = (await result.json()).id;
      }
      const root = board ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
      const email = `expiry-recipient-${surface}-${viewport.width}-${Date.now()}@example.test`;
      const issued = await context.request.post(root, { headers, data: board ? { email, role: 'MEMBER' }
        : { email, surface, targetRole: surface === 'PORTAL' ? 'OWNER' : 'MEMBER' } });
      expect(issued.status()).toBe(201); const id = (await issued.json()).id;
      const canonical = await context.request.get(root); expect(canonical.status()).toBe(200);
      const before = (await canonical.json()).items.find((item: { id: string }) => item.id === id);
      // Advance only browser time around the real persisted expiry. This proves
      // conservative UI withdrawal; it does not claim the API clock has expired.
      await page.clock.install({ time: new Date(Date.parse(before.expiresAt) - 2000) });
      await page.clock.pauseAt(new Date(Date.parse(before.expiresAt) - 1000));
      let reads = 0, writes = 0, documents = 0;
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (new URL(request.url()).pathname.startsWith(root)) {
          if (request.method() === 'GET') reads++; else writes++;
        }
      });
      await page.goto(board ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`);
      const action = page.getByRole('button', { name: `Revoke invitation for ${email}` });
      await expect(action).toBeVisible(); await action.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toBeVisible(); const initialReads = reads;
      await page.clock.runFor(1500);
      await expect(page.getByText('Expired', { exact: true })).toBeVisible();
      // The protected read can finish after runFor returns. Allow the newly
      // scheduled MUI dialog exit timer to complete on the paused clock.
      await page.clock.runFor(500);
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(action).toHaveCount(0);
      expect(reads).toBeGreaterThan(initialReads); expect(writes).toBe(0); expect(documents).toBe(1);
      const after = await context.request.get(root); expect(after.status()).toBe(200);
      expect((await after.json()).items.find((item: { id: string }) => item.id === id)).toEqual(before);
      // Axe schedules browser timers. Resume only after every expiry, privacy
      // and original-command assertion so the accessibility scan can complete.
      await page.clock.resume();
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    });
  }
}
