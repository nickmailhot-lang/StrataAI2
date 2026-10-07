import { useCallback, useLayoutEffect, useRef, type ReactNode } from 'react';
import { Box, Button } from '@mui/material';
import { useDraggable, useDroppable } from '@dnd-kit/core';
export type ListDropRequest = { listId: string; name: string; version: number; before: string; nonce: string };
export function ListDragColumn({ id, name, disabled, available, children, scrollMemory }: { id: string; name: string; disabled: boolean; available: boolean; children: ReactNode; scrollMemory?: Map<string, number> }) {
  const drag = useDraggable({ id, disabled: disabled || !available }); const drop = useDroppable({ id, disabled: disabled || !available });
  const element = useRef<HTMLElement | null>(null);
  const { setNodeRef: setDragNode } = drag, { setNodeRef: setDropNode } = drop;
  const registerNode = useCallback((node: HTMLElement | null) => {
    element.current = node; setDragNode(node); setDropNode(node);
  }, [setDragNode, setDropNode]);
  useLayoutEffect(() => { if (element.current) element.current.scrollTop = scrollMemory?.get(`section:${id}`) ?? 0; }, [id, scrollMemory]);
  return <Box component="section" data-kanban-scroll data-kanban-scroll-axis="vertical" aria-labelledby={`list-name-${id}`} ref={registerNode}
    onScroll={event => { if (event.target === event.currentTarget) scrollMemory?.set(`section:${id}`, event.currentTarget.scrollTop); }}
    sx={{ bgcolor: 'grey.100', borderRadius: 2, p: 2, minHeight: 240, maxHeight: '70vh', overflowY: 'auto', position: 'relative', zIndex: drag.isDragging ? 2 : 'auto',
      outline: drop.isOver && !drag.isDragging ? '2px solid' : undefined, outlineColor: 'primary.main',
      transform: drag.transform ? `translate3d(${drag.transform.x}px,${drag.transform.y}px,0)` : undefined }}>
    {available && <Button ref={drag.setActivatorNodeRef} {...drag.attributes} {...drag.listeners} disabled={disabled} sx={{ touchAction: 'none' }}>Drag {name} list</Button>}
    {children}
  </Box>;
}
export function ListEndTarget({ disabled }: { disabled: boolean }) {
  const drop = useDroppable({ id: 'list-end', disabled });
  return <Box ref={drop.setNodeRef} sx={{ minWidth: 100, border: '2px dashed', borderColor: drop.isOver ? 'primary.main' : 'grey.300', p: 2 }}>
    Drop list at end
  </Box>;
}
