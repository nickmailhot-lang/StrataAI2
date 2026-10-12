import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

const headers = { 'X-StrataAI-Request': '1' };
async function press(page: Page, name: string) {
  const button = page.getByRole('button', { name, exact: true });
  await expect(button).toBeEnabled(); await button.focus(); await page.keyboard.press('Enter');
}
async function sourceEntry(page: Page, kind: string) {
  const classification = page.getByRole('region', { name: 'Source retention classification', exact: true });
  const entry = page.getByRole('button', { name: `Retention classification for this ${kind === 'card' ? 'Card' : kind === 'cardComment' ? 'Comment' : 'Attachment'}`, exact: true });
  const parent = kind === 'cardComment' ? page.getByRole('button', { name: 'Review Card comments', exact: true })
    : kind === 'attachment' ? page.getByRole('button', { name: 'Show attachments', exact: true }) : undefined;
  // Only unopened source disclosures may reactivate. Observe admitted source
  // classification before review; consent and the original command stay below.
  await expect(async () => {
    if (await classification.isVisible()) return;
    const action = await entry.isVisible() ? entry : parent && await parent.isVisible() ? parent : undefined;
    if (action) {
      await expect(action).toBeEnabled({ timeout: 500 });
      await action.focus({ timeout: 500 });
      await expect(action).toBeFocused({ timeout: 500 });
      await action.press('Enter', { timeout: 500 });
    }
    await expect(classification).toBeVisible({ timeout: 500 });
  }).toPass({ timeout: 5_000 });
}
async function chooseHold(page: Page, name: string, option: string) {
  const select = page.getByRole('combobox', { name, exact: true }); await expect(select).toBeEnabled();
  await select.focus(); await page.keyboard.press('ArrowDown');
  await page.getByRole('option', { name: option, exact: true }).focus(); await page.keyboard.press('Enter');
}

for (const width of [1280,390]) {
  test(`PRD-70 hold administration streams another actor's release and withdraws history at ${width}px`,async ({page,context,browser})=>{
    test.setTimeout(120_000);await page.setViewportSize({width,height:844});
    const owner=await browser.newContext({baseURL:test.info().project.use.baseURL});
    const frames:{userId:string;holdId:string;events:{eventId:string;eventType:string;actorId:string;entityVersion:number}[]}[]=[];
    page.on('websocket',socket=>{
      if (!new URL(socket.url()).pathname.endsWith('/organizations/live/retention-holds')) return;
      socket.on('framereceived',frame=>{if(typeof frame.payload==='string')for(const part of frame.payload.split('\u001e').filter(Boolean)){
        try{const message=JSON.parse(part);if(message.type===2 && message.item?.events)frames.push(message.item);}catch{/* Partial frame. */}
      }});
    });
    try{
      const verified=process.env.STRATAAI_E2E_RUNTIME_MODE!=='demo';
      const author=await registerNotificationAccount(owner.request,{email:`hold-live-owner-${width}-${crypto.randomUUID()}@example.test`,password:'hold-live-owner-fixture',displayName:'Live hold Owner'},verified);
      const email=`hold-live-admin-${width}-${crypto.randomUUID()}@example.test`;
      const admin=await registerNotificationAccount(context.request,{email,password:'hold-live-admin-fixture',displayName:'Live hold administrator'},verified);
      const created=await owner.request.post('/organizations',{headers,data:{name:'Live hold fixture'}});expect(created.status()).toBe(201);const org=(await created.json()).organization.id;
      const invitation=await owner.request.post(`/organizations/${org}/invitations`,{headers,data:{email,surface:'INTERNAL',targetRole:'ADMIN'}});expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`,{headers})).status()).toBe(200);
      const id=crypto.randomUUID(),path=`/organizations/${org}/retention-holds/${id}`;
      const applied=await owner.request.post(path+'/apply',{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:{recordClass:null,category:null,recordId:null,
        reason:'Live private preservation reason',review:null,confirmed:true}});expect(applied.status()).toBe(200);
      await page.goto(`/app/${org}/retention-holds`);
      const directory=page.getByRole('region',{name:'Available retention holds'});await expect(directory).toContainText('Live private preservation reason');
      await press(page,`Review active hold ${id}`);await expect(page.getByText('Status: active. Version: 1.',{exact:true})).toBeVisible();
      await expect.poll(()=>frames.some(frame=>frame.userId===admin.user.id && frame.holdId===id && frame.events.some(event=>event.eventType==='RETENTION_HOLD_APPLIED'))).toBe(true);
      await press(page,'View hold history');await expect(page.getByRole('region',{name:'Retained hold history'})).toContainText('Applied by Live hold Owner as Organization Owner');
      await page.getByRole('checkbox',{name:'I reviewed this version and confirm release of this hold.',exact:true}).focus();await page.keyboard.press('Space');
      const release=await owner.request.post(path+'/release',{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data:{version:1,confirmed:true}});expect(release.status()).toBe(200);
      await expect(page.getByText('Status: released. Version: 2.',{exact:true})).toBeVisible({timeout:10_000});
      await expect(page.getByRole('button',{name:'Release confirmed hold',exact:true})).toHaveCount(0);
      await expect(page.getByRole('region',{name:'Retained hold history'})).toHaveCount(0);
      await expect.poll(()=>frames.some(frame=>frame.events.some(event=>event.eventType==='RETENTION_HOLD_RELEASED' && event.entityVersion===2 && event.actorId===author.user.id))).toBe(true);
      await press(page,'View hold history');await expect(page.getByRole('region',{name:'Retained hold history'})).toContainText('Released by Live hold Owner as Organization Owner');
      const timeline=await owner.request.get(path+'/history');expect(timeline.status()).toBe(200);const revisions=(await timeline.json()).revisions;
      expect(frames.flatMap(frame=>frame.events).find(event=>event.eventType==='RETENTION_HOLD_RELEASED')?.eventId).toBe(revisions[1].eventId);
      expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
      const memberResponse=await owner.request.get(`/organizations/${org}/members/${admin.user.id}`);expect(memberResponse.status()).toBe(200);const member=(await memberResponse.json()).member;
      expect((await owner.request.delete(`/organizations/${org}/members/${admin.user.id}?expectedVersion=${member.version}`,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()}})).status()).toBe(204);
      await expect(page.getByRole('region',{name:'Retained hold history'})).toHaveCount(0,{timeout:20_000});
      expect(await page.locator('body').innerText()).not.toContain('Live private preservation reason');
      expect((await context.request.get(path+'/history')).status()).toBe(404);
    }finally{await owner.close();}
  });
}

for (const width of [1280, 390]) {
  test(`PRD-70 hold administration recovers reviewed preservation and releases blocked disposition at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const account = await registerNotificationAccount(context.request, {
      email: `hold-review-${width}-${crypto.randomUUID()}@example.test`, password: 'hold-review-fixture-horse', displayName: 'Hold review Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Reviewed hold fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Retained held Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Held List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Private held Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const source = `/organizations/${org}/retention-sources/card/${card}`;
    const reviewed = await context.request.get(source + '/review'); expect(reviewed.status()).toBe(200);
    expect((await context.request.post(source + '/classification', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { review: await reviewed.json(), category: 'browser.hold', jurisdiction: null, confirmed: true } })).status()).toBe(200);
    expect((await context.request.post(`/cards/${card}/archive`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 1 } })).status()).toBe(200);
    const writes: { path:string; body:string; key:string; receipt: { id:string; version:number; status:string } }[] = [];
    const matches = (url:URL) => url.pathname.startsWith(`/organizations/${org}/retention-holds/`) && url.pathname.endsWith('/apply');
    await page.route(matches, async route => {
      const request = route.request(), response = await route.fetch(); expect(response.status()).toBe(200);
      writes.push({ path:new URL(request.url()).pathname, body:request.postData()!, key:request.headers()['idempotency-key'], receipt:await response.json() });
      if (writes.length === 1) await route.fulfill({ status:503,contentType:'application/json',body:'{"code":"organization_storage_unavailable"}' });
      else await route.fulfill({ response });
    });
    await page.goto(`/app/${org}/retention-holds`);
    await chooseHold(page,'Hold scope','Individual classified record'); await chooseHold(page,'Source type','Card');
    await page.getByRole('textbox',{name:'Classified record identifier',exact:true}).fill(card);
    const confirm = page.getByRole('checkbox',{name:'I reviewed this scope and confirm preservation under this hold.',exact:true});
    await expect(confirm).toBeDisabled(); await press(page,'Review classified source');
    await expect(page.getByText(`Reviewed card: ${card}. Board: ${board}. Source version 2; Card version 2.`,{exact:true})).toBeVisible();
    await page.getByRole('textbox',{name:'Preservation reason',exact:true}).fill('Private preservation evidence');
    await expect(page.getByRole('button',{name:'Apply confirmed hold',exact:true})).toBeDisabled();
    await confirm.focus(); await page.keyboard.press('Space');
    expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await press(page,'Apply confirmed hold');
    await expect(page.getByText('The acknowledgment could not be confirmed. Retry the retained original request.',{exact:true})).toBeVisible();
    expect(writes).toHaveLength(1); await page.reload(); await press(page,'Retry original request');
    await expect(page.getByText('Original request acknowledged. Load the current hold to review its present status.',{exact:true})).toBeVisible();
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    expect(JSON.parse(writes[0].body)).toMatchObject({recordClass:'operationalWork',recordId:card,confirmed:true,
      review:{organizationId:org,kind:'card',recordId:card,boardId:board,sourceVersion:2}});
    const deletionKey=crypto.randomUUID(), deletionPath=`/cards/${card}?version=2&confirmed=true`;
    const blocked=await context.request.delete(deletionPath,{headers:{...headers,'Idempotency-Key':deletionKey}});
    expect(blocked.status()).toBe(409); expect((await blocked.json()).code).toBe('retention_hold_prevents_disposition');
    expect(await blocked.text()).not.toContain('Private preservation evidence');
    await press(page,'Load current hold'); await expect(page.getByText('Status: active. Version: 1.',{exact:true})).toBeVisible();
    const release=page.getByRole('button',{name:'Release confirmed hold',exact:true}); await expect(release).toBeDisabled();
    await page.getByRole('checkbox',{name:'I reviewed this version and confirm release of this hold.',exact:true}).focus(); await page.keyboard.press('Space');
    expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    await press(page,'Release confirmed hold'); await expect(page.getByText('Original request acknowledged. Load the current hold to review its present status.',{exact:true})).toBeVisible();
    await press(page,'Load current hold'); await expect(page.getByText('Status: released. Version: 2.',{exact:true})).toBeVisible();
    await expect(release).toHaveCount(0);
    const original=await context.request.post(writes[0].path,{headers:{...headers,'Idempotency-Key':writes[0].key},data:JSON.parse(writes[0].body)});
    expect(original.status()).toBe(200); expect(await original.json()).toEqual(writes[0].receipt);
    const current=await context.request.get(`/organizations/${org}/retention-holds/${writes[0].receipt.id}`);
    expect(current.status()).toBe(200); expect(await current.json()).toMatchObject({status:'released',version:2,appliedBy:account.user.id});
    const deleted=await context.request.delete(deletionPath,{headers:{...headers,'Idempotency-Key':deletionKey}});
    expect(deleted.status()).toBe(200); const deletionReceipt=await deleted.json();
    expect(deletionReceipt).toMatchObject({id:card,version:3,lifecycleState:'deleted',deletedBy:account.user.id});
    const replay=await context.request.delete(deletionPath,{headers:{...headers,'Idempotency-Key':deletionKey}});
    expect(replay.status()).toBe(200); expect(await replay.json()).toEqual(deletionReceipt);
    await page.unroute(matches);
  });
  test(`PRD-70 hold administration withdraws pending private preservation after real membership removal at ${width}px`, async ({page,context,browser}) => {
    test.setTimeout(120_000); await page.setViewportSize({width,height:844});
    const owner=await browser.newContext({baseURL:test.info().project.use.baseURL});
    try {
      const verified=process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
      await registerNotificationAccount(owner.request,{email:`hold-owner-${width}-${crypto.randomUUID()}@example.test`,password:'hold-owner-fixture-horse',displayName:'Hold Owner'},verified);
      const email=`hold-admin-${width}-${crypto.randomUUID()}@example.test`;
      const admin=await registerNotificationAccount(context.request,{email,password:'hold-admin-fixture-horse',displayName:'Hold Admin'},verified);
      const created=await owner.request.post('/organizations',{headers,data:{name:'Hold withdrawal fixture'}});
      expect(created.status()).toBe(201); const org=(await created.json()).organization.id;
      const invitation=await owner.request.post(`/organizations/${org}/invitations`,{headers,data:{email,surface:'INTERNAL',targetRole:'ADMIN'}});
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`,{headers})).status()).toBe(200);
      const writes: {path:string;body:string;key:string}[]=[];
      const matches=(url:URL)=>url.pathname.startsWith(`/organizations/${org}/retention-holds/`) && url.pathname.endsWith('/apply');
      await page.route(matches,async route=>{
        const request=route.request(),response=await route.fetch(); expect(response.status()).toBe(200);
        writes.push({path:new URL(request.url()).pathname,body:request.postData()!,key:request.headers()['idempotency-key']});
        await route.fulfill({status:503,contentType:'application/json',body:'{"code":"organization_storage_unavailable"}'});
      });
      await page.goto(`/app/${org}/retention-holds`);
      const reason=page.getByRole('textbox',{name:'Preservation reason',exact:true}); await expect(reason).toBeEnabled(); await reason.fill('Private revoked hold reason');
      await page.getByRole('checkbox',{name:'I reviewed this scope and confirm preservation under this hold.',exact:true}).focus(); await page.keyboard.press('Space');
      await press(page,'Apply confirmed hold');
      await expect(page.getByText('The acknowledgment could not be confirmed. Retry the retained original request.',{exact:true})).toBeVisible();
      const storageKey=`strataai:hold-command:v1:${admin.user.id}:${org}`;
      expect(await page.evaluate(key=>sessionStorage.getItem(key)!==null,storageKey)).toBe(true); expect(writes).toHaveLength(1);
      const membership=await owner.request.get(`/organizations/${org}/members/${admin.user.id}`); expect(membership.status()).toBe(200);
      const member=(await membership.json()).member;
      expect((await owner.request.delete(`/organizations/${org}/members/${admin.user.id}?expectedVersion=${member.version}`,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()}})).status()).toBe(204);
      await expect(page.getByRole('button',{name:'Retry original request',exact:true})).toHaveCount(0,{timeout:20_000});
      await expect(reason).toHaveCount(0); expect(await page.locator('body').innerText()).not.toContain('Private revoked hold reason');
      await expect.poll(()=>page.evaluate(key=>sessionStorage.getItem(key),storageKey)).toBeNull(); expect(writes).toHaveLength(1);
      const denied=await context.request.post(writes[0].path,{headers:{...headers,'Idempotency-Key':writes[0].key},data:JSON.parse(writes[0].body)});
      expect(denied.status()).toBe(404); expect(await denied.text()).not.toContain('Private revoked hold reason');
      const holdId=writes[0].path.split('/').at(-2);
      const retained=await owner.request.get(`/organizations/${org}/retention-holds/${holdId}`); expect(retained.status()).toBe(200);
      expect(await retained.json()).toMatchObject({status:'active',version:1,appliedBy:admin.user.id});
      await page.unroute(matches);
    } finally {await owner.close();}
  });
}

for (const width of [1280, 390]) {
  test(`PRD-70 archived Card and Attachment classification remains independent of operational editing at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const account = await registerNotificationAccount(context.request, {
      email: `source-archive-${width}-${crypto.randomUUID()}@example.test`, password: 'source-archive-fixture-horse', displayName: 'Archive classification Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Archived source fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Retained source Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Retained source List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Archived classified Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const added = await context.request.post(`/cards/${card}/attachments/url`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { title: 'Archived reference', url: 'https://example.test/archived-source', cardVersion: 1 } });
    expect(added.status()).toBe(200); const attachment = (await added.json()).attachment;
    expect((await context.request.post(`/cards/${card}/attachments/${attachment.id}/archive`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { cardVersion: 2, version: 1 },
    })).status()).toBe(200);
    expect((await context.request.post(`/cards/${card}/archive`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { version: 3 } })).status()).toBe(200);
    const receipts: { actorId: string; reference: { recordId: string; sourceVersion: number } }[] = [];
    page.on('response', async response => {
      if (response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/classification') && response.status() === 200)
        receipts.push(await response.json());
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`, reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await expect(page.getByText('This Card or its List is archived. Details are read-only.', { exact: true })).toBeVisible();
      for (const source of [{ kind: 'card', id: card, version: 4 }, { kind: 'attachment', id: attachment.id, version: 2 }]) {
        if (source.kind === 'attachment') await press(page, 'Review archived attachments');
        await press(page, `Retention classification for this ${source.kind === 'card' ? 'Card' : 'Attachment'}`);
        await press(page, 'Review source classification');
        await expect(page.getByRole('textbox', { name: 'Record category', exact: false })).toBeFocused();
        await expect(page.getByText(`Source revision ${source.version}; Card revision 4; List revision 1.`, { exact: true })).toBeVisible();
        await expect(page.getByText('Source archived; Card archived; List active.', { exact: true })).toBeVisible();
        await page.keyboard.insertText(`archived.${source.kind}`);
        await page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true }).focus();
        await page.keyboard.press('Space');
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        for (const name of ['Save card', 'Manage attachments', 'Add link attachment', 'Restore attachment Archived reference', 'Delete attachment Archived reference'])
          await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0);
        await press(page, 'Confirm classification');
        await expect(page.getByRole('region', { name: 'Source retention classification', exact: true })
          .getByText('Classification confirmed. The original source and audit evidence are preserved.', { exact: true })).toBeVisible();
        await expect.poll(() => receipts.some(receipt => receipt.reference.recordId === source.id && receipt.reference.sourceVersion === source.version
          && receipt.actorId === account.user.id)).toBe(true);
        await press(page, 'Hide retention classification');
      }
      expect(receipts).toHaveLength(2);
      const retained = await context.request.get(`/boards/${board}/cards/${card}/archived-details`); expect(retained.status()).toBe(200);
      expect((await retained.json()).version).toBe(4);
      const archive = await context.request.get(`/cards/${card}/attachments/archive`); expect(archive.status()).toBe(200);
      expect(await archive.json()).toMatchObject({ cardVersion: 4, canRestore: false, canDelete: false,
        items: [{ id: attachment.id, version: 2, lifecycleState: 1 }] });
    } finally { restoreWorker(); }
  });
  test(`PRD-70 membership withdrawal clears pending source recovery without replay at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {};
    try {
      const verified = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
      await registerNotificationAccount(owner.request, {
        email: `source-withdraw-owner-${width}-${crypto.randomUUID()}@example.test`,
        password: 'source-withdraw-owner-fixture', displayName: 'Source withdrawal Owner',
      }, verified);
      const email = `source-withdraw-admin-${width}-${crypto.randomUUID()}@example.test`;
      const admin = await registerNotificationAccount(context.request, { email,
        password: 'source-withdraw-admin-fixture', displayName: 'Source withdrawal administrator' }, verified);
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Source withdrawal fixture' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const invitation = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private withdrawal Board', visibility: 'PRIVATE' } });
      expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
      const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Private source List' } });
      expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
      const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Private source before withdrawal' } });
      expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
      const path = `/organizations/${org}/retention-sources/card/${card}/classification`;
      const writes: { body: string; key: string }[] = [];
      await page.route(url => url.pathname === path, async route => {
        const request = route.request(), response = await route.fetch(); expect(response.status()).toBe(200);
        writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'] });
        await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
      });
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`, reads = trackBoardReads(page, board, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card'); await press(page, 'Review source classification');
      await expect(page.getByRole('textbox', { name: 'Record category', exact: false })).toBeFocused();
      await page.keyboard.insertText('private.withdrawal');
      await page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true }).focus();
      await page.keyboard.press('Space'); await press(page, 'Confirm classification');
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toBeFocused();
      expect(writes).toHaveLength(1);
      const storageKey = `strataai:retention-source-classification:v1:${admin.user.id}:${org}`;
      expect(await page.evaluate(key => sessionStorage.getItem(key) !== null, storageKey)).toBe(true);
      const memberResponse = await owner.request.get(`/organizations/${org}/members/${admin.user.id}`);
      expect(memberResponse.status()).toBe(200); const member = (await memberResponse.json()).member;
      expect((await owner.request.delete(`/organizations/${org}/members/${admin.user.id}?expectedVersion=${member.version}`,
        { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } })).status()).toBe(204);
      // Board deep links retain their own safe denial view after the outer
      // Organization surface withdraws; require that authoritative Board state.
      await expect(page.getByRole('alert').filter({ hasText: 'This board or action is unavailable.' })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toHaveCount(0);
      await expect.poll(() => page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull();
      expect(await page.locator('body').innerText()).not.toContain('Private source before withdrawal');
      expect(await page.locator('body').innerText()).not.toContain('private.withdrawal');
      expect(writes).toHaveLength(1);
      const refused = await context.request.post(path, { headers: { ...headers, 'Idempotency-Key': writes[0].key }, data: JSON.parse(writes[0].body) });
      expect(refused.status()).toBe(404); expect(await refused.text()).not.toContain('private.withdrawal');
      const preserved = await owner.request.get(`/boards/${board}`); expect(preserved.status()).toBe(200);
      expect((await preserved.json()).lists.flatMap((item: { cards: { id: string; version: number }[] }) => item.cards)
        .find((item: { id: string }) => item.id === card).version).toBe(1);
      const replacement = await owner.request.post(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: JSON.parse(writes[0].body) });
      // A fresh actor/key cannot disclose or replace an existing classification.
      expect(replacement.status()).toBe(404);
      expect((await replacement.json()).code).toBe('retention_source_unavailable');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    } finally { restoreWorker(); await owner.close(); }
  });
  test(`PRD-70 original classification recovery retains its source Board after a real Card move at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, {
      email: `source-move-${width}-${crypto.randomUUID()}@example.test`,
      password: 'source-move-fixture-correct-horse', displayName: 'Source move Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Classification move fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boards: string[] = [], lists: string[] = [];
    for (const name of ['Original classification Board', 'Current source Board']) {
      const board = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(board.status()).toBe(201); boards.push((await board.json()).id);
      const list = await context.request.post(`/boards/${boards.at(-1)}/lists`, { headers, data: { name: 'Source context' } });
      expect(list.status()).toBe(201); lists.push((await list.json()).id);
    }
    const cardResponse = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Moving classified source' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const path = `/organizations/${org}/retention-sources/card/${card}/classification`;
    const writes: { body: string; key: string; receipt: unknown }[] = [];
    await page.route(url => url.pathname === path, async route => {
      const request = route.request(), response = await route.fetch(); expect(response.status()).toBe(200);
      writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'], receipt: await response.json() });
      if (writes.length === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
      else await route.fulfill({ response });
    });
    const restoreWorker = scopedBoardWorker(org);
    try {
      for (const board of boards) await waitForBoardDelivery(context.request, board);
      const originalPath = `/app/${org}/boards/${boards[0]}/cards/${card}`, originalReads = trackBoardReads(page, boards[0], originalPath);
      await page.goto(originalPath); await expect.poll(originalReads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card'); await press(page, 'Review source classification');
      await expect(page.getByRole('textbox', { name: 'Record category', exact: false })).toBeFocused();
      await page.keyboard.insertText('reviewed.before.move');
      await page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true }).focus();
      await page.keyboard.press('Space'); await press(page, 'Confirm classification');
      await expect(page.getByRole('button', { name: 'Retry original classification', exact: true })).toBeFocused();
      const moved = await context.request.post(`/cards/${card}/move`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { sourceBoardId: boards[0], destinationListId: lists[1], expectedVersion: 1 } });
      expect(moved.status()).toBe(200);
      await waitForBoardDelivery(context.request, boards[1]);
      const currentPath = `/app/${org}/boards/${boards[1]}/cards/${card}`, currentReads = trackBoardReads(page, boards[1], currentPath);
      await page.goto(currentPath); await expect.poll(currentReads).toBeGreaterThanOrEqual(2);
      await sourceEntry(page, 'card');
      await expect(page.getByRole('button', { name: 'Review source classification', exact: true })).toHaveCount(0);
      await press(page, 'Retry original classification');
      await expect(page.getByText('Classification confirmed. The original source and audit evidence are preserved.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      expect(JSON.parse(writes[0].body).review).toMatchObject({ cardVersion: 1, sourceVersion: 1, boardId: boards[0], listId: lists[0], recordId: card });
      expect((writes[1].receipt as { reference: object }).reference).toMatchObject({ originalBoardId: boards[0], sourceVersion: 1, recordId: card });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { restoreWorker(); }
  });
  test(`PRD-70 actual three-source keyboard classification and exact original reload recovery at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const account = await registerNotificationAccount(context.request, {
      email: `source-classification-${width}-${crypto.randomUUID()}@example.test`,
      password: 'source-classification-fixture-horse', displayName: 'Classification Owner',
    }, process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Source classification fixture' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardResponse = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Reviewed source Board', visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Source List' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Actual retained source Card' } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    const commentResponse = await context.request.post(`/cards/${card}/comments`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { content: 'Actual source comment', cardVersion: 1 },
    });
    expect(commentResponse.status()).toBe(200);
    const comments = await context.request.get(`/cards/${card}/comments`); expect(comments.status()).toBe(200);
    const comment = (await comments.json()).items[0];
    const attachmentResponse = await context.request.post(`/cards/${card}/attachments/url`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { title: 'Actual source attachment', url: 'https://example.test/retained-source', cardVersion: 2 },
    });
    expect(attachmentResponse.status()).toBe(200);
    const attachments = await context.request.get(`/cards/${card}/attachments`); expect(attachments.status()).toBe(200);
    const attachment = (await attachments.json()).items[0];
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`;
      const reads = trackBoardReads(page, board, cardPath), events: string[] = [];
      for (const source of [{ kind: 'card', id: card, version: 3 },
        { kind: 'cardComment', id: comment.id, version: comment.version },
        { kind: 'attachment', id: attachment.id, version: attachment.version }]) {
        const path = `/organizations/${org}/retention-sources/${source.kind}/${source.id}/classification`;
        const matches = (url: URL) => url.pathname === path;
        const writes: { body: string; key: string; receipt: unknown }[] = [];
        await page.route(matches, async route => {
          const request = route.request(); expect(request.method()).toBe('POST');
          const response = await route.fetch(); expect(response.status()).toBe(200);
          writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'], receipt: await response.json() });
          if (writes.length === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"work_storage_unavailable"}' });
          else await route.fulfill({ response });
        });
        const before = reads(); await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(before + 2);
        await sourceEntry(page, source.kind); await press(page, 'Review source classification');
        const category = page.getByRole('textbox', { name: 'Record category', exact: false });
        await expect(category).toBeFocused(); await page.keyboard.insertText(`reviewed.${source.kind}`);
        await expect(page.getByRole('button', { name: 'Confirm classification', exact: true })).toBeDisabled();
        const confirm = page.getByRole('checkbox', { name: 'I reviewed the source evidence and confirm this classification', exact: true });
        await confirm.focus(); await page.keyboard.press('Space');
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await press(page, 'Confirm classification');
        const retry = page.getByRole('button', { name: 'Retry original classification', exact: true });
        await expect(retry).toBeFocused(); expect(writes).toHaveLength(1);
        const beforeReload = reads(); await page.reload(); await expect.poll(reads).toBeGreaterThanOrEqual(beforeReload + 2);
        await sourceEntry(page, source.kind); await press(page, 'Retry original classification');
        await expect(page.getByText('Classification confirmed. The original source and audit evidence are preserved.', { exact: true })).toBeVisible();
        expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
        expect(JSON.parse(writes[0].body)).toMatchObject({ confirmed: true, category: `reviewed.${source.kind}`, jurisdiction: null,
          review: { organizationId: org, kind: source.kind, recordId: source.id, cardId: card, boardId: board, cardVersion: 3, sourceVersion: source.version } });
        const receipt = writes[0].receipt as { actorId: string; eventId: string; reference: object };
        expect(receipt.actorId).toBe(account.user.id);
        expect(receipt.reference).toMatchObject({ organizationId: org, recordId: source.id, kind: source.kind, originalBoardId: board, sourceVersion: source.version });
        events.push(receipt.eventId);
        await page.unroute(matches);
      }
      expect(new Set(events).size).toBe(3);
      const snapshot = await context.request.get(`/boards/${board}`); expect(snapshot.status()).toBe(200);
      expect((await snapshot.json()).lists.flatMap((item: { cards: { id: string; version: number }[] }) => item.cards)
        .find((item: { id: string }) => item.id === card).version).toBe(3);
    } finally { restoreWorker(); }
  });
}
