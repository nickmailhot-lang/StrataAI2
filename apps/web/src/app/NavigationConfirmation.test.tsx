import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { NavigationConfirmation } from './NavigationConfirmation';
const actor = '11111111-1111-4111-8111-111111111111';
const organization = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const event = '44444444-4444-4444-8444-444444444444';
const target = { kind: 'board' as const, organization, board, version: 3 };
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
const source = { eventId: event, actorId: actor, organizationId: organization, boardId: board,
  eventType: 'BOARD_OPENED', entityType: 'Board', entityId: board, version: 3, metadata: {}, createdAt: '2026-10-05T12:00:00Z' };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('PRD-01 invalid navigation identities cannot dispatch account or observation reads', () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={{ ...target, board: 'board-placeholder' }} />);
  expect(fetch).not.toHaveBeenCalled();
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});

it('PRD-01 waits for admission and does not create another open for an entity edit', async () => {
  const fetch = vi.fn(async (path: string) => response(path === '/me' ? { id: actor } : source));
  vi.stubGlobal('fetch', fetch);
  const view = render(<NavigationConfirmation target={target} admitted={false} />);
  expect(fetch).not.toHaveBeenCalled();
  view.rerender(<NavigationConfirmation target={target} admitted />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
  view.rerender(<NavigationConfirmation target={{ ...target, version: 4 }} admitted />);
  expect(fetch.mock.calls.filter(([path]) => path !== '/me')).toHaveLength(1);
});
it('PRD-01 offers explicit recovery using the original key and revision after a lost reply', async () => {
  let writes = 0;
  const fetch = vi.fn(async (path: string) => {
    if (path === '/me') return response({ id: actor });
    if (++writes === 1) throw new TypeError('Lost response');
    return response(source);
  }); vi.stubGlobal('fetch', fetch);
  const view = render(<NavigationConfirmation target={target} />);
  const retry = await screen.findByRole('button', { name: 'Retry navigation confirmation' });
  view.rerender(<NavigationConfirmation target={{ ...target, version: 4 }} />);
  fireEvent.click(retry);
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull());
  await waitFor(() => expect(writes).toBe(2));
  const requests = fetch.mock.calls.filter(([path]) => path !== '/me') as unknown as [string, RequestInit][];
  expect(requests[0][0]).toBe(requests[1][0]);
  expect(new Headers(requests[0][1].headers).get('Idempotency-Key')).toBe(new Headers(requests[1][1].headers).get('Idempotency-Key'));
});
it('PRD-01 anonymous visitors create no personal navigation event', async () => {
  const fetch = vi.fn(async () => new Response('{}', { status: 401 })); vi.stubGlobal('fetch', fetch);
  render(<NavigationConfirmation target={target} />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  expect(fetch.mock.calls).toEqual([['/me', expect.anything()]]);
  expect(screen.queryByRole('button', { name: 'Retry navigation confirmation' })).toBeNull();
});
