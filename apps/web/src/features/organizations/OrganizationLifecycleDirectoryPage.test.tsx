import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { OrganizationLifecycleDirectoryPage, parseLifecycleDirectory } from './OrganizationLifecycleDirectoryPage';
const actor='22222222-2222-4222-8222-222222222222'; const org='55555555-5555-4555-8555-555555555555';
const later='66666666-6666-4666-8666-666666666666';
const profile={id:actor,status:'ACTIVE',version:1,emailVerified:true,locale:'en-CA',timezone:'UTC'};
const review={organizationId:org,actorId:actor,status:'SUSPENDED',version:3,updatedAt:'2026-10-10T12:00:00Z',actingCapacity:'ORGANIZATION_OWNER',name:'Retained Organization'};
const page={actorId:actor,items:[review],nextCursor:null};
const response=(body:unknown,status=200)=>new Response(JSON.stringify(body),{status});
function mount(){const router=createMemoryRouter([{path:'/app/lifecycle',element:<OrganizationLifecycleDirectoryPage/>},
  {path:'/login',element:<h1>Sign in destination</h1>}],{initialEntries:['/app/lifecycle']});render(<RouterProvider router={router}/>);return router;}
afterEach(()=>vi.unstubAllGlobals());
it('links a named suspended Organization directly to independent lifecycle review',async()=>{
  vi.stubGlobal('fetch',vi.fn(async(path:string)=>response(path==='/me'?profile:page)));mount();
  const link=await screen.findByRole('link',{name:'Retained Organization: Suspended; revision 3'});expect(link).toHaveAttribute('href',`/app/${org}/operational-lifecycle`);
  expect(screen.getByText('Lifecycle: Suspended. Revision 3.')).toBeInTheDocument();
});
it('keeps an empty authorized page usable when its routing continuation exists',async()=>{
  const fetcher=vi.fn(async(path:string)=>response(path==='/me'?profile:path.includes('?after=')?page:{actorId:actor,items:[],nextCursor:org}));
  vi.stubGlobal('fetch',fetcher);mount();const next=await screen.findByRole('button',{name:'Next lifecycle page'});fireEvent.click(next);
  await screen.findByRole('button',{name:'First lifecycle page'});await waitFor(()=>expect(screen.getByRole('heading',{name:'Organization lifecycle management'})).toHaveFocus());
  // The resumed page rejects a row at/before the cursor rather than exposing it.
  await screen.findByText(/Unable to confirm lifecycle access/);expect(screen.queryByText('Retained Organization')).not.toBeInTheDocument();
});
it('fences an account switch after directory IO and exposes no private names',async()=>{
  let accounts=0;vi.stubGlobal('fetch',vi.fn(async(path:string)=>response(path==='/me'?{...profile,id:++accounts===1?actor:later}:page)));mount();
  await screen.findByText('Sign in destination');expect(screen.queryByText('Retained Organization')).not.toBeInTheDocument();
});
it('withdraws old page content when foreground refresh loses permission',async()=>{
  let pages=0;vi.stubGlobal('fetch',vi.fn(async(path:string)=>response(path==='/me'?profile:++pages===1?page:{actorId:actor,items:[],nextCursor:null})));mount();
  await screen.findByRole('link',{name:'Retained Organization: Suspended; revision 3'});fireEvent(window,new Event('focus'));
  await screen.findByText('No organizations you can administer on this page.');expect(screen.queryByText('Retained Organization')).not.toBeInTheDocument();
});
it('aborts pending reads on navigation and renders only a fixed message for private failures',async()=>{
  let signal:AbortSignal|null|undefined;vi.stubGlobal('fetch',vi.fn((_path:string,options?:RequestInit)=>{signal=options?.signal;return new Promise<Response>(()=>{});}));mount();
  await waitFor(()=>expect(signal).toBeDefined());cleanup();expect(signal?.aborted).toBe(true);
});
it('validates actor, ordering, bounded pages, safe rows and advancing continuations',()=>{
  expect(parseLifecycleDirectory(page,actor)).toBeDefined();
  for(const bad of [{...page,actorId:later},{...page,items:[review,review]},{...page,items:[{...review,actingCapacity:'MEMBER'}]},
    {...page,nextCursor:actor},{...page,items:Array(51).fill(review)},{...page,extra:'Private'}])expect(parseLifecycleDirectory(bad,actor)).toBeUndefined();
  expect(parseLifecycleDirectory({actorId:actor,items:[],nextCursor:later},actor,org)).toBeDefined();
});
