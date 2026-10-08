import { StrictMode, useState } from 'react';
import { Button, Dialog, DialogTitle } from '@mui/material';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ImmediateDialogTransition } from './ImmediateDialogTransition';

it('opens its zero-duration backdrop without a synchronous scroll-position read', async () => {
  let backdropReads = 0;
  const scroll = vi.spyOn(Element.prototype, 'scrollTop', 'get').mockImplementation(function (this: Element) {
    if (this.classList.contains('MuiBackdrop-root')) backdropReads++;
    return 0;
  });
  try {
    function Fixture() {
      const [open, setOpen] = useState(false);
      return <><Button onClick={() => setOpen(true)}>Open details</Button>
        <Dialog open={open} slots={{ transition: ImmediateDialogTransition }} transitionDuration={0}
          slotProps={{ backdrop: { slots: { transition: ImmediateDialogTransition } } }}
          onClose={() => setOpen(false)}><DialogTitle>Details</DialogTitle></Dialog></>;
    }
    render(<Fixture />);
    fireEvent.click(screen.getByRole('button', { name: 'Open details' }));
    await screen.findByRole('dialog', { name: 'Details' });
    expect(backdropReads).toBe(0);
  } finally { scroll.mockRestore(); }
});

it('keeps native MUI focus containment, Escape closure and repeat disclosure in StrictMode', async () => {
  const exited = vi.fn();
  function Fixture() {
    const [open, setOpen] = useState(false);
    return <><Button onClick={() => setOpen(true)}>Open details</Button>
      <Dialog open={open} slots={{ transition: ImmediateDialogTransition }} transitionDuration={0}
        onClose={() => setOpen(false)} slotProps={{ backdrop: { slots: { transition: ImmediateDialogTransition } }, transition: { onExited: exited } }}>
        <DialogTitle>Details</DialogTitle>
      </Dialog></>;
  }
  render(<StrictMode><Fixture /></StrictMode>);
  const opener = screen.getByRole('button', { name: 'Open details' });
  for (let index = 0; index < 2; index++) {
    act(() => opener.focus()); fireEvent.click(opener);
    const dialog = await screen.findByRole('dialog', { name: 'Details' });
    const container = dialog.parentElement!;
    expect(container).toHaveAttribute('tabindex', '-1');
    await waitFor(() => expect(container.contains(document.activeElement)).toBe(true));
    // An attempt to focus the covered page must remain inside the modal.
    act(() => opener.focus());
    await waitFor(() => expect(container.contains(document.activeElement)).toBe(true));
    fireEvent.keyDown(container, { key: 'Escape', code: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Open details' })).toHaveFocus());
    expect(exited).toHaveBeenCalledTimes(index + 1);
  }
});
