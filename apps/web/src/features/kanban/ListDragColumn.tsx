import type { ReactNode } from 'react';
import { Box, Button } from '@mui/material';
import { useDraggable, useDroppable } from '@dnd-kit/core';
export type ListDropRequest = { listId: string; name: string; version: number; before: string; nonce: string };
export function ListDragColumn({ id, name, disabled, available, children }: { id: string; name: string; disabled: boolean; available: boolean; children: ReactNode }) {
  const drag = useDraggable({ id, disabled: disabled || !available }); const drop = useDroppable({ id, disabled: disabled || !available });
  return <Box component="section" data-kanban-scroll aria-labelledby={`list-name-${id}`} ref={node => { drag.setNodeRef(node as HTMLElement | null); drop.setNodeRef(node as HTMLElement | null); }}
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
