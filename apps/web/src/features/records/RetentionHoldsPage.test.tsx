import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ThemeProvider } from '@mui/material';
import { appTheme } from '../../theme/appTheme';
import { RetentionHoldsPage } from './RetentionHoldsPage';

vi.mock('./retentionHoldLive',()=>({watchRetentionHold:vi.fn((options:{source:unknown;confirmed:(value:unknown)=>void})=>{queueMicrotask(()=>options.confirmed(options.source));return vi.fn();})}));
const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
function fixture() {
  const state = { unavailable:false, denied:false, requests:[] as { path:string; body:string; key:string }[], holds:new Map<string,object>() };
  vi.stubGlobal('fetch',vi.fn(async (input:string, options:RequestInit = {}) => {
    if (input === '/me') return new Response(JSON.stringify({ id:actor,version:1,status:'ACTIVE',emailVerified:true,locale:'en-CA',timezone:'UTC' }));
    if (state.denied) return new Response('{}',{status:404});
    if (input.includes('/retention-policies')) return new Response(JSON.stringify({ organizationId:org,items:[],nextAfter:null }));
    if (input.endsWith('/retention-holds')) return new Response(JSON.stringify({organizationId:org,items:[...state.holds.values()],nextAfter:null}));
    if (input.endsWith('/review')) {
      const parts=input.split('/'), id=parts.at(-2)!, kind=parts.at(-3)!;
      const card=kind === 'card' ? id : '44444444-4444-4444-8444-444444444444';
      return new Response(JSON.stringify({ organizationId:org,recordId:id,kind,parentCardId:kind === 'card' ? null : card,
        cardId:card,boardId:'55555555-5555-4555-8555-555555555555',listId:'66666666-6666-4666-8666-666666666666',
        cardVersion:1,listVersion:1,sourceVersion:1,cardState:'active',listState:'active',sourceState:'active',createdAt:'2026-10-10T01:00:00Z' }));
    }
    if (options.method === 'POST') {
      state.requests.push({ path:input,body:options.body as string,key:new Headers(options.headers).get('Idempotency-Key')! });
      if (state.unavailable) return new Response('{}',{status:503});
      const id = input.split('/').at(-2)!, body = JSON.parse(options.body as string);
      const current = state.holds.get(id) ?? { id,organizationId:org,version:1,recordClass:body.recordClass,category:body.category,
        recordId:body.recordId,status:'active',reason:body.reason,appliedBy:actor,appliedAt:'2026-10-11T01:00:00Z',releasedBy:null,releasedAt:null };
      const result = input.endsWith('/release') ? { ...current,version:2,status:'released',releasedBy:actor,releasedAt:'2026-10-11T02:00:00Z' } : current;
      state.holds.set(id,result); return new Response(JSON.stringify(result));
    }
    const id=input.split('/').at(-1)!; return new Response(JSON.stringify(state.holds.get(id) ?? {}),{status:state.holds.has(id)?200:404});
  })); return state;
}
function mount() {
  const router=createMemoryRouter([{path:'/app/:organizationId/retention-holds',element:<RetentionHoldsPage />}],{initialEntries:[`/app/${org}/retention-holds`]});
  render(<ThemeProvider theme={appTheme}><RouterProvider router={router}/></ThemeProvider>);
}
async function prepare() {
  await waitFor(() => expect(screen.getByRole('textbox',{name:'Preservation reason'})).toBeEnabled());
  fireEvent.change(screen.getByRole('textbox',{name:'Preservation reason'}),{target:{value:'Preserve private records'}});
  expect(screen.getByRole('button',{name:'Apply confirmed hold'})).toBeDisabled();
  fireEvent.click(screen.getByRole('checkbox',{name:'I reviewed this scope and confirm preservation under this hold.'}));
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); sessionStorage.clear(); });
describe('PRD-70 confirmed MUI holds',() => {
  it.each([['Card','card','operationalWork'],['Comment','cardComment','operationalWork'],['Attachment','attachment','document']])(
    'requires classified %s review and submits its actual relationship evidence',async (label,kind,recordClass) => {
      const server=fixture(); mount();
      await waitFor(() => expect(screen.getByRole('combobox',{name:'Hold scope'})).toBeEnabled());
      fireEvent.mouseDown(screen.getByRole('combobox',{name:'Hold scope'}));
      fireEvent.click(await screen.findByRole('option',{name:'Individual classified record'}));
      fireEvent.mouseDown(screen.getByRole('combobox',{name:'Source type'})); fireEvent.click(await screen.findByRole('option',{name:label}));
      fireEvent.change(screen.getByRole('textbox',{name:'Classified record identifier'}),{target:{value:'33333333-3333-4333-8333-333333333333'}});
      expect(screen.getByRole('checkbox',{name:'I reviewed this scope and confirm preservation under this hold.'})).toBeDisabled();
      fireEvent.click(screen.getByRole('button',{name:'Review classified source'}));
      await screen.findByText(new RegExp(`Reviewed ${kind}:`)); await prepare();
      fireEvent.click(screen.getByRole('button',{name:'Apply confirmed hold'}));
      await screen.findByText('Original request acknowledged. Load the current hold to review its present status.');
      expect(JSON.parse(server.requests[0].body)).toMatchObject({recordClass,recordId:'33333333-3333-4333-8333-333333333333',
        review:{kind,boardId:'55555555-5555-4555-8555-555555555555',sourceVersion:1,cardVersion:1},confirmed:true});
    });
  it('requires confirmation, loads persisted current state, and releases its reviewed version',async () => {
    const server=fixture(); mount(); await prepare();
    fireEvent.click(screen.getByRole('button',{name:'Apply confirmed hold'}));
    await screen.findByText('Original request acknowledged. Load the current hold to review its present status.');
    expect(server.requests).toHaveLength(1);
    expect(JSON.parse(server.requests[0].body)).toMatchObject({recordClass:null,category:null,recordId:null,review:null,confirmed:true});
    fireEvent.click(screen.getByRole('button',{name:'Load current hold'})); await screen.findByText('Reason: Preserve private records');
    expect(screen.getByRole('button',{name:'Release confirmed hold'})).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox',{name:'I reviewed this version and confirm release of this hold.'}));
    fireEvent.click(screen.getByRole('button',{name:'Release confirmed hold'}));
    await waitFor(() => expect(server.requests).toHaveLength(2));
    expect(JSON.parse(server.requests[1].body)).toEqual({version:1,confirmed:true});
    await waitFor(() => expect(screen.queryByText('Original release request awaiting acknowledgment')).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button',{name:'Load current hold'})); await screen.findByText('Status: released. Version: 2.');
    expect(screen.queryByRole('button',{name:'Release confirmed hold'})).not.toBeInTheDocument();
  });
  it('recovers an uncertain original request across remount with identical body, identifier and key',async () => {
    const server=fixture(); server.unavailable=true; mount(); await prepare();
    fireEvent.click(screen.getByRole('button',{name:'Apply confirmed hold'}));
    await screen.findByText('The acknowledgment could not be confirmed. Retry the retained original request.');
    const original=server.requests[0]; expect(screen.getByRole('button',{name:'Apply confirmed hold'})).toBeDisabled();
    cleanup(); server.unavailable=false; mount();
    const retry=await screen.findByRole('button',{name:'Retry original request'}); await waitFor(() => expect(retry).toBeEnabled()); fireEvent.click(retry);
    await screen.findByText('Original request acknowledged. Load the current hold to review its present status.');
    expect(server.requests[1]).toEqual(original); expect(sessionStorage.length).toBe(0);
  });
  it('withdraws private reason, pending recovery and controls after authority loss',async () => {
    const server=fixture(); server.unavailable=true; mount(); await prepare();
    fireEvent.click(screen.getByRole('button',{name:'Apply confirmed hold'}));
    await screen.findByRole('button',{name:'Retry original request'}); server.denied=true;
    fireEvent.click(screen.getByRole('button',{name:'Retry original request'}));
    await screen.findByText('Retention holds are no longer available with your current access.');
    expect(screen.queryByRole('textbox',{name:'Preservation reason'})).not.toBeInTheDocument();
    expect(screen.queryByRole('button',{name:'Retry original request'})).not.toBeInTheDocument();
    expect(server.requests).toHaveLength(1); expect(sessionStorage.length).toBe(0);
  });
});
