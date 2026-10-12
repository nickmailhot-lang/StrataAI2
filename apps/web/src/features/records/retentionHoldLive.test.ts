import { parseHoldSync, watchRetentionHold, type HoldEvent, type createHoldConnection } from './retentionHoldLive';
import type { RetentionHold } from './retentionHoldClient';
const org='11111111-1111-4111-8111-111111111111',actor='22222222-2222-4222-8222-222222222222',id='33333333-3333-4333-8333-333333333333';
const source:RetentionHold={id,organizationId:org,version:1,recordClass:null,category:null,recordId:null,status:'active',reason:'Private reviewed reason',
  appliedBy:actor,appliedAt:'2026-10-11T01:00:00Z',releasedBy:null,releasedAt:null};
const released:RetentionHold={...source,version:2,status:'released',releasedBy:actor,releasedAt:'2026-10-11T02:00:00Z'};
function event(version:1|2):HoldEvent {return {eventId:version===1?'44444444-4444-4444-8444-444444444444':'55555555-5555-4555-8555-555555555555',
  eventType:version===1?'RETENTION_HOLD_APPLIED':'RETENTION_HOLD_RELEASED',organizationId:org,actorId:actor,actingCapacity:'ORGANIZATION_OWNER',entityType:'RETENTION_HOLD',
  entityId:id,entityVersion:version,correlationId:'original-audit',createdAt:version===1?source.appliedAt:released.releasedAt!,metadata:{status:version===1?'ACTIVE':'RELEASED',scopeKind:'ORGANIZATION'}};}
const page=(current:RetentionHold,events:HoldEvent[])=>({organizationId:org,userId:actor,holdId:id,current,afterVersion:current.version,resetRequired:false,events});
afterEach(()=>{vi.useRealTimers();vi.unstubAllGlobals();});
it('accepts original audit replay, quiet heartbeats and missed release while rejecting altered identity/scope/history',()=>{
  const seen=new Map<string,string>(); const first=parseHoldSync(page(source,[event(1)]),source,actor,0,seen);
  expect(first.current).toEqual(source);seen.set(event(1).eventId,JSON.stringify(event(1)));
  expect(parseHoldSync(page(source,[]),source,actor,1,seen).events).toEqual([]);
  expect(parseHoldSync(page(released,[event(2)]),source,actor,1,seen).current).toEqual(released);
  for (const invalid of [page(released,[]),{...page(source,[event(1)]),userId:id},page({...source,reason:'Unauthorized changed reason'},[event(1)]),
    page(source,[{...event(1),correlationId:'changed-original'}]),page(source,[{...event(1),entityId:actor}])])
    expect(()=>parseHoldSync(invalid,source,actor,invalid.current.version===2?1:0,seen)).toThrow();
});
it('fences retired stream observers and disposes authority recovery',async()=>{
  const observers: {next:(value:unknown)=>void}[]=[];let reconnect=()=>{},reconnected=()=>{};
  const connection={start:vi.fn(async()=>{}),stop:vi.fn(async()=>{}),stream:vi.fn(()=>({subscribe:(observer:{next:(value:unknown)=>void})=>{observers.push(observer);return {dispose:vi.fn()};}})),
    onreconnecting:(callback:()=>void)=>{reconnect=callback;},onreconnected:(callback:()=>void)=>{reconnected=callback;},onclose:vi.fn()};
  vi.stubGlobal('fetch',vi.fn(async()=>new Response(JSON.stringify({service:'strataai-api',mode:'production'}))));
  const confirmed=vi.fn(),pending=vi.fn(),lost=vi.fn();
  const stop=watchRetentionHold({source,actorId:actor,confirmed,pending,accessLost:lost,connection:connection as unknown as ReturnType<typeof createHoldConnection>});
  await vi.waitFor(()=>expect(observers).toHaveLength(1));observers[0].next(page(source,[event(1)]));expect(confirmed).toHaveBeenCalledWith(source);
  reconnect();reconnected();expect(observers).toHaveLength(2);confirmed.mockClear();
  observers[0].next(page(released,[event(2)]));expect(confirmed).not.toHaveBeenCalled();
  observers[1].next(page(released,[event(2)]));expect(confirmed).toHaveBeenCalledWith(released);
  stop();confirmed.mockClear();observers[1].next(page(source,[event(1)]));expect(confirmed).not.toHaveBeenCalled();expect(lost).not.toHaveBeenCalled();
});
it('rechecks actual HTTP source authority while no stream frame is confirmed and withdraws on definitive denial',async()=>{
  vi.useFakeTimers();let denied=false;
  vi.stubGlobal('fetch',vi.fn(async (path:string)=>new Response(JSON.stringify(path==='/api/runtime'?{service:'strataai-api',mode:'production'}
    : path==='/me'?{id:actor,version:1,status:'ACTIVE',emailVerified:true,locale:'en-CA',timezone:'UTC'}
      : path.includes('/retention-policies')?{organizationId:org,items:[],nextAfter:null}:released),{status:denied&&path.includes('/retention-holds/')?404:200})));
  const connection={start:vi.fn(async()=>{}),stop:vi.fn(async()=>{}),stream:()=>({subscribe:()=>({dispose:vi.fn()})}),onreconnecting:vi.fn(),onreconnected:vi.fn(),onclose:vi.fn()};
  const confirmed=vi.fn(),lost=vi.fn();const stop=watchRetentionHold({source,actorId:actor,confirmed,pending:vi.fn(),accessLost:lost,connection:connection as unknown as ReturnType<typeof createHoldConnection>});
  await vi.advanceTimersByTimeAsync(1100);expect(confirmed).toHaveBeenCalledWith(released);
  denied=true;await vi.advanceTimersByTimeAsync(1100);expect(lost).toHaveBeenCalledOnce();
  stop();const calls=confirmed.mock.calls.length;await vi.advanceTimersByTimeAsync(5000);expect(confirmed).toHaveBeenCalledTimes(calls);
});
