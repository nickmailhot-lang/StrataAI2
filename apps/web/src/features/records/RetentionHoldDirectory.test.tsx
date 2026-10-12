import { cleanup,fireEvent,render,screen,waitFor } from '@testing-library/react';
import { ThemeProvider } from '@mui/material';
import { appTheme } from '../../theme/appTheme';
import { RetentionHoldDirectory } from './RetentionHoldDirectory';
const org='11111111-1111-4111-8111-111111111111',actor='22222222-2222-4222-8222-222222222222';
const id=(index:number)=>`10000000-0000-4000-8000-${index.toString().padStart(12,'0')}`;
const hold=(index:number)=>({id:id(index),organizationId:org,version:1,recordClass:null,category:null,recordId:null,status:'active',reason:`Preservation ${index}`,
  appliedBy:actor,appliedAt:'2026-10-11T01:00:00Z',releasedBy:null,releasedAt:null});
function fixture() {
  const server={denied:false,invalid:false};
  vi.stubGlobal('fetch',vi.fn(async(path:string)=>new Response(JSON.stringify(path==='/me'?{id:actor,version:1,status:'ACTIVE',emailVerified:true,locale:'en-CA',timezone:'UTC'}
    :path.includes('retention-policies')?{organizationId:org,items:[],nextAfter:null}
      :server.invalid?{organizationId:org,items:[hold(2),hold(1)],nextAfter:null}
        :path.includes('?after=')?{organizationId:org,items:[hold(26)],nextAfter:null}
          :{organizationId:org,items:Array.from({length:25},(_,index)=>hold(index+1)),nextAfter:id(25)}),{status:server.denied&&path!=='/me'?404:200})));
  return server;
}
function mount() {
  const select=vi.fn(),accessLost=vi.fn();
  render(<ThemeProvider theme={appTheme}><RetentionHoldDirectory organizationId={org} actorId={actor} enabled select={select} accessLost={accessLost}/></ThemeProvider>);
  return {select,accessLost};
}
afterEach(()=>{cleanup();vi.unstubAllGlobals();});
it('pages authorized results, selects a known source, and restores the prior boundary',async()=>{
  fixture();const actions=mount();await screen.findByText('Preservation 25');
  fireEvent.click(screen.getByRole('button',{name:`Review active hold ${id(3)}`}));expect(actions.select).toHaveBeenCalledWith(hold(3));
  fireEvent.click(screen.getByRole('button',{name:'Next holds'}));await screen.findByText('Preservation 26');
  expect(screen.queryByText('Preservation 1')).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'Next holds'})).toBeDisabled();
  fireEvent.click(screen.getByRole('button',{name:'Previous holds'}));await screen.findByText('Preservation 25');
  expect(screen.getByRole('button',{name:'Previous holds'})).toBeDisabled();
});
it('withdraws all private rows on definitive authority loss',async()=>{
  const server=fixture(),actions=mount();await screen.findByText('Preservation 25');server.denied=true;
  fireEvent.click(screen.getByRole('button',{name:'Refresh holds'}));await waitFor(()=>expect(actions.accessLost).toHaveBeenCalledOnce());
  expect(screen.queryByText('Preservation 25')).not.toBeInTheDocument();expect(screen.queryByRole('button',{name:/Review active hold/})).not.toBeInTheDocument();
});
it('rejects invalid ordering rather than exposing partial directory data',async()=>{
  const server=fixture();server.invalid=true;mount();await screen.findByRole('button',{name:'Retry directory'});
  expect(screen.queryByText('Preservation 1')).not.toBeInTheDocument();expect(screen.queryByText('Preservation 2')).not.toBeInTheDocument();
});
