import { afterEach, expect, it } from 'vitest';
import { forgetSearchReturn, readSearchReturn, retainSearchReturn } from './searchReturn';
const org='11111111-1111-4111-8111-111111111111',other='22222222-2222-4222-8222-222222222222';
const criteria={q:'private needle',label:'maintenance',member:'council',match:'all' as const,scope:'active' as const};
afterEach(()=>forgetSearchReturn());
it('retains a detached whitelist of criteria without result snapshots or cursors',()=>{
  const draft={...criteria,items:[{title:'protected result'}],nextCursor:'private cursor'};
  retainSearchReturn({organization:org,actor:org,entry:'origin',card:org,draft,applied:draft});draft.q='changed input';
  expect(readSearchReturn(org,'origin')?.draft).toEqual(criteria);
  expect(JSON.stringify(readSearchReturn(org,'origin'))).not.toContain('protected result');
  expect(JSON.stringify(readSearchReturn(org,'origin'))).not.toContain('private cursor');
});
it('keeps a single bounded record rather than accumulating prior searches',()=>{
  retainSearchReturn({organization:org,actor:org,entry:'first',card:org,draft:criteria,applied:criteria});
  retainSearchReturn({organization:other,actor:org,entry:'second',card:other,draft:criteria,applied:criteria});
  expect(readSearchReturn(org,'first')).toBeUndefined();expect(readSearchReturn(other,'second')).toBeDefined();
  forgetSearchReturn(org);expect(readSearchReturn(other,'second')).toBeDefined();
  forgetSearchReturn(other);expect(readSearchReturn(other,'second')).toBeUndefined();
});
it('refuses invalid identities and oversized fields and retires the previous return',()=>{
  retainSearchReturn({organization:org,actor:org,entry:'first',card:org,draft:criteria,applied:criteria});
  retainSearchReturn({organization:org,actor:'not-an-account',entry:'first',card:org,draft:criteria,applied:criteria});
  expect(readSearchReturn(org,'first')).toBeUndefined();
  retainSearchReturn({organization:org,actor:org,entry:'first',card:org,draft:{...criteria,q:'x'.repeat(161)},applied:criteria});
  expect(readSearchReturn(org,'first')).toBeUndefined();
});
