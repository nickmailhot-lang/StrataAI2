import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280,390]) {
  test(`PRD-07: whole-List move recovers a lost reply and destination peer after reconnect at ${width}px`, async ({ page,context,browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width,height:844 });
    const headers = { 'X-StrataAI-Request':'1' };
    const account = { email:`list-move-${width}-${Date.now()}@example.test`,password:'list-move-correct-horse-battery',displayName:'List mover' };
    await registerNotificationAccount(context.request,account);
    expect((await context.request.post('/auth/login',{ headers,data:account })).status()).toBe(200);
    const organization = await context.request.post('/organizations',{ headers,data:{ name:'Move Organization' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    async function create(path: string,data: object) {
      const response = await context.request.post(path,{ headers,data }); expect(response.status()).toBe(201); return response.json();
    }
    const source = await create('/boards',{ organizationId:org,name:'Source',visibility:'PRIVATE' });
    const destination = await create('/boards',{ organizationId:org,name:'Delivery',visibility:'PRIVATE' });
    const list = await create(`/boards/${source.id}/lists`,{ name:'Planning' });
    const active = await create(`/lists/${list.id}/cards`,{ title:'Moved active work',description:'Stable contents' });
    const archived = await create(`/lists/${list.id}/cards`,{ title:'Moved archived work',description:'Stable archived contents' });
    expect((await context.request.post(`/cards/${archived.id}/archive`,{ headers,data:{ version:1 } })).status()).toBe(200);
    const peerContext = await browser.newContext({ storageState:await context.storageState() });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request,source.id);
      const peer = await peerContext.newPage(); await peer.setViewportSize({ width,height:844 });
      let unavailable = false; let socket: WebSocketRoute | undefined;
      await peer.routeWebSocket('**/boards/live*',route => {
        if (unavailable) { route.close({ code:1013 }); return; }
        socket = route; route.connectToServer();
      });
      await peer.goto(`/app/${org}/boards/${destination.id}`);
      await expect(peer.getByText('Live updates connected.',{ exact:true })).toBeVisible();
      await page.goto(`/app/${org}/boards/${source.id}`);
      await expect(page.getByText('Live updates connected.',{ exact:true })).toBeVisible();
      const attempts: { key:string|undefined;body:string|null }[] = [];
      await page.route(`**/lists/${list.id}/move`,async route => {
        attempts.push({ key:route.request().headers()['idempotency-key'],body:route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (attempts.length===1) await route.abort('failed'); else await route.fulfill({ response });
      });
      const entry = page.getByRole('button',{ name:'Move list',exact:true }); await expect(entry).toBeEnabled(); await entry.press('Enter');
      await expect(page.getByRole('button',{ name:'Reload move destinations' })).toBeEnabled();
      async function choose(label:string,name:string) {
        await page.getByRole('combobox',{ name:label }).focus(); await page.keyboard.press('Enter');
        await page.getByRole('option',{ name,exact:true }).focus(); await page.keyboard.press('Enter');
      }
      await choose('List to move','Planning'); await choose('Destination Board','Delivery');
      await page.getByRole('button',{ name:'Review List move',exact:true }).press('Enter');
      await expect(page.getByText('Move Planning to the end of Delivery?',{ exact:true })).toBeVisible();
      expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
      await page.getByRole('button',{ name:'Confirm List move',exact:true }).press('Enter');
      const retry = page.getByRole('button',{ name:'Retry this List move',exact:true }); await expect(retry).toBeEnabled();
      await expect(page.getByRole('button',{ name:'Cancel move' })).toHaveCount(0);
      await expect(page.getByRole('heading',{ name:'Planning',exact:true })).toHaveCount(0);
      await expect(page.getByRole('button',{ name:'Add list',exact:true })).toBeDisabled();
      await expect(peer.getByRole('heading',{ name:'Planning',exact:true })).toBeVisible();
      await expect(peer.getByRole('link',{ name:'Moved active work',exact:true })).toBeVisible();
      await expect(peer.getByRole('link',{ name:'Moved archived work',exact:true })).toHaveCount(0);
      await retry.press('Enter'); await expect(page.getByRole('button',{ name:'Refresh board',exact:true })).toBeFocused();
      await expect(page.getByText('List move acknowledged. Check the destination Board for its current contents.',{ exact:true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ sourceBoardId:source.id,destinationBoardId:destination.id,expectedVersion:1 });
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      const result = await context.request.get(`/boards/${destination.id}`); expect(result.status()).toBe(200);
      const column = (await result.json()).lists[0];
      expect(column.list).toMatchObject({ id:list.id,name:list.name,version:2,lifecycleState:'active' });
      expect(column.cards).toHaveLength(1); expect(column.cards[0]).toMatchObject({ id:active.id,title:active.title,description:active.description,rank:active.rank,version:2 });
      const archive = await context.request.get(`/boards/${destination.id}/archived-cards`); expect(archive.status()).toBe(200);
      expect((await archive.json()).items[0].card).toMatchObject({ id:archived.id,title:archived.title,description:archived.description,rank:archived.rank,version:3,lifecycleState:'archived' });
      const old = await context.request.get(`/boards/${source.id}`); expect(old.status()).toBe(200); expect((await old.json()).lists).toHaveLength(0);
      // Use an independent browser context so only this peer misses delivery.
      // Explicitly close its forwarded real socket and refuse replacement
      // sockets during the outage; no fabricated SignalR frames are supplied.
      expect(socket).toBeDefined(); unavailable = true; await socket!.close({ code:1012 });
      await peerContext.setOffline(true);
      const returned = await context.request.post(`/lists/${list.id}/move`,{ headers:{ ...headers,'Idempotency-Key':crypto.randomUUID() },
        data:{ sourceBoardId:destination.id,destinationBoardId:source.id,expectedVersion:2 } });
      expect(returned.status()).toBe(200); expect(await returned.json()).toMatchObject({ id:list.id,boardId:source.id,version:3 });
      await waitForBoardDelivery(context.request,destination.id);
      await expect(page.getByRole('heading',{ name:'Planning',exact:true })).toBeVisible();
      unavailable = false; await peerContext.setOffline(false);
      await expect(peer.getByText('Live updates connected.',{ exact:true })).toBeVisible({ timeout:45_000 });
      await expect(peer.getByRole('heading',{ name:'Planning',exact:true })).toHaveCount(0);
      await expect(peer.getByRole('link',{ name:'Moved active work',exact:true })).toHaveCount(0);
      const movedAgain = await context.request.post(`/lists/${list.id}/move`,{ headers:{ ...headers,'Idempotency-Key':crypto.randomUUID() },
        data:{ sourceBoardId:source.id,destinationBoardId:destination.id,expectedVersion:3 } });
      expect(movedAgain.status()).toBe(200); expect(await movedAgain.json()).toMatchObject({ id:list.id,boardId:destination.id,version:4 });
      await waitForBoardDelivery(context.request,destination.id);
      await expect(peer.getByRole('heading',{ name:'Planning',exact:true })).toBeVisible();
      await expect(peer.getByRole('link',{ name:'Moved active work',exact:true })).toBeVisible();
      await expect(page.getByRole('heading',{ name:'Planning',exact:true })).toHaveCount(0);
      const current = await context.request.get(`/boards/${destination.id}`); expect(current.status()).toBe(200);
      const latest = (await current.json()).lists[0]; expect(latest.list).toMatchObject({ id:list.id,version:4 });
      expect(latest.cards[0]).toMatchObject({ id:active.id,version:4 });
      await peer.reload(); await expect(peer.getByRole('heading',{ name:'Planning',exact:true })).toBeVisible();
      await expect(peer.getByRole('link',{ name:'Moved active work',exact:true })).toBeVisible();
    } finally { try { await peerContext.close(); } finally { restoreWorker(); } }
  });
}
