import { useMemo } from 'react';
import { CssBaseline, ThemeProvider } from '@mui/material';
import {
  Navigate,
  RouterProvider,
  createBrowserRouter,
} from 'react-router-dom';

import { appTheme } from '../theme/appTheme';
import { BoardScreen } from '../features/kanban/BoardScreen';
import { PortalShell } from '../portal/PortalShell';
import { InternalAppShell } from './InternalAppShell';

const routes = [
  {
    path: '/',
    element: <Navigate to="/app/demo/boards/demo-board" replace />,
  },
  {
    path: '/app/:organizationId',
    element: <InternalAppShell />,
    children: [
      {
        path: 'boards/:boardId',
        element: <BoardScreen />,
      },
      {
        path: 'boards/:boardId/cards/:cardId',
        element: <BoardScreen />,
      },
    ],
  },
  {
    path: '/portal/:organizationId',
    element: <PortalShell />,
  },
];

export function App() {
  const router = useMemo(() => createBrowserRouter(routes), []);

  return (
    <ThemeProvider theme={appTheme}>
      <CssBaseline />
      <RouterProvider router={router} />
    </ThemeProvider>
  );
}
