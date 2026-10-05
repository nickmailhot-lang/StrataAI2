import { useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Box, Stack, useMediaQuery, useTheme } from '@mui/material';
import { useDndContext } from '@dnd-kit/core';

type Item = { id: string };
export type BoardWindowHeights = Map<string, { width: number; rows: Map<string, number> }>;
type Props<T extends Item> = {
  items: T[]; axis: 'lists' | 'cards'; memory: Map<string, number>; memoryKey: string;
  heightMemory?: BoardWindowHeights;
  pinned?: string[]; end?: ReactNode; renderItem: (item: T) => ReactNode;
  ownsDrag?: (item: T, activeId: string) => boolean;
};
const focusable = (row: HTMLElement) => Array.from(row.querySelectorAll<HTMLElement>(
  'a[href],button,input,select,textarea,[tabindex]',
)).filter(node => node.tabIndex >= 0 && !node.matches(':disabled') && !node.closest('[hidden],[inert],[aria-hidden="true"]'));

// PRD-04/06: only viewport rows and bounded overscan mount. Small Boards retain
// their existing layout. Stable identities pin focus, open work and active drags.
export function BoardWindow<T extends Item>(props: Props<T>) {
  return props.items.length > (props.axis === 'lists' ? 20 : 100)
    ? <Windowed {...props} />
    : props.axis === 'lists'
      ? <Box aria-label="Kanban board" data-kanban-scroll sx={{ display: 'grid', gridAutoFlow: 'column', gridAutoColumns: { xs: '82vw', sm: 320 }, gap: 2, overflowX: 'auto', pb: 2 }}>
        {props.items.map(item => <Box key={item.id}>{props.renderItem(item)}</Box>)}{props.end}
      </Box>
      : <Stack spacing={1} sx={{ mt: 2 }}>{props.items.map(item => <Box key={item.id}>{props.renderItem(item)}</Box>)}</Stack>;
}

function Windowed<T extends Item>({ items, axis, memory, memoryKey, heightMemory, pinned = [], end, renderItem, ownsDrag }: Props<T>) {
  const horizontal = axis === 'lists'; const theme = useTheme();
  const desktop = useMediaQuery(theme.breakpoints.up('sm'));
  const { active } = useDndContext();
  const root = useRef<HTMLDivElement>(null); const rows = useRef(new Map<string, HTMLDivElement>());
  const [viewport, setViewport] = useState({ offset: memory.get(memoryKey) ?? 0, size: horizontal ? 1280 : 400, width: 1280 });
  const [heights, setHeights] = useState(() => heightMemory?.get(memoryKey)?.rows ?? new Map<string, number>());
  const measuredWidth = useRef(heightMemory?.get(memoryKey)?.width);
  const [focused, setFocused] = useState<string>();
  const pendingFocus = useRef<{ id: string; reverse: boolean } | undefined>(undefined);
  const pendingAnchor = useRef<number | undefined>(undefined);
  const columnSize = desktop ? 320 : viewport.width * 0.82;
  const gap = Number.parseFloat(theme.spacing(horizontal ? 2 : 1));
  const layout = useMemo(() => {
    let total = 0;
    const entries = items.map(item => {
      const start = total, size = horizontal ? columnSize : heights.get(item.id) ?? 128;
      total += size + gap; return { item, start, size };
    });
    return { entries, total: Math.max(0, total - gap) };
  }, [items, horizontal, columnSize, heights, gap]);
  const measured = useRef(heights); measured.current = heights;
  const positions = useRef(layout); positions.current = layout;
  const activeId = active ? String(active.id).replace(/^card:/, '') : undefined;
  const indices = useMemo(() => {
    const selected = new Set<number>();
    const visible = layout.entries.findIndex(row => row.start + row.size >= viewport.offset);
    const first = Math.max(0, (visible < 0 ? layout.entries.length - 1 : visible) - 2);
    let last = first;
    while (last < layout.entries.length && layout.entries[last].start <= viewport.offset + viewport.size) last++;
    for (let i = first; i < Math.min(items.length, last + 2); i++) selected.add(i);
    const retained = new Set([...pinned, focused, activeId]);
    layout.entries.forEach((row, i) => { if (retained.has(row.item.id) || activeId && ownsDrag?.(row.item, activeId)) selected.add(i); });
    return [...selected].sort((a, b) => a - b);
  }, [layout, viewport.offset, viewport.size, items.length, pinned, focused, activeId, ownsDrag]);

  useLayoutEffect(() => {
    const element = root.current; if (!element) return;
    if (horizontal) element.scrollLeft = memory.get(memoryKey) ?? 0;
    else element.scrollTop = memory.get(memoryKey) ?? 0;
    const measure = () => {
      const width = element.clientWidth;
      if (!horizontal && width > 0 && measuredWidth.current !== width) {
        if (measuredWidth.current !== undefined) {
          // Text/cover heights at another width cannot recover this viewport.
          // Retain the canonical anchor while rebuilding its measurements.
          const previous = positions.current.entries;
          const index = previous.findIndex(row => row.start + row.size > element.scrollTop);
          if (index >= 0) pendingAnchor.current = index * (128 + gap) + Math.min(127, Math.max(0, element.scrollTop - previous[index].start));
          const cleared = new Map<string, number>(); measured.current = cleared; setHeights(cleared);
        }
        measuredWidth.current = width;
        heightMemory?.set(memoryKey, { width, rows: measured.current });
      }
      setViewport(value => ({ ...value,
        size: (horizontal ? width : element.clientHeight) || value.size,
        width: window.innerWidth,
      }));
    };
    measure();
    if (typeof ResizeObserver === 'undefined') { window.addEventListener('resize', measure); return () => window.removeEventListener('resize', measure); }
    const observer = new ResizeObserver(measure); observer.observe(element); return () => observer.disconnect();
  }, [horizontal, memory, memoryKey, heightMemory, gap]);

  useLayoutEffect(() => {
    if (horizontal || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(entries => {
      const changes = entries.flatMap(entry => {
        const id = (entry.target as HTMLElement).dataset.boardWindowId;
        const height = entry.borderBoxSize[0]?.blockSize ?? entry.contentRect.height;
        return id && height > 0 ? [{ id, height }] : [];
      });
      if (!changes.length) return;
      const changed = changes.filter(row => Math.abs((measured.current.get(row.id) ?? 128) - row.height) >= 1);
      if (!changed.length) return;
      const offset = root.current?.scrollTop ?? 0;
      const delta = changed.reduce((sum, row) => {
        const previous = positions.current.entries.find(value => value.item.id === row.id);
        return previous && previous.start + previous.size <= offset ? sum + row.height - previous.size : sum;
      }, 0);
      if (delta) pendingAnchor.current = offset + delta;
      const next = new Map(measured.current); for (const row of changed) next.set(row.id, row.height);
      measured.current = next;
      if (measuredWidth.current !== undefined) heightMemory?.set(memoryKey, { width: measuredWidth.current, rows: next });
      setHeights(next);
    });
    for (const i of indices) { const node = rows.current.get(items[i].id); if (node) observer.observe(node); }
    return () => observer.disconnect();
  }, [horizontal, indices, items, heightMemory, memoryKey]);

  useLayoutEffect(() => {
    const maximum = Math.max(0, layout.total + (horizontal && end ? 100 + gap : 0) - viewport.size);
    if (viewport.offset > maximum && root.current) {
      if (horizontal) root.current.scrollLeft = maximum; else root.current.scrollTop = maximum;
      memory.set(memoryKey, maximum); setViewport(value => ({ ...value, offset: maximum }));
    }
    if (pendingAnchor.current !== undefined && root.current) {
      const offset = pendingAnchor.current; pendingAnchor.current = undefined;
      root.current.scrollTop = offset; memory.set(memoryKey, offset); setViewport(value => ({ ...value, offset }));
    }
    const request = pendingFocus.current; if (!request) return;
    const row = rows.current.get(request.id); if (!row) return;
    const targets = focusable(row); const target = request.reverse ? targets.at(-1) : targets[0];
    if (target) { pendingFocus.current = undefined; target.focus({ preventScroll: true }); }
  }, [indices, layout, memory, memoryKey, horizontal, end, gap, viewport.offset, viewport.size]);

  function keyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.defaultPrevented || event.key !== 'Tab' || event.altKey || event.ctrlKey || event.metaKey || active) return;
    const target = event.target as HTMLElement;
    const row = target.closest<HTMLElement>(`[data-board-window-axis="${axis}"]`);
    const id = row?.dataset.boardWindowId; if (!row || !id) return;
    const controls = focusable(row);
    if (target !== (event.shiftKey ? controls[0] : controls.at(-1))) return;
    const index = items.findIndex(item => item.id === id) + (event.shiftKey ? -1 : 1);
    if (index < 0 || index >= items.length || !root.current) return;
    event.preventDefault(); const destination = layout.entries[index];
    pendingFocus.current = { id: destination.item.id, reverse: event.shiftKey };
    setFocused(destination.item.id);
    const offset = destination.start;
    if (horizontal) root.current.scrollLeft = offset; else root.current.scrollTop = offset;
    memory.set(memoryKey, offset); setViewport(value => ({ ...value, offset }));
  }

  return <Box ref={root} role="group" aria-label={horizontal ? 'Kanban board' : 'Cards'} data-kanban-scroll
    onKeyDown={keyDown} onScroll={event => {
      const offset = horizontal ? event.currentTarget.scrollLeft : event.currentTarget.scrollTop;
      memory.set(memoryKey, offset); setViewport(value => value.offset === offset ? value : { ...value, offset });
    }} sx={horizontal ? { overflowX: 'auto', overflowAnchor: 'none', pb: 2 } : { overflowY: 'auto', overflowAnchor: 'none', height: 'min(50vh, 480px)', mt: 2 }}>
    <Box sx={{ position: 'relative', ...(horizontal
      ? { width: layout.total + (end ? 100 + gap : 0), height: '70vh', minHeight: 240 }
      : { height: layout.total }) }}>
      {indices.map(i => { const row = layout.entries[i]; return <Box key={row.item.id}
        data-board-window-axis={axis} data-board-window-id={row.item.id}
        ref={(node: HTMLDivElement | null) => { if (node) rows.current.set(row.item.id, node); else rows.current.delete(row.item.id); }}
        onFocus={event => {
          setFocused(row.item.id);
          if (root.current && (row.start < viewport.offset || row.start + row.size > viewport.offset + viewport.size)) {
            const offset = row.start;
            if (horizontal) root.current.scrollLeft = offset; else root.current.scrollTop = offset;
            memory.set(memoryKey, offset); setViewport(value => ({ ...value, offset }));
          }
          // A long List can also scroll around its header and Card viewport.
          // Reveal focus through every ancestor, including after Dialog exit.
          const target = event.target as HTMLElement;
          target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
        }}
        sx={{ position: 'absolute', ...(horizontal ? { left: row.start, top: 0, width: columnSize } : { top: row.start, left: 0, right: 0 }) }}>
        {renderItem(row.item)}
      </Box>; })}
      {end && <Box sx={{ position: 'absolute', top: 0, left: layout.total + gap }}>{end}</Box>}
    </Box>
  </Box>;
}
