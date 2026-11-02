import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Button } from '@mui/material';
import { CardCommentsControl } from './CardCommentsControl';
import { workRequest } from '../../api/workManagement';

vi.mock('../../api/workManagement', async original => ({ ...await original<typeof import('../../api/workManagement')>(), workRequest: vi.fn() }));
vi.mock('../auth/identityLive', () => ({ watchIdentity: vi.fn(() => vi.fn()) }));
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), cardId: id(3) };
const profile = { id: id(8), version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' };
const row = { id: id(4), organizationId: scope.organizationId, cardId: scope.cardId, authorId: profile.id,
  content: 'Existing comment', createdAt: '2026-10-03T08:00:00.123456Z', updatedAt: '2026-10-03T08:00:00.123456Z',
  version: 1, editedAt: null, deletedAt: null, deletedBy: null };
beforeEach(() => {
  vi.mocked(workRequest).mockReset().mockImplementation(async path => path === '/me' ? profile
    : { ...scope, cardVersion: 4, items: [row], nextCursor: null, canComment: true });
});

for (const edit of [false, true]) {
  for (const otherDestination of [false, true]) {
   for (const invalid of [false, true]) {
    it(`${edit ? 'edit' : 'new'} ${invalid ? 'invalid' : 'opened'} draft restores owned editor after access reread, respecting ${otherDestination ? 'another control' : 'the dialog fallback'}`, async () => {
      const props = { ...scope, version: 4, editable: true, disabled: false,
        onRefresh: vi.fn(), onBusyChange: vi.fn(), onRecoveryChange: vi.fn() };
      // Isolate component ownership from MUI's asynchronous trap redirects,
      // using the existing recovery test's connected-dialog fixture. Native
      // production-dialog restoration is covered by the real browser cases.
      const frame = (unavailable: boolean) => <div className="MuiDialog-root">
        <div className="MuiDialog-container" role="presentation"><div role="dialog" data-mui-focusable tabIndex={-1}>
          <CardCommentsControl {...props} unavailable={unavailable} /><Button>Another control</Button>
        </div></div>
      </div>;
      const view = render(frame(false));
      const review = screen.getByRole('button', { name: 'Review Card comments' });
      act(() => review.focus()); fireEvent.click(review);
      await screen.findByRole('button', { name: 'Add comment' });
      const launcher = screen.getByRole('button', { name: edit ? 'Edit comment' : 'Add comment' });
      act(() => launcher.focus()); fireEvent.click(launcher);
      const field = screen.getByRole('textbox', { name: edit ? 'Edit your comment' : 'New comment' });
      await waitFor(() => expect(field).toHaveFocus());
      const text = invalid ? 'Hello\u0001' : edit ? row.content : '';
      if (invalid) {
        fireEvent.change(field, { target: { value: text } });
        fireEvent.click(screen.getByRole('button', { name: 'Save comment' }));
        expect(field).toHaveAccessibleDescription('Enter valid comment text, up to 10000 characters.');
      }
      expect(field).toHaveFocus();
      const before = vi.mocked(workRequest).mock.calls.length;
      view.rerender(frame(true));
      expect(screen.queryByRole('textbox', { name: edit ? 'Edit your comment' : 'New comment' })).not.toBeInTheDocument();
      if (otherDestination) act(() => screen.getByRole('button', { name: 'Another control' }).focus());
      view.rerender(frame(false));
      const restored = screen.getByRole('textbox', { name: edit ? 'Edit your comment' : 'New comment' });
      await waitFor(() => expect(otherDestination ? screen.getByRole('button', { name: 'Another control' }) : restored).toHaveFocus());
      expect(restored).toHaveValue(text);
      expect(restored).toHaveAttribute('aria-invalid', String(invalid));
      if (invalid) expect(restored).toHaveAccessibleDescription('Enter valid comment text, up to 10000 characters.');
      expect(vi.mocked(workRequest).mock.calls).toHaveLength(before);
      expect(vi.mocked(workRequest).mock.calls.filter(([, init]) => init?.method)).toHaveLength(0);
    });
   }
  }
}
