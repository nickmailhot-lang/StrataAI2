import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview } from './keyboardAdmission';

const org='11111111-1111-4111-8111-111111111111', actor='22222222-2222-4222-8222-222222222222';
const board='33333333-3333-4333-8333-333333333333', list='44444444-4444-4444-8444-444444444444', card='55555555-5555-4555-8555-555555555555';
const rank='500000000000000000000000000000';
const searchPath=`/app/${org}/search`, cardPath=`/app/${org}/boards/${board}/cards/${card}`;
async function fixture(page: Page) {
  const source={title:'Original landscaping Card',failure:0,hold:false,waiters:[] as Array<()=>void>,searches:[] as string[],writes:[] as string[],returnActor:false,editable:true};
  await page.routeWebSocket('**/*',socket=>socket.onMessage(message=>{
    if(typeof message==='string'&&message.includes('"protocol"'))socket.send('{}\u001e');
  }));
  await page.route('**/*',async route=>{
    const request=route.request();if(!['fetch','xhr'].includes(request.resourceType()))return route.continue();
    const url=new URL(request.url()),path=url.pathname;
    const currentActor=source.returnActor&&new URL(page.url()).pathname===searchPath?board:actor;
    if(path.endsWith('/negotiate'))return route.fulfill({json:{negotiateVersion:1,connectionId:'search-return',connectionToken:'search-return',availableTransports:[{transport:'WebSockets',transferFormats:['Text','Binary']}]} });
    if(path==='/navigation/observations')return route.fulfill({json:{eventId:request.headers()['idempotency-key'],actorId:currentActor,organizationId:org,boardId:path.includes('/cards/')?board:null,eventType:'APPLICATION_CONTEXT_CHANGED',entityType:'Organization',entityId:org,version:1,metadata:{},createdAt:'2026-10-23T12:00:00Z'}});
    if(!['GET','HEAD'].includes(request.method()))source.writes.push(path);
    if(path==='/api/runtime')return route.fulfill({json:{service:'strataai-api',mode:'production',version:'0.1.0',revision:'a'.repeat(40)}});
    if(path==='/me')return route.fulfill({json:{id:currentActor,version:1,status:'ACTIVE',emailVerified:true,locale:'en-CA',timezone:'America/Vancouver'}});
    if(path.endsWith('/surface-access'))return route.fulfill({json:{organizationId:org,surface:'INTERNAL'}});
    if(path===`/organizations/${org}`)return route.fulfill({json:{organization:{id:org,name:'Search return council',status:0},role:2}});
    if(path===`/boards/${board}`)return route.fulfill({json:{board:{id:board,organizationId:org,name:'Council work',description:'Current authorized Board',version:1,lifecycleState:'active'},access:{canView:true,canEdit:source.editable,canMove:source.editable,canAdminister:false},lists:[{list:{id:list,name:'Planning',rank,version:1,lifecycleState:'active'},cards:[{id:card,title:'Original landscaping Card',description:'An existing Card',rank,version:1}]}]}});
    if(path==='/search'){
      expect(request.headers()['x-strataai-expected-actor']).toBe(currentActor);
      source.searches.push(url.search);if(source.hold)await new Promise<void>(resolve=>source.waiters.push(resolve));
      if(source.failure)return route.fulfill({status:source.failure,json:{}});
      return route.fulfill({json:{nextCursor:url.searchParams.has('after')?null:'return-next',items:[{sourceKind:'CARD',card:{id:card,organizationId:org,boardId:board,listId:list,title:source.title,version:1,lifecycleState:'active',dueAt:null,dueHasTime:false,dueComplete:false},boardName:'Council work',listName:'Planning',boardDateTimezone:null,labels:[{id:list,name:'Maintenance'}],members:[{userId:actor,displayName:'Council member'}],hasMoreLabels:false,hasMoreMembers:false}],interaction:{eventId:card,entityId:card,actorId:actor,version:1,eventType:'SEARCH_EXECUTED',entityType:'Search',organizationId:null,boardId:null,metadata:{},createdAt:'2026-10-23T12:00:00Z'}}});
    }
    return route.fulfill({status:503,json:{}});
  });
  return source;
}
async function choose(page:Page,name:string,value:string){await page.getByRole('combobox',{name,exact:true}).click();await page.getByRole('option',{name:value,exact:true}).click();}
for(const width of [1280,768,390,320]){
  test(`PRD-16 Search Card return preserves drafts and refreshes authorized results at ${width}px`,async({page},info)=>{
    test.setTimeout(60_000);
    await page.setViewportSize({width,height:900});const source=await fixture(page);await page.goto(searchPath);
    const query=page.getByRole('textbox',{name:'Card text',exact:true});
    await query.fill('landscaping');await page.getByRole('textbox',{name:'Label name'}).fill('maintenance');await page.getByRole('textbox',{name:'Member name'}).fill('council');await query.press('Enter');
    await expect(page.getByRole('link',{name:source.title,exact:true})).toBeVisible();
    await page.getByRole('button',{name:'Next search page',exact:true}).press('Enter');await expect(page.getByText('1 result on this page. Search complete.',{exact:true})).toBeVisible();
    await query.fill('unsent roof');await page.getByRole('textbox',{name:'Label name'}).fill('urgent');await page.getByRole('textbox',{name:'Member name'}).fill('new member');
    await choose(page,'Match criteria','Match any');await choose(page,'Search scope','Archived work');
    // Run the actual Card/dirty/return journey at enlarged text, rather than
    // changing text size after return or focusing the result on its behalf.
    await page.evaluate(()=>{document.documentElement.style.fontSize='200%';});
    const original=page.getByRole('link',{name:'Original landscaping Card',exact:true});await original.press('Enter');await expect(page).toHaveURL(new RegExp(cardPath+'$'));
    expect(await page.evaluate(()=>JSON.stringify({history:history.state,session:{...sessionStorage},local:{...localStorage}}))).not.toContain('unsent roof');
    const detail=page.getByRole('dialog',{name:'Card details',exact:true});const title=detail.getByRole('textbox',{name:'Card title',exact:true});await expect(title).toHaveValue('Original landscaping Card');
    await title.fill('Temporary unsaved Card edit');
    const guard=page.getByRole('dialog',{name:'Discard unsaved changes?',exact:true});
    await openAdmittedReview(detail.getByRole('button',{name:'Close',exact:true}),guard);
    await guard.getByRole('button',{name:'Keep editing',exact:true}).press('Enter');await expect(title).toHaveValue('Temporary unsaved Card edit');
    await openAdmittedReview(detail.getByRole('button',{name:'Close',exact:true}),guard);
    source.hold=true;source.title='Fresh authorized landscaping Card';
    await guard.getByRole('button',{name:'Discard changes and leave',exact:true}).press('Enter');await expect(page).toHaveURL(new RegExp(searchPath+'$'));
    await expect.poll(()=>source.waiters.length).toBeGreaterThan(0);await expect(query).toBeDisabled();await expect(query).toHaveValue('');
    await expect(page.getByRole('link',{name:'Original landscaping Card',exact:true})).toHaveCount(0);
    source.hold=false;source.waiters.splice(0).forEach(resolve=>resolve());
    const fresh=page.getByRole('link',{name:source.title,exact:true});await expect(fresh).toBeFocused();await expect(query).toHaveValue('unsent roof');
    await expect(page.getByRole('textbox',{name:'Label name'})).toHaveValue('urgent');await expect(page.getByRole('textbox',{name:'Member name'})).toHaveValue('new member');
    await expect(page.getByRole('combobox',{name:'Match criteria'})).toHaveText('Match any');await expect(page.getByRole('combobox',{name:'Search scope'})).toHaveText('Archived work');
    await expect(page.getByText('Search refreshed from the first page.',{exact:true})).toBeVisible();
    const returned=new URLSearchParams(source.searches.at(-1));expect(returned.get('q')).toBe('landscaping');expect(returned.get('label')).toBe('maintenance');expect(returned.get('member')).toBe('council');expect(returned.get('match')).toBe('all');expect(returned.get('scope')).toBe('active');expect(returned.has('after')).toBe(false);
    // A later automatic/online refresh must not drop the returned Card's
    // keyboard focus to body while protected old results are withdrawn.
    source.hold=true;await page.evaluate(()=>window.dispatchEvent(new Event('online')));
    await expect.poll(()=>source.waiters.length).toBeGreaterThan(0);
    await expect(page.getByRole('status')).toBeFocused();await expect(fresh).toHaveCount(0);
    expect(await page.getByRole('status').evaluate(el=>{const r=el.getBoundingClientRect(),header=document.querySelector('header')?.getBoundingClientRect();
      return r.top>=(header?.bottom??0)&&r.bottom<=innerHeight;})).toBe(true);
    source.hold=false;source.waiters.splice(0).forEach(resolve=>resolve());await expect(fresh).toBeFocused();
    expect(await fresh.evaluate(el=>{const r=el.getBoundingClientRect(),header=document.querySelector('header')?.getBoundingClientRect();
      return r.top>=(header?.bottom??0)&&r.bottom<=innerHeight&&el.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2));})).toBe(true);
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    expect((await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({path:info.outputPath(`search-card-return-viewport-${width}.png`)});
    await page.screenshot({path:info.outputPath(`search-card-return-${width}.png`),fullPage:true});
    await fresh.press('Enter');await expect(detail).toBeVisible();source.failure=403;
    await detail.getByRole('button',{name:'Close',exact:true}).press('Enter');await expect(page.getByText(/Search is unavailable/)).toBeVisible();
    await expect(query).toHaveValue('');await expect(page.getByRole('button',{name:'Retry previous search',exact:true})).toHaveCount(0);await expect(fresh).toHaveCount(0);
    expect(source.writes.filter(path=>/^\/(boards|cards|lists)(\/|$)/.test(path))).toEqual([]);
  });
}
test('PRD-16 browser Back restores a Viewer search but refuses retained criteria after an account change',async({page})=>{
  const source=await fixture(page);source.editable=false;await page.goto(searchPath);const query=page.getByRole('textbox',{name:'Card text',exact:true});await query.fill('landscaping');await query.press('Enter');
  await page.getByRole('link',{name:source.title,exact:true}).press('Enter');const detail=page.getByRole('dialog',{name:'Card details',exact:true});await expect(detail.getByRole('textbox',{name:'Card title',exact:true})).toBeDisabled();
  await page.goBack();await expect(query).toHaveValue('landscaping');await expect(page.getByRole('link',{name:source.title,exact:true})).toBeFocused();
  await page.getByRole('link',{name:source.title,exact:true}).press('Enter');await expect(detail).toBeVisible();const before=source.searches.length;source.returnActor=true;
  await detail.getByRole('button',{name:'Close',exact:true}).press('Enter');await expect(page.getByText(/Search is unavailable/)).toBeVisible();await expect(query).toHaveValue('');
  expect(source.searches.length).toBe(before);await expect(page.getByRole('link',{name:source.title,exact:true})).toHaveCount(0);
});
test('PRD-16 pointer Close and actual interval refresh preserve the returned Search target',async({page})=>{
  const source=await fixture(page);await page.goto(searchPath);
  const query=page.getByRole('textbox',{name:'Card text',exact:true});await query.fill('landscaping');
  await page.getByRole('button',{name:'Search',exact:true}).click();
  const result=page.getByRole('link',{name:source.title,exact:true});await expect(result).toBeVisible();
  await query.fill('unsent pointer draft');await result.click();
  await page.getByRole('dialog',{name:'Card details',exact:true}).getByRole('button',{name:'Close',exact:true}).click();
  await expect(result).toBeFocused();await expect(query).toHaveValue('unsent pointer draft');
  const before=source.searches.length;
  // Exercise the real ten-second interval, independently of the online-event
  // controlled read. This new bounded wait includes that existing interval.
  await expect.poll(()=>source.searches.length,{timeout:15_000}).toBeGreaterThan(before);
  await expect(result).toBeFocused();await expect(query).toHaveValue('unsent pointer draft');
});
