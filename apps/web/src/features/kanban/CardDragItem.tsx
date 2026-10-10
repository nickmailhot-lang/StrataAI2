import { useCallback, type ReactNode } from 'react';
import { Box, Button } from '@mui/material';
import { useDraggable, useDroppable } from '@dnd-kit/core';
import { useDragHandleFocus } from './useDragHandleFocus';

export function CardDragItem({ id, title, disabled, available, children }: { id: string; title: string; disabled: boolean; available: boolean; children: ReactNode }) {
  const drag = useDraggable({ id: `card:${id}`, disabled: disabled || !available });
  const drop = useDroppable({ id: `card:${id}`, disabled: disabled || !available });
  const focus = useDragHandleFocus(id, disabled, available);
  const { setActivatorNodeRef } = drag;
  const handleRef = useCallback((node: HTMLButtonElement | null) => {
    focus.handle.current = node; setActivatorNodeRef(node);
  }, [focus.handle, setActivatorNodeRef]);
  const { setNodeRef: setDragNode } = drag, { setNodeRef: setDropNode } = drop;
  const registerNode = useCallback((node: HTMLElement | null) => { setDragNode(node); setDropNode(node); }, [setDragNode, setDropNode]);
  return <Box data-card-drag-id={id} ref={registerNode}
    sx={{ position: 'relative', zIndex: drag.isDragging ? 3 : 'auto', outline: drop.isOver && !drag.isDragging ? '2px solid' : undefined,
      outlineColor: 'primary.main', transform: drag.transform ? `translate3d(${drag.transform.x}px,${drag.transform.y}px,0)` : undefined }}>
    {available && <Button ref={handleRef} {...drag.attributes} {...drag.listeners} onFocus={focus.onFocus} disabled={disabled} sx={{ touchAction: 'none' }}>Drag {title} card</Button>}
    {children}
  </Box>;
}
export function CardListEndTarget({ id, name, disabled }: { id: string; name: string; disabled: boolean }) {
  const drop = useDroppable({ id: `card-end:${id}`, disabled });
  return <Box data-card-list-end={id} ref={drop.setNodeRef} sx={{ minHeight: 64, border: '2px dashed', borderColor: drop.isOver ? 'primary.main' : 'grey.300', p: 1, overflowWrap: 'anywhere' }}>
    Drop card at end of {name}
  </Box>;
}
