import type { BoardSnapshot } from '../../api/workManagement';
export const boardColors = ['blue', 'green', 'red', 'purple', 'orange', 'gray'] as const;
const palette = { blue: ['#eff6ff', '#10243a'], green: ['#ecfdf5', '#102e24'], red: ['#fff1f2', '#381b22'],
  purple: ['#faf5ff', '#291e38'], orange: ['#fff7ed', '#352615'], gray: ['#f3f4f6', '#252930'] } as const;
// Never interpolate persisted CSS, URLs or object references into presentation.
export function boardBackgroundColor(board: BoardSnapshot['board'], mode: 'light' | 'dark'): string | undefined {
  if (board.backgroundType !== 'COLOR' || !boardColors.includes(board.backgroundValue as typeof boardColors[number])) return undefined;
  return palette[board.backgroundValue as keyof typeof palette][mode === 'dark' ? 1 : 0];
}
