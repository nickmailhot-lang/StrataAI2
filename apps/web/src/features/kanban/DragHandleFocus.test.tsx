import { DndContext } from '@dnd-kit/core';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { CardDragItem } from './CardDragItem';
import { ListDragColumn } from './ListDragColumn';

for (const kind of ['Card', 'List'] as const) {
  function fixture(disabled: boolean, available = true, id = 'source') {
    return <DndContext><button>Another action</button>{kind === 'Card'
      ? <CardDragItem id={id} title="Source" disabled={disabled} available={available}><a href="/card">Open Card</a></CardDragItem>
      : <ListDragColumn id={id} name="Source" disabled={disabled} available={available}><span>List content</span></ListDragColumn>}
    </DndContext>;
  }
  const handleName = `Drag Source ${kind.toLowerCase()}`;
  it(`${kind} restores its owned handle after temporary admission disables and native blur`, () => {
    const view = render(fixture(false)); const handle = screen.getByRole('button', { name: handleName });
    act(() => handle.focus()); view.rerender(fixture(true)); expect(handle).toBeDisabled();
    // Browsers blur a newly disabled button; jsdom needs that native effect.
    act(() => handle.blur()); expect(document.body).toHaveFocus();
    view.rerender(fixture(false)); expect(handle).toHaveFocus();
  });
  it(`${kind} does not reclaim focus from another control during admission`, () => {
    const view = render(fixture(false)); const handle = screen.getByRole('button', { name: handleName });
    act(() => handle.focus()); view.rerender(fixture(true)); act(() => handle.blur());
    const other = screen.getByRole('button', { name: 'Another action' }); act(() => other.focus());
    view.rerender(fixture(false)); expect(other).toHaveFocus();
  });
  it(`${kind} retires focus when current movement permission removes the handle`, () => {
    const view = render(fixture(false)); const handle = screen.getByRole('button', { name: handleName });
    act(() => handle.focus()); view.rerender(fixture(true)); act(() => handle.blur());
    view.rerender(fixture(false, false)); expect(screen.queryByRole('button', { name: handleName })).not.toBeInTheDocument();
    view.rerender(fixture(false)); expect(screen.getByRole('button', { name: handleName })).not.toHaveFocus();
  });
  it(`${kind} respects a pointer choice outside its handle even without a focusable destination`, () => {
    const view = render(fixture(false)); const handle = screen.getByRole('button', { name: handleName });
    act(() => handle.focus()); view.rerender(fixture(true)); act(() => handle.blur());
    fireEvent.pointerDown(document.body);
    view.rerender(fixture(false)); expect(document.body).toHaveFocus();
  });
  it(`${kind} does not transfer an old focus request to a replacement identity`, () => {
    const view = render(fixture(false)); const handle = screen.getByRole('button', { name: handleName });
    act(() => handle.focus()); view.rerender(fixture(true)); act(() => handle.blur());
    view.rerender(fixture(false, true, 'replacement')); expect(document.body).toHaveFocus();
  });
}
