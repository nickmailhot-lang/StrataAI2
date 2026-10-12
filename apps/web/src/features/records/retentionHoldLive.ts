import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { notificationInstant, notificationUuid } from '../notifications/notificationInbox';
import { configurationAuthorityFallback } from '../organizations/configurationAuthorityFallback';
import { parseHold, readHold, type RetentionHold } from './retentionHoldClient';

export type HoldEvent = { eventId:string; eventType:'RETENTION_HOLD_APPLIED'|'RETENTION_HOLD_RELEASED'; organizationId:string;
  actorId:string; actingCapacity:'ORGANIZATION_OWNER'|'ORGANIZATION_ADMIN'; entityType:'RETENTION_HOLD'; entityId:string;
  entityVersion:1|2; correlationId:string; createdAt:string; metadata:{status:'ACTIVE'|'RELEASED';scopeKind:'RECORD'|'CATEGORY'|'CLASS'|'ORGANIZATION'} };
const exact = (row:object,keys:string[]) => Object.keys(row).sort().join(',') === [...keys].sort().join(',');
function sameApplication(a:RetentionHold,b:RetentionHold) {
  return (['id','organizationId','recordClass','category','recordId','reason','appliedBy','appliedAt'] as const).every(key=>a[key]===b[key]);
}
export function parseHoldSync(value:unknown, source:RetentionHold, actor:string, after:number, seen:Map<string,string>) {
  if (!value || typeof value!=='object' || Array.isArray(value)) throw new Error('Invalid hold source');
  const row=value as Record<string,unknown>;
  if (!exact(row,['organizationId','userId','holdId','current','afterVersion','resetRequired','events'])
    || row.organizationId!==source.organizationId || row.userId!==actor || row.holdId!==source.id || typeof row.resetRequired!=='boolean'
    || !Array.isArray(row.events) || row.events.length>2) throw new Error('Invalid hold source');
  const current=parseHold(row.current,source.organizationId,source.id);
  if (!sameApplication(current,source) || current.version>2 || row.afterVersion!==current.version
    || row.resetRequired!==(after>current.version)) throw new Error('Invalid hold source');
  const boundary=row.resetRequired?0:after;
  if (row.events.length!==current.version-boundary) throw new Error('Incomplete hold source');
  const events:HoldEvent[]=[];
  for (const [index,value] of row.events.entries()) {
    if (!value || typeof value!=='object' || Array.isArray(value)) throw new Error('Invalid hold event');
    const event=value as HoldEvent, version=boundary+index+1;
    if (!exact(event,['eventId','eventType','organizationId','actorId','actingCapacity','entityType','entityId','entityVersion','correlationId','createdAt','metadata'])
      || !notificationUuid(event.eventId) || event.eventId!==event.eventId.toLowerCase()
      || event.organizationId!==source.organizationId || event.entityId!==source.id || event.entityType!=='RETENTION_HOLD'
      || event.entityVersion!==version || event.eventType!==(version===1?'RETENTION_HOLD_APPLIED':'RETENTION_HOLD_RELEASED')
      || event.actorId!==(version===1?current.appliedBy:current.releasedBy)
      || !['ORGANIZATION_OWNER','ORGANIZATION_ADMIN'].includes(event.actingCapacity)
      || typeof event.correlationId!=='string' || !event.correlationId.trim() || event.correlationId.length>256
      || Array.from(event.correlationId).some(c=>c.charCodeAt(0)<32 || c.charCodeAt(0)>=127 && c.charCodeAt(0)<=159)
      || !event.metadata || typeof event.metadata!=='object' || !exact(event.metadata,['status','scopeKind'])
      || event.metadata.status!==(version===1?'ACTIVE':'RELEASED')
      || event.metadata.scopeKind!==(current.recordId?'RECORD':current.category?'CATEGORY':current.recordClass?'CLASS':'ORGANIZATION')
      || event.createdAt!==(version===1?current.appliedAt:current.releasedAt)) throw new Error('Invalid hold event');
    notificationInstant(event.createdAt);
    const fingerprint=JSON.stringify(event);
    if (seen.has(event.eventId) && seen.get(event.eventId)!==fingerprint || events.some(x=>x.eventId===event.eventId)) throw new Error('Changed hold event');
    events.push(event);
  }
  return {current,events,after:current.version,reset:row.resetRequired};
}
export function createHoldConnection() {
  return new HubConnectionBuilder().withUrl('/organizations/live/retention-holds',{
    transport:HttpTransportType.WebSockets,withCredentials:true,headers:{'X-StrataAI-Request':'1'},timeout:15_000,
  }).withAutomaticReconnect([0,2000,10_000,30_000]).configureLogging(LogLevel.None).build();
}
export function watchRetentionHold(options:{source:RetentionHold;actorId:string;pending:()=>void;
  confirmed:(hold:RetentionHold)=>void;accessLost:(error:WorkRequestError)=>void;connection?:ReturnType<typeof createHoldConnection>}) {
  let connection:ReturnType<typeof createHoldConnection>|undefined;
  let disposed=false,generation=0,after=0,attempt=0;
  let subscription:{dispose():void}|undefined,retry:ReturnType<typeof setTimeout>|undefined;
  let reading:AbortController|undefined; const seen=new Map<string,string>(),runtime=new AbortController();
  async function recover() {
    if (disposed || reading) return; const controller=new AbortController();reading=controller;
    try {
      const hold=await readHold(options.source.organizationId,options.source.id,options.actorId,controller.signal);
      if (!sameApplication(hold,options.source)) throw new Error('Changed hold application');
      if (!disposed && reading===controller) options.confirmed(hold);
    } catch (error) {
      if (!disposed && reading===controller) {
        options.pending();
        if (error instanceof WorkRequestError && [401,403,404].includes(error.status)) options.accessLost(error);
      }
    } finally {if (reading===controller) reading=undefined;}
  }
  const authority=configurationAuthorityFallback(()=>{void recover();});
  const unavailable=()=>{if (!disposed) {options.pending();authority.unavailable();}};
  function schedule() {
    if (disposed || retry!==undefined) return;
    retry=setTimeout(()=>{retry=undefined;void start();},[1000,2000,5000,10_000,30_000][Math.min(attempt++,4)]);
  }
  function subscribe() {
    const active=++generation;subscription?.dispose();
    subscription=connection!.stream<unknown>('Watch',options.source.organizationId,options.source.id,options.actorId,after).subscribe({
      next:value=>{
        if (disposed || active!==generation) return;
        try {
          const page=parseHoldSync(value,options.source,options.actorId,after,seen);
          authority.confirmed();reading?.abort();reading=undefined;
          if (page.reset) seen.clear();
          for (const event of page.events) seen.set(event.eventId,JSON.stringify(event));
          after=page.after;attempt=0;options.confirmed(page.current);
        } catch {fail();}
      },error:fail,complete:fail,
    });
    function fail() {
      queueMicrotask(()=>{
        if (disposed || active!==generation) return;
        ++generation;unavailable();void connection!.stop().catch(()=>{}).finally(schedule);
      });
    }
  }
  function ensureConnection() {
    if (connection) return connection;
    const selected=options.connection ?? createHoldConnection();connection=selected;
    selected.onreconnecting(()=>{if(!disposed){++generation;unavailable();}});
    selected.onreconnected(()=>{if(!disposed){options.pending();authority.pending();subscribe();}});
    selected.onclose(()=>{if(!disposed){++generation;unavailable();schedule();}});
    return selected;
  }
  async function start() {
    if (disposed) return;
    try {
      const mode=await boundedWorkRead(signal=>workRequest<unknown>('/api/runtime',{signal}),runtime.signal);
      if (disposed) return;
      if (!mode || typeof mode!=='object' || !('service' in mode) || mode.service!=='strataai-api'
        || !('mode' in mode) || !['production','demo'].includes(mode.mode as string)) throw new Error('Runtime unavailable');
      const selected=ensureConnection();
      await selected.start();if (disposed) {await selected.stop();return;} subscribe();
    } catch {if (!disposed) {unavailable();schedule();}}
  }
  // Bind lifecycle handlers before starting the connection, including a supplied
  // fixture connection. Generation fencing rejects callbacks from retired peers.
  try {ensureConnection();} catch {unavailable();}
  options.pending();authority.pending();void start();
  return ()=>{disposed=true;++generation;authority.dispose();runtime.abort();reading?.abort();clearTimeout(retry);subscription?.dispose();void connection?.stop().catch(()=>{});};
}
