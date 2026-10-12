import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Paper, Stack, Typography } from '@mui/material';
import { WorkRequestError } from '../../api/workManagement';
import { readHoldDirectory, type HoldDirectory, type RetentionHold } from './retentionHoldClient';
import { recordClasses } from './RetentionPolicyEditor';

export function RetentionHoldDirectory({organizationId,actorId,enabled,select,accessLost}:{organizationId:string;actorId:string;
  enabled:boolean;select:(hold:RetentionHold)=>void;accessLost:()=>void}) {
  const [page,setPage]=useState<HoldDirectory>(),[busy,setBusy]=useState(false),[error,setError]=useState(false);
  const [boundaries,setBoundaries]=useState<(string|undefined)[]>([undefined]);
  const current=boundaries.at(-1),active=useRef<AbortController|undefined>(undefined),epoch=useRef(0);
  const callbacks=useRef({select,accessLost});callbacks.current={select,accessLost};
  const load=useCallback(async()=>{
    if (!enabled || active.current) return;
    const controller=new AbortController(),generation=++epoch.current;active.current=controller;setBusy(true);setError(false);
    try {
      const result=await readHoldDirectory(organizationId,actorId,controller.signal,current);
      if (!controller.signal.aborted && generation===epoch.current) setPage(result);
    } catch (value) {
      if (!controller.signal.aborted && generation===epoch.current) {
        setPage(undefined);setError(true);
        if(value instanceof WorkRequestError && [401,403,404].includes(value.status)) callbacks.current.accessLost();
      }
    } finally {if(generation===epoch.current){active.current=undefined;setBusy(false);}}
  },[organizationId,actorId,current,enabled]);
  useEffect(()=>{
    setPage(undefined);void load();
    const timer=window.setInterval(()=>{void load();},5000);
    const focus=()=>{void load();};window.addEventListener('focus',focus);
    return ()=>{++epoch.current;active.current?.abort();active.current=undefined;window.clearInterval(timer);window.removeEventListener('focus',focus);};
  },[load]);
  if(!enabled) return null;
  return <Paper component="section" aria-label="Available retention holds" variant="outlined" sx={{p:2}}><Stack spacing={2}>
    <Typography variant="h6" component="h2">Existing holds</Typography>
    {error && <Alert severity="error">Available holds could not be confirmed. <Button disabled={busy} onClick={()=>void load()}>Retry directory</Button></Alert>}
    {!page && !error && <Typography role="status">Loading available holds…</Typography>}
    {page?.items.length===0 && <Typography>No holds are available with your current access. Apply a reviewed hold when preservation is required.</Typography>}
    {page?.items.map(hold=><Paper key={hold.id} variant="outlined" sx={{p:2}}><Stack spacing={1}>
      <Typography>{hold.status==='active'?'Active preservation':'Released hold'} · Version {hold.version}</Typography>
      <Typography sx={{overflowWrap:'anywhere'}}>{hold.recordId?`Classified record ${hold.recordId}`
        : hold.recordClass?`${recordClasses[hold.recordClass]}${hold.category?` · ${hold.category}`:''}`:'Entire Organization'}</Typography>
      <Typography sx={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{hold.reason}</Typography>
      <Button disabled={busy} aria-label={`Review ${hold.status} hold ${hold.id}`} onClick={()=>callbacks.current.select(hold)}>Review hold</Button>
    </Stack></Paper>)}
    <Stack direction="row" spacing={1}>
      <Button disabled={busy || boundaries.length===1} onClick={()=>setBoundaries(previous=>previous.slice(0,-1))}>Previous holds</Button>
      <Button disabled={busy || !page?.nextAfter} onClick={()=>setBoundaries(previous=>[...previous,page!.nextAfter!])}>Next holds</Button>
      <Button disabled={busy} onClick={()=>void load()}>Refresh holds</Button>
    </Stack>
  </Stack></Paper>;
}
