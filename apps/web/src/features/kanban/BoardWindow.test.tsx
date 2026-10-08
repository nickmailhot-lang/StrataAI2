import { ThemeProvider } from '@mui/material/styles';
import { appTheme } from '../../theme/appTheme';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { BoardWindow, type BoardWindowHeights } from './BoardWindow';

let active: { id: string } | null = null;
const measureDroppableContainers = vi.fn();
vi.mock('@dnd-kit/core', () => ({ useDndContext: () => ({ active, measureDroppableContainers }) }));
const cards = Array.from({ length: 5000 }, (_, i) => ({ id: `card-${i}` }));
const lists = Array.from({ length: 200 }, (_, i) => ({ id: `list-${i}` }));
const renderItem = (item: { id: string }) => <><button>Drag {item.id}</button><a href={'#' + item.id}>Open {item.id}</a></>;
function mount(items = cards, axis: 'lists' | 'cards' = 'cards', options: { pinned?: string[]; memory?: Map<string, number> } = {}) {
  const memory = options.memory ?? new Map<string, number>();
  return { memory, ...render(<BoardWindow items={items} axis={axis} memory={memory} memoryKey={axis} pinned={options.pinned} renderItem={renderItem} />) };
}
beforeEach(() => {
  active = null;
  measureDroppableContainers.mockClear();
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(1280);
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(400);
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('bounds mounted work for 5000 Cards and renders the canonical end after scrolling', () => {
  const { memory } = mount();
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
  expect(screen.queryByRole('link', { name: 'Open card-4999' })).not.toBeInTheDocument();
  const viewport = screen.getByLabelText('Cards');
  fireEvent.scroll(viewport, { target: { scrollTop: 679400 } });
  expect(screen.getByRole('link', { name: 'Open card-4999' })).toBeVisible();
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
  expect(memory.get('cards')).toBe(679400);
});
it('bounds mounted Lists at 200 and restores a retained Board scroll position after remount', () => {
  const memory = new Map<string, number>();
  const first = mount(lists, 'lists', { memory });
  expect(screen.getAllByRole('link').length).toBeLessThan(15);
  fireEvent.scroll(screen.getByLabelText('Kanban board'), { target: { scrollLeft: 4000 } });
  first.unmount(); mount(lists, 'lists', { memory });
  expect(screen.getByLabelText('Kanban board').scrollLeft).toBe(4000);
});
it('keeps admitted open work mounted and makes its focus visible after a detail closes', async () => {
  const { memory } = mount(cards, 'cards', { pinned: ['card-4999'] });
  const link = screen.getByRole('link', { name: 'Open card-4999' });
  act(() => link.focus());
  await waitFor(() => expect(link).toHaveFocus());
  expect(memory.get('cards')).toBeGreaterThan(600000);
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it('preserves active drag identities while their original row scrolls out of view', () => {
  active = { id: 'card:card-0' }; mount();
  fireEvent.scroll(screen.getByLabelText('Cards'), { target: { scrollTop: 20000 } });
  expect(screen.getByRole('button', { name: 'Drag card-0' })).toBeVisible();
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it.each(['cards', 'lists'] as const)('keeps the drag viewport when source focus is restored in %s', axis => {
  active = { id: axis === 'cards' ? 'card:card-0' : 'list-0' };
  const { memory } = mount(axis === 'cards' ? cards : lists, axis);
  const viewport = screen.getByLabelText(axis === 'cards' ? 'Cards' : 'Kanban board');
  const property = axis === 'cards' ? 'scrollTop' : 'scrollLeft';
  fireEvent.scroll(viewport, { target: { [property]: 20000 } });
  const reveal = vi.fn();
  const source = screen.getByRole('button', { name: axis === 'cards' ? 'Drag card-0' : 'Drag list-0' });
  source.scrollIntoView = reveal;
  act(() => source.focus({ preventScroll: true }));
  expect(viewport[property]).toBe(20000);
  expect(memory.get(axis)).toBe(20000);
  expect(reveal).not.toHaveBeenCalled();
});
it('keeps the horizontal drag viewport when focus returns inside its owning List', () => {
  active = { id: 'card:source-card' };
  const memory = new Map<string, number>();
  render(<BoardWindow items={lists} axis="lists" memory={memory} memoryKey="lists"
    ownsDrag={(item, id) => item.id === 'list-0' && id === 'source-card'} renderItem={renderItem} />);
  const viewport = screen.getByLabelText('Kanban board');
  fireEvent.scroll(viewport, { target: { scrollLeft: 20000 } });
  act(() => screen.getByRole('button', { name: 'Drag list-0' }).focus({ preventScroll: true }));
  expect(viewport.scrollLeft).toBe(20000);
  expect(memory.get('lists')).toBe(20000);
});
it('still reveals a different focused row while another Card is being dragged', () => {
  active = { id: 'card:card-0' };
  const { memory } = mount(cards, 'cards', { pinned: ['card-4999'] });
  const destination = screen.getByRole('link', { name: 'Open card-4999' });
  const reveal = vi.fn(); destination.scrollIntoView = reveal;
  act(() => destination.focus({ preventScroll: true }));
  expect(destination).toHaveFocus();
  expect(memory.get('cards')).toBeGreaterThan(600000);
  expect(reveal).toHaveBeenCalledWith({ block: 'nearest', inline: 'nearest' });
});
it('keeps the sensor source anchored while earlier row estimates refine, then restores canonical layout', () => {
  const observers: { callback: ResizeObserverCallback; nodes: Set<Element> }[] = [];
  class Observer {
    nodes = new Set<Element>();
    constructor(callback: ResizeObserverCallback) { observers.push({ callback, nodes: this.nodes }); }
    observe(node: Element) { this.nodes.add(node); }
    disconnect() { this.nodes.clear(); }
  }
  vi.stubGlobal('ResizeObserver', Observer);
  active = { id: 'card:card-2' };
  const memory = new Map<string, number>();
  const tree = () => <BoardWindow items={cards} axis="cards" memory={memory} memoryKey="cards" renderItem={renderItem} />;
  const view = render(tree());
  const source = screen.getByRole('link', { name: 'Open card-2' }).parentElement!;
  const before = getComputedStyle(source).top;
  const prior = screen.getByRole('link', { name: 'Open card-0' }).parentElement!;
  const observer = observers.find(value => value.nodes.has(prior))!;
  act(() => observer.callback([{ target: prior, borderBoxSize: [{ blockSize: 100 }] } as unknown as ResizeObserverEntry], {} as ResizeObserver));
  expect(getComputedStyle(source).top).toBe(before);
  expect(screen.getByRole('link', { name: 'Open card-3' }).parentElement).toHaveStyle({ top: '380px' });
  active = null; view.rerender(tree());
  expect(source).toHaveStyle({ top: '244px' });
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it.each([false, true])('refreshes moved drop positions after measured window layout changes only during a drag: %s', dragging => {
  const observers: { callback: ResizeObserverCallback; nodes: Set<Element> }[] = [];
  class Observer {
    nodes = new Set<Element>();
    constructor(callback: ResizeObserverCallback) { observers.push({ callback, nodes: this.nodes }); }
    observe(node: Element) { this.nodes.add(node); }
    unobserve(node: Element) { this.nodes.delete(node); }
    disconnect() { this.nodes.clear(); }
  }
  vi.stubGlobal('ResizeObserver', Observer);
  active = dragging ? { id: 'card:card-0' } : null;
  mount();
  const row = screen.getByRole('link', { name: 'Open card-1' }).closest('[data-board-window-id]')!;
  const observer = observers.find(value => value.nodes.has(row))!;
  measureDroppableContainers.mockClear();
  act(() => observer.callback([{ target: row, borderBoxSize: [{ blockSize: 100 }] } as unknown as ResizeObserverEntry], {} as ResizeObserver));
  if (dragging) expect(measureDroppableContainers).toHaveBeenCalled();
  else expect(measureDroppableContainers).not.toHaveBeenCalled();
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it('keeps the List owning an active Card drag mounted', () => {
  active = { id: 'card:source-card' };
  render(<BoardWindow items={lists} axis="lists" memory={new Map()} memoryKey="lists"
    ownsDrag={(item, id) => item.id === 'list-199' && id === 'source-card'} renderItem={renderItem} />);
  expect(screen.getByRole('button', { name: 'Drag list-199' })).toBeVisible();
  expect(screen.getAllByRole('link').length).toBeLessThan(15);
});
it('tabs forward and backward across a window boundary in canonical order', async () => {
  mount(); const last = screen.getAllByRole('link').at(-1)!;
  const index = Number(last.getAttribute('href')!.split('-').at(-1));
  act(() => last.focus()); fireEvent.keyDown(last, { key: 'Tab' });
  const next = await screen.findByRole('button', { name: `Drag card-${index + 1}` });
  await waitFor(() => expect(next).toHaveFocus());
  fireEvent.keyDown(next, { key: 'Tab', shiftKey: true });
  await waitFor(() => expect(screen.getByRole('link', { name: `Open card-${index}` })).toHaveFocus());
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it('preserves normal Board markup and all Cards below the virtualization threshold', () => {
  mount(cards.slice(0, 50));
  expect(screen.getAllByRole('link')).toHaveLength(50);
  expect(screen.queryByLabelText('Cards')).not.toBeInTheDocument();
});
it('bounds work and clamps the viewport after a canonical snapshot removes most rows', () => {
  const memory = new Map<string, number>([['cards', 679400]]);
  const view = mount(cards, 'cards', { memory });
  view.rerender(<BoardWindow items={cards.slice(0, 200)} axis="cards" memory={memory} memoryKey="cards" renderItem={renderItem} />);
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
  expect(screen.getByRole('link', { name: 'Open card-199' })).toBeVisible();
  expect(memory.get('cards')).toBeLessThan(30000);
});
it('does not skip offscreen Cards when a readonly List is nested in the horizontal window', async () => {
  const memory = new Map<string, number>();
  render(<BoardWindow items={lists} axis="lists" memory={memory} memoryKey="lists" renderItem={list =>
    <BoardWindow items={cards.slice(0, 200).map(card => ({ id: list.id + '/' + card.id }))} axis="cards" memory={memory} memoryKey={list.id}
      renderItem={item => <a href={'#' + item.id}>Open {item.id}</a>} />} />);
  const firstListLinks = screen.getAllByRole('link').filter(link => link.getAttribute('href')?.startsWith('#list-0/'));
  const last = firstListLinks.at(-1)!; const index = Number(last.getAttribute('href')!.split('-').at(-1));
  act(() => last.focus()); fireEvent.keyDown(last, { key: 'Tab' });
  await waitFor(() => expect(screen.getByRole('link', { name: `Open list-0/card-${index + 1}` })).toHaveFocus());
});
it('remeasures variable-height Cards without moving the retained scroll anchor', async () => {
  const observers: { callback: ResizeObserverCallback; nodes: Set<Element> }[] = [];
  class Observer {
    nodes = new Set<Element>();
    constructor(callback: ResizeObserverCallback) { observers.push({ callback, nodes: this.nodes }); }
    observe(node: Element) { this.nodes.add(node); }
    disconnect() { this.nodes.clear(); }
  }
  vi.stubGlobal('ResizeObserver', Observer);
  const { memory } = mount(cards, 'cards', { pinned: ['card-0'] });
  const viewport = screen.getByLabelText('Cards'); fireEvent.scroll(viewport, { target: { scrollTop: 20000 } });
  const row = screen.getByRole('link', { name: 'Open card-0' }).parentElement!;
  const observer = observers.find(value => value.nodes.has(row))!;
  act(() => observer.callback([{ target: row, borderBoxSize: [{ blockSize: 256 }] } as unknown as ResizeObserverEntry], {} as ResizeObserver));
  await waitFor(() => expect(memory.get('cards')).toBe(20128));
  expect(viewport.scrollTop).toBe(20128);
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it('restores the same canonical Card range using measured heights after a List unmounts', () => {
  const memory = new Map<string, number>([['cards', 10400]]);
  const heightMemory: BoardWindowHeights = new Map([['cards', {
    width: 1280, rows: new Map(cards.slice(0, 200).map(card => [card.id, 200])),
  }]]);
  const tree = () => <BoardWindow items={cards} axis="cards" memory={memory} memoryKey="cards" heightMemory={heightMemory} renderItem={renderItem} />;
  const first = render(tree());
  expect(screen.getByRole('link', { name: 'Open card-50' })).toBeVisible();
  const retained = screen.getAllByRole('link').map(link => link.getAttribute('href'));
  first.unmount(); render(tree());
  expect(screen.getAllByRole('link').map(link => link.getAttribute('href'))).toEqual(retained);
  expect(screen.getByLabelText('Cards').scrollTop).toBe(10400);
  expect(screen.getAllByRole('link').length).toBeLessThan(30);
});
it('invalidates another width’s row heights while retaining the canonical Card anchor', () => {
  const memory = new Map<string, number>([['cards', 10400]]);
  const heightMemory: BoardWindowHeights = new Map([['cards', {
    width: 640, rows: new Map(cards.slice(0, 200).map(card => [card.id, 200])),
  }]]);
  render(<BoardWindow items={cards} axis="cards" memory={memory} memoryKey="cards" heightMemory={heightMemory} renderItem={renderItem} />);
  expect(screen.getByRole('link', { name: 'Open card-50' })).toBeVisible();
  expect(memory.get('cards')).toBe(6800);
  expect(heightMemory.get('cards')?.width).toBe(1280);
  expect(heightMemory.get('cards')?.rows.size).toBe(0);
});

it('uses resolved CSS-variable theme spacing to mount the actual distant List', () => {
  expect(appTheme.spacing(2)).toContain('var(');
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: true, media: query, onchange: null,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() }));
  const computed = window.getComputedStyle.bind(window);
  vi.spyOn(window, 'getComputedStyle').mockImplementation((node, pseudo) => {
    const style = computed(node, pseudo);
    return node.getAttribute('aria-label') === 'Kanban board'
      ? new Proxy(style, { get: (target, property) => property === 'gap' ? '24px' : Reflect.get(target, property) })
      : style;
  });
  render(<ThemeProvider theme={appTheme}><BoardWindow items={lists} axis="lists"
    memory={new Map()} memoryKey="lists" renderItem={renderItem} /></ThemeProvider>);
  fireEvent.scroll(screen.getByLabelText('Kanban board'), { target: { scrollLeft: 194 * (320 + 24) } });
  const row = screen.getByRole('link', { name: 'Open list-194' }).closest('[data-board-window-axis="lists"]');
  expect(row).toHaveStyle({ left: `${194 * (320 + 24)}px` });
  expect(screen.getAllByRole('link').length).toBeLessThan(15);
  expect(screen.queryByRole('link', { name: 'Open list-0' })).not.toBeInTheDocument();
});

// PRD-06: scroll/context frames must not reconstruct unchanged List controls.
it('reuses mounted content across scroll frames and still renders authoritative item changes', () => {
  const memory = new Map<string, number>();
  const items = lists.map(item => ({ ...item, name: item.id }));
  const builder = vi.fn((item: { id: string; name: string }) => <button>{item.name}</button>);
  const view = render(<BoardWindow items={items} axis="lists" memory={memory} memoryKey="lists" renderItem={builder} />);
  const initialCalls = builder.mock.calls.length;
  fireEvent.scroll(screen.getByLabelText('Kanban board'), { target: { scrollLeft: 1 } });
  expect(builder).toHaveBeenCalledTimes(initialCalls);
  view.rerender(<BoardWindow items={items.map((item, index) => index === 0 ? { ...item, name: 'Updated authoritative List' } : item)}
    axis="lists" memory={memory} memoryKey="lists" renderItem={builder} />);
  expect(screen.getByRole('button', { name: 'Updated authoritative List' })).toBeVisible();
  expect(builder).toHaveBeenCalledTimes(initialCalls + 1);
});
