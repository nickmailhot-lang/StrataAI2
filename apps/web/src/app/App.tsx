import { useMemo } from "react";
import { CssBaseline, ThemeProvider } from "@mui/material";
import {
  Navigate,
  RouterProvider,
  createBrowserRouter,
} from "react-router-dom";

import { InternalAppShell } from "./InternalAppShell";
import { AuthPage } from "../features/auth/AuthPage";
import { ProfilePage } from "../features/auth/ProfilePage";
import { PasswordRecoveryPage } from "../features/auth/PasswordRecoveryPage";
import { ResetPasswordPage } from "../features/auth/ResetPasswordPage";
import { VerifyEmailPage } from "../features/auth/VerifyEmailPage";
import { BoardScreen } from "../features/kanban/BoardScreen";
import { BoardVisibilityPage } from "../features/kanban/BoardVisibilityPage";
import { BoardMembersPage } from "../features/kanban/BoardMembersPage";
import { ArchivedListsPage } from "../features/kanban/ArchivedListsPage";
import { OrganizationHome } from "../features/organizations/OrganizationHome";
import { OrganizationSettingsPage } from "../features/organizations/OrganizationSettingsPage";
import { OrganizationMembersPage } from "../features/organizations/OrganizationMembersPage";
import { OrganizationInvitationPage, BoardInvitationPage } from "../features/organizations/OrganizationInvitationPage";
import { OrganizationInvitationHistoryPage, BoardInvitationHistoryPage } from "../features/organizations/OrganizationInvitationHistoryPage";
import { InvitationsPage } from "../features/auth/InvitationsPage";
import { InvitationLinkPage } from "../features/auth/InvitationLinkPage";
import { PortalShell } from "../portal/PortalShell";
import { appTheme } from "../theme/appTheme";
import { BuildIdentityFooter } from './BuildIdentityFooter';

const routes = [
  { path: "/app", element: <OrganizationHome /> },
  { path: "/app/profile", element: <ProfilePage /> },
  { path: "/app/invitations", element: <InvitationsPage /> },
  { path: "/invitation", element: <InvitationLinkPage /> },
  { path: "/verify-email", element: <VerifyEmailPage /> },
  { path: "/forgot-password", element: <PasswordRecoveryPage /> },
  { path: "/reset-password", element: <ResetPasswordPage /> },
  {
    path: "/",
    element: <Navigate to="/login" replace />,
  },
  {
    path: "/login",
    element: <AuthPage />,
  },
  {
    path: "/app/:organizationId",
    element: <InternalAppShell />,
    children: [
      { index: true, element: <OrganizationHome /> },
      { path: "settings", element: <OrganizationSettingsPage /> },
      { path: "members", element: <OrganizationMembersPage /> },
      { path: "invite", element: <OrganizationInvitationPage /> },
      { path: "boards/:boardId/invite", element: <BoardInvitationPage /> },
      { path: "boards/:boardId/visibility", element: <BoardVisibilityPage /> },
      { path: "boards/:boardId/members", element: <BoardMembersPage /> },
      { path: "boards/:boardId/archived-lists", element: <ArchivedListsPage /> },
      { path: "boards/:boardId/invitations", element: <BoardInvitationHistoryPage /> },
      { path: "invitations", element: <OrganizationInvitationHistoryPage /> },
      {
        path: "profile",
        element: <ProfilePage />,
      },
      {
        path: "boards/:boardId",
        element: <BoardScreen />,
      },
      {
        path: "boards/:boardId/cards/:cardId",
        element: <BoardScreen />,
      },
    ],
  },
  {
    path: "/portal/:organizationId",
    element: <PortalShell />,
  },
];

export function App() {
  const router = useMemo(() => createBrowserRouter(routes), []);

  return (
    <ThemeProvider theme={appTheme}>
      <CssBaseline />
      <RouterProvider router={router} />
      <BuildIdentityFooter />
    </ThemeProvider>
  );
}
