import { StrictMode } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { GlobalSearchPage } from './GlobalSearchPage';
import { forgetSearchReturn, readSearchReturn, retainSearchReturn } from './searchReturn';
import { workRequest, WorkRequestError } from '../../api/workManagement';
vi.mock('../../api/workManagement', async () => ({
  workRequest: vi.fn(),
  WorkRequestError: (await vi.importActual<typeof import('../../api/workManagement')>('../../api/workManagement')).WorkRequestError,
  boundedWorkRead: (read: (signal: AbortSignal) => Promise<unknown>) => read(new AbortController().signal),
}));
const org='11111111-1111-4111-8111-111111111111', actor='22222222-2222-4222-8222-222222222222';
const board='33333333-3333-4333-8333-333333333333', card='44444444-4444-4444-8444-444444444444';
const profile=(id=actor)=>({id,version:1,status:'ACTIVE',emailVerified:true,locale:'en',timezone:'UTC'});
const page=(title='Fresh Card', include=true)=>({items:include?[{sourceKind:'CARD',card:{id:card,organizationId:org,boardId:board,listId:actor,title,version:1,lifecycleState:'active',dueAt:null,dueHasTime:false,dueComplete:false},boardDateTimezone:null,boardName:'Current Board',listName:'Current List',labels:[],members:[],hasMoreLabels:false,hasMoreMembers:false}]:[],nextCursor:null,interaction:{eventId:card,entityId:card,actorId:actor,eventType:'SEARCH_EXECUTED',entityType:'Search',version:1,organizationId:null,boardId:null,metadata:{},createdAt:'2026-10-23T12:00:00Z'}});
const draft={q:'unsent roof',label:'draft label',member:'draft member',match:'any' as const,scope:'archived' as const};
const applied={q:'landscaping',label:'',member:'',match:'all' as const,scope:'active' as const};
const request=vi.mocked(workRequest);
beforeEach(()=>{request.mockReset();forgetSearchReturn();});
afterEach(()=>{cleanup();forgetSearchReturn();});
function Exit(){const navigate=useNavigate();return <button onClick={()=>navigate(-1)}>Close Card</button>;}
function mount(strict=false){const app=<MemoryRouter initialEntries={[{pathname:`/app/${org}/search`,key:'origin'}]}><button>Other task</button><Routes>
  <Route path="/app/:organizationId/search" element={<GlobalSearchPage/>}/><Route path="/app/:organizationId/boards/:boardId/cards/:cardId" element={<Exit/>}/>
</Routes></MemoryRouter>;return render(strict?<StrictMode>{app}</StrictMode>:app);}
function saved(){retainSearchReturn({organization:org,actor,entry:'origin',card,draft,applied});}
function queue(title='Fresh Card',include=true){request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(page(title,include)).mockResolvedValueOnce(profile());}
async function depart(){mount();const query=screen.getByRole('textbox',{name:'Card text'});fireEvent.change(query,{target:{value:'landscaping'}});queue('Original Card');fireEvent.submit(query.closest('form')!);await screen.findByRole('link',{name:'Original Card'});fireEvent.change(query,{target:{value:'unsent roof'}});fireEvent.click(screen.getByRole('link',{name:'Original Card'}));await screen.findByRole('button',{name:'Close Card'});}
it('restores draft and applied criteria after an actual Card route roundtrip, using only fresh results',async()=>{
  await depart();queue();fireEvent.click(screen.getByRole('button',{name:'Close Card'}));
  const link=await screen.findByRole('link',{name:'Fresh Card'});await waitFor(()=>expect(link).toHaveFocus());
  expect(screen.queryByRole('link',{name:'Original Card'})).not.toBeInTheDocument();
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('unsent roof');
  expect(screen.getByText('Search refreshed from the first page.')).toBeVisible();
  expect(screen.getByRole('note',{name:'Unapplied Search filters'})).toBeVisible();
  const paths=request.mock.calls.filter(([p])=>String(p).startsWith('/search?')).map(([p])=>String(p));
  expect(paths).toHaveLength(2);expect(paths.every(p=>p.includes('q=landscaping')&&!p.includes('after='))).toBe(true);
  for(const [,options] of request.mock.calls.filter(([p])=>String(p).startsWith('/search?')))
    expect(new Headers(options?.headers).get('X-StrataAI-Expected-Actor')).toBe(actor);
  expect(readSearchReturn(org,'origin')).toBeUndefined();
});
it('withholds saved fields and old results until both fresh account checks complete',async()=>{
  saved();let finish!:(value:unknown)=>void;
  request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(page()).mockImplementationOnce(()=>new Promise(r=>{finish=r;}));mount();
  await waitFor(()=>expect(finish).toBeTypeOf('function'));
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');expect(screen.getByRole('textbox',{name:'Card text'})).toBeDisabled();
  expect(screen.queryByRole('link',{name:'Fresh Card'})).not.toBeInTheDocument();
  await act(async()=>finish(profile()));await screen.findByRole('link',{name:'Fresh Card'});
  for(const [name,value] of [['Card text',draft.q],['Label name',draft.label],['Member name',draft.member]])expect(screen.getByRole('textbox',{name})).toHaveValue(value);
  expect(screen.getByRole('combobox',{name:'Match criteria'})).toHaveTextContent('Match any');
  expect(screen.getByRole('combobox',{name:'Search scope'})).toHaveTextContent('Archived work');
});
it('does not send retained criteria for a different current account',async()=>{
  saved();request.mockResolvedValueOnce(profile(board));mount();await screen.findByText(/Search is unavailable/);
  expect(request.mock.calls.map(([p])=>p)).toEqual(['/me']);
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');expect(screen.queryByRole('button',{name:'Retry previous search'})).not.toBeInTheDocument();
});
it('withdraws drafts and results if the account changes during the restored read',async()=>{
  saved();request.mockResolvedValueOnce(profile()).mockResolvedValueOnce(page()).mockResolvedValueOnce(profile(board));mount();await screen.findByText(/Search is unavailable/);
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');expect(screen.queryByRole('link',{name:'Fresh Card'})).not.toBeInTheDocument();
});
it.each([401,403,404])('forgets the return on definitive %s without exposing private fields',async status=>{
  saved();request.mockResolvedValueOnce(profile()).mockRejectedValueOnce(new WorkRequestError(status,null));mount();await screen.findByText(/Search is unavailable/);
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');expect(screen.queryByRole('button',{name:'Retry previous search'})).not.toBeInTheDocument();
  expect(readSearchReturn(org,'origin')).toBeUndefined();
});
it('allows an explicit transient retry without exposing saved fields before successful revalidation',async()=>{
  saved();request.mockRejectedValueOnce(new WorkRequestError(503,null));mount();const retry=await screen.findByRole('button',{name:'Retry previous search'});
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');queue();fireEvent.click(retry);await screen.findByRole('link',{name:'Fresh Card'});
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue(draft.q);
});
it('uses stable feedback when the originating Card is absent from the new first page',async()=>{
  saved();queue('unused',false);mount();await screen.findByText('Search refreshed from the first page.');
  await waitFor(()=>expect(screen.getByRole('status')).toHaveFocus());expect(screen.queryByRole('link',{name:'Fresh Card'})).not.toBeInTheDocument();
});
it('does not reclaim focus after the user starts another task during revalidation',async()=>{
  saved();let finish!:(value:unknown)=>void;request.mockResolvedValueOnce(profile()).mockImplementationOnce(()=>new Promise(r=>{finish=r;})).mockResolvedValueOnce(profile());mount();
  await waitFor(()=>expect(finish).toBeTypeOf('function'));const other=screen.getByRole('button',{name:'Other task'});act(()=>other.focus());fireEvent.keyDown(other,{key:'Tab'});
  await act(async()=>finish(page()));await screen.findByRole('link',{name:'Fresh Card'});expect(other).toHaveFocus();
});
it('does not restore another history entry or Organization, or cache values in browser storage',()=>{
  saved();expect(readSearchReturn(org,'other')).toBeUndefined();expect(readSearchReturn(board,'origin')).toBeUndefined();
  expect(JSON.stringify({...sessionStorage,...localStorage})).not.toContain(draft.q);
  forgetSearchReturn(org);mount();expect(request).not.toHaveBeenCalled();expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue('');
});
it('survives StrictMode initialization and aborted effect replay without losing the return',async()=>{
  saved();request.mockImplementation(async p=>p==='/me'?profile():page());mount(true);
  await screen.findByRole('link',{name:'Fresh Card'});expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue(draft.q);
});
it('keeps returned Card focus on safe feedback during background refresh, then on the fresh result',async()=>{
  saved();queue();mount();const link=await screen.findByRole('link',{name:'Fresh Card'});await waitFor(()=>expect(link).toHaveFocus());
  let finish!:(value:unknown)=>void;
  request.mockResolvedValueOnce(profile()).mockImplementationOnce(()=>new Promise(r=>{finish=r;})).mockResolvedValueOnce(profile());
  fireEvent(window,new Event('online'));await waitFor(()=>expect(finish).toBeTypeOf('function'));
  expect(screen.getByRole('status')).toHaveFocus();expect(screen.queryByRole('link',{name:'Fresh Card'})).not.toBeInTheDocument();
  await act(async()=>finish(page('Revalidated Card')));
  await waitFor(()=>expect(screen.getByRole('link',{name:'Revalidated Card'})).toHaveFocus());
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue(draft.q);
});
it.each([false,true])('keeps background refresh focus safe when the Card disappears or access is lost (%s)',async denied=>{
  saved();queue();mount();await waitFor(()=>expect(screen.getByRole('link',{name:'Fresh Card'})).toHaveFocus());
  if(denied)request.mockRejectedValueOnce(new WorkRequestError(403,null));else queue('unused',false);
  fireEvent(window,new Event('online'));
  await screen.findByText(denied?/Search is unavailable/:/0 results on this page/);
  expect(screen.getByRole('status')).toHaveFocus();expect(screen.queryByRole('link',{name:'Fresh Card'})).not.toBeInTheDocument();
  expect(screen.getByRole('textbox',{name:'Card text'})).toHaveValue(denied?'':draft.q);
});
it('does not reclaim background-refresh focus after intervening keyboard input',async()=>{
  saved();queue();mount();await waitFor(()=>expect(screen.getByRole('link',{name:'Fresh Card'})).toHaveFocus());
  let finish!:(value:unknown)=>void;
  request.mockResolvedValueOnce(profile()).mockImplementationOnce(()=>new Promise(r=>{finish=r;})).mockResolvedValueOnce(profile());
  fireEvent(window,new Event('online'));await waitFor(()=>expect(finish).toBeTypeOf('function'));
  expect(screen.getByRole('status')).toHaveFocus();
  const other=screen.getByRole('button',{name:'Other task'});act(()=>other.focus());fireEvent.keyDown(other,{key:'Tab'});
  await act(async()=>finish(page()));await screen.findByRole('link',{name:'Fresh Card'});expect(other).toHaveFocus();
});
