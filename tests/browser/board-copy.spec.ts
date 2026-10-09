import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280,390]) {
  test(`PRD-04: private Board copy, lost reply, concurrent source and keyboard navigation at ${width}px`, async ({ page,context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width,height:844 });
    const headers = { 'X-StrataAI-Request':'1' };
    const account = { email:`board-copy-${width}-${Date.now()}@example.test`,password:'copy-correct-horse-battery',displayName:'Board copier' };
    const registered = await registerVerifiedAccountFixture(context.request, account);
    const actor = registered.user.id;
    const organization = await context.request.post('/organizations',{headers,data:{name:'Copy browser fixture'}});
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards',{headers,data:{organizationId:org,name:'Copy source',description:'Original Board description',visibility:'PRIVATE'}});
    expect(created.status()).toBe(201); const source = await created.json();
    const listResponse = await context.request.post(`/boards/${source.id}/lists`,{headers,data:{name:'Source structure'}});
    expect(listResponse.status()).toBe(201); const list = await listResponse.json();
    const cardResponse = await context.request.post(`/lists/${list.id}/cards`,{headers,data:{title:'Copied Card',description:'Copied private content'}});
    expect(cardResponse.status()).toBe(201); const card = await cardResponse.json();
    const checklistResponse = await context.request.post(`/cards/${card.id}/checklists`,{headers,data:{title:'Fresh work',cardVersion:1}});
    expect(checklistResponse.status()).toBe(200); const checklist = (await checklistResponse.json()).checklist;
    const itemResponse = await context.request.post(`/cards/${card.id}/checklists/${checklist.id}/items`,{headers,data:{text:'New work',cardVersion:2,checklistVersion:1}});
    expect(itemResponse.status()).toBe(200); const item = (await itemResponse.json()).item;
    expect((await context.request.patch(`/cards/${card.id}/checklists/${checklist.id}/items/${item.id}`,{
      headers,data:{text:'New work',completed:true,cardVersion:3,checklistVersion:2,version:1}})).status()).toBe(200);
    expect((await context.request.put(`/cards/${card.id}/members/${actor}?version=4`,{headers})).status()).toBe(200);
    expect((await context.request.put(`/boards/${source.id}/star?version=0`,{headers})).status()).toBe(204);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request,source.id);
      const other = await context.newPage(); await other.setViewportSize({width,height:844});
      const path = `/app/${org}/boards/${source.id}`; await page.goto(path); await other.goto(path);
      await expect(other.getByText('Live updates connected.',{exact:true})).toBeVisible();
      const attempts: { key: string | undefined; body: string | null; id: string }[] = [];
      let copyDispatches = 0;
      await page.route(`**/boards/${source.id}/copy`,async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        copyDispatches++;
        const response = await route.fetch(); expect(response.status()).toBe(201); const acknowledgment = await response.json();
        attempts.push({ key:route.request().headers()['idempotency-key'],body:route.request().postData(),id:acknowledgment.id });
        if (attempts.length===1) {
          expect((await context.request.patch(`/boards/${source.id}`,{headers,data:{name:'Later source',description:'Later description',version:1}})).status()).toBe(200);
          await route.abort('failed');
        } else await route.fulfill({response});
      });
      const entry = page.getByRole('button',{name:'Copy Board',exact:true}); await expect(entry).toBeEnabled(); await entry.focus(); await page.keyboard.press('Enter');
      const name = 'W'.repeat(160); const input = page.getByRole('textbox',{name:'Copied Board name',exact:true});
      await expect(page.getByRole('button',{name:'Create Board copy',exact:true})).toBeEnabled();
      await input.focus(); await page.keyboard.press('ControlOrMeta+A'); await page.keyboard.insertText(name);
      expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
      await page.getByRole('button',{name:'Create Board copy',exact:true}).focus(); await page.keyboard.press('Enter');
      const retry = page.getByRole('button',{name:'Retry same Board copy',exact:true}); await expect(retry).toBeEnabled();
      await expect(input).toHaveValue(name); await expect(input).toBeDisabled();
      await expect(page.getByRole('button',{name:'Cancel Board copy',exact:true})).toHaveCount(0);
      await expect(page.getByRole('button',{name:'Edit Board details',exact:true,includeHidden:true})).toBeDisabled();
      await expect(other.getByRole('heading',{name:'Later source',exact:true})).toBeVisible();
      await expect(async () => {
        if (copyDispatches === 1) await pressAdmittedAction(retry);
        await expect.poll(() => copyDispatches, { timeout: 500 }).toBe(2);
      }).toPass({ timeout: 5_000 });
      const open = page.getByRole('link',{name:'Open copied Board',exact:true}); await expect(open).toBeVisible(); await expect(open).toBeFocused();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(copyDispatches).toBe(2);
      expect(JSON.parse(attempts[0].body!)).toEqual({name,version:1}); expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      const copiedId = attempts[0].id; await waitForBoardDelivery(context.request,copiedId);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await open.focus(); await page.keyboard.press('Enter'); await expect(page).toHaveURL(new RegExp(`/boards/${copiedId}$`));
      await expect(page.getByRole('heading',{name,exact:true})).toBeVisible();
      await expect(page.getByText('Copied Card',{exact:true})).toBeVisible();
      const copiedResponse = await context.request.get(`/boards/${copiedId}`); expect(copiedResponse.status()).toBe(200);
      const copied = await copiedResponse.json(); expect(copied.board.visibility).toBe('PRIVATE'); expect(copied.board.description).toBe('Original Board description');
      const copiedCard = copied.lists[0].cards[0]; expect(copiedCard.id).not.toBe(card.id); expect(copiedCard.description).toBe(card.description); expect(copiedCard.version).toBe(1);
      const checklists = await context.request.get(`/cards/${copiedCard.id}/checklists`); expect(checklists.status()).toBe(200);
      const copiedChecklist = (await checklists.json()).items[0]; expect(copiedChecklist.total).toBe(1); expect(copiedChecklist.completed).toBe(0);
      const members = await context.request.get(`/cards/${copiedCard.id}/members`); expect(members.status()).toBe(200); expect((await members.json()).items).toEqual([]);
      const preference = await context.request.get(`/boards/${copiedId}/star`); expect(preference.status()).toBe(200); expect((await preference.json()).version).toBe(0);
      const history = await context.request.get(`/boards/${copiedId}/activity`); expect(history.status()).toBe(200);
      expect((await history.json()).items.map((event: {eventType:string})=>event.eventType).sort()).toEqual(['BOARD_COPIED','BOARD_CREATED']);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
      await other.close();
    } finally { restoreWorker(); }
  });
}
