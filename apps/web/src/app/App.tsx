import { useMemo } from 'react';
import { CssBaseline, ThemeProvider } from '@mui/material';
import {
  Navigate,
  RouterProvider,
  createBrowserRouter,
} from 'react-router-dom';

import { InternalAppShell } from './InternalAppShell';
import { AuthPage } from '../features/auth/AuthPage';
import { ProfilePage } from '../features/auth/ProfilePage';
import { PasswordRecoveryPage } from '../features/auth/PasswordRecoveryPage';
import { ResetPasswordPage } from '../features/auth/ResetPasswordPage';
import { BoardScreen } from '../features/kanban/BoardScreen';
import { PortalShell } from '../portal/PortalShell';
import { appTheme } from '../theme/appTheme';

const routes = [
  { path: '/forgot-password', element: <PasswordRecoveryPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  {
    path: '/',
    element: <Navigate to="/login" replace />,
  },
  {
    path: '/login',
    element: <AuthPage />,
  },
  {
    path: '/app/:organizationId',
    element: <InternalAppShell />,
    children: [
      {
        path: 'profile',
        element: <ProfilePage />,
      },
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
