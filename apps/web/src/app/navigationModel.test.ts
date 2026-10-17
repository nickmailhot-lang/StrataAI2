import { internalNavigation, organizationNavigation } from './navigationModel';

const source = (role: number, status = 0, id = 'org-a') => ({ organization: { id, status }, role });

it.each([0, 1])('UX-002: current Organization role %i discovers administration without duplicate destinations', role => {
  const links = organizationNavigation('org-a', source(role));
  expect(links.find(item => item.id === 'settings')?.to).toBe('/app/org-a/settings');
  expect(links.find(item => item.id === 'members')?.to).toBe('/app/org-a/members');
  expect(links.some(item => item.id === 'delete')).toBe(role === 0);
  expect(new Set(links.map(item => item.to)).size).toBe(links.length);
});

it('UX-002: ordinary membership does not imply Board administration or governance authority', () => {
  expect(organizationNavigation('org-a', source(2)).map(item => item.id)).toEqual(['archived-boards', 'leave']);
  expect(organizationNavigation('org-a', source(2)).some(item => item.to.includes('settings'))).toBe(false);
});

it.each([
  ['unconfirmed', undefined], ['foreign Organization', source(0, 0, 'org-b')],
  ['pending lifecycle', source(0, 1)], ['terminal lifecycle', source(0, 2)],
  ['unknown role', source(3)], ['negative role', source(-1)], ['fractional role', source(0.5)],
] as const)('UX-002: %s cannot retain privileged discovery', (_, value) => {
  expect(organizationNavigation('org-a', value)).toEqual([]);
});

it('UX-002: an unscoped page cannot inherit a previous Organization menu', () => {
  expect(organizationNavigation(undefined, source(0))).toEqual([]);
});

it('UX-002: Portal grants never construct an internal menu', () => {
  expect(internalNavigation('PORTAL', 'org-a', '/portal/org-a')).toEqual([]);
});

it.each(['/app/org-a/boards/board', '/app/org-a/boards/board/cards/card', '/app/org-a/boards/board/members', '/app/org-a/'])('UX-002: Board context remains identifiable at %s', path => {
    expect(internalNavigation('INTERNAL', 'org-a', path).filter(item => 'current' in item && item.current).map(item => item.id)).toEqual(['boards']);
  });

it('UX-002: future modules have no misleading destination', () => {
  const menu = internalNavigation('INTERNAL', 'org-a', '/app/org-a/notifications');
  const unavailable = menu.filter(item => 'unavailable' in item);
  expect(unavailable.map(item => item.id)).toEqual(['meetings', 'correspondence', 'documents', 'units']);
  expect(unavailable.every(item => !('to' in item))).toBe(true);
  expect(menu.filter(item => 'current' in item && item.current).map(item => item.id)).toEqual(['notifications']);
});
