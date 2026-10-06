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
import { BoardDatePolicyPage } from "../features/kanban/BoardDatePolicyPage";
import { BoardMembersPage } from "../features/kanban/BoardMembersPage";
import { ArchivedListsPage } from "../features/kanban/ArchivedListsPage";
import { ArchivedCardsPage } from "../features/kanban/ArchivedCardsPage";
import { ArchivedBoardsPage } from "../features/kanban/ArchivedBoardsPage";
import { OrganizationHome } from "../features/organizations/OrganizationHome";
import { OrganizationSettingsPage } from "../features/organizations/OrganizationSettingsPage";
import { OrganizationMembersPage } from "../features/organizations/OrganizationMembersPage";
import { OrganizationDeletePage } from "../features/organizations/OrganizationDeletePage";
import { OrganizationLeavePage } from "../features/organizations/OrganizationLeavePage";
import { OrganizationInvitationPage, BoardInvitationPage } from "../features/organizations/OrganizationInvitationPage";
import { OrganizationInvitationHistoryPage, BoardInvitationHistoryPage } from "../features/organizations/OrganizationInvitationHistoryPage";
import { InvitationsPage } from "../features/auth/InvitationsPage";
import { InvitationLinkPage } from "../features/auth/InvitationLinkPage";
import { PortalShell } from "../portal/PortalShell";
import { appTheme } from "../theme/appTheme";
import { BuildIdentityFooter } from './BuildIdentityFooter';
import { NotificationCenterPage } from '../features/notifications/NotificationCenterPage';
import { GlobalSearchPage } from '../features/search/GlobalSearchPage';

const routes = [
  { path: "/app", element: <OrganizationHome /> },
  { path: "/app/profile", element: <ProfilePage /> },
  { path: "/app/invitations", element: <InvitationsPage /> },
  // Deletion withdraws normal surface access. The operation independently
  // authorizes its review/receipt and must retain an unresolved original intent.
  { path: "/app/:organizationId/delete", element: <OrganizationDeletePage /> },
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
      { path: "notifications", element: <NotificationCenterPage /> },
      { path: "search", element: <GlobalSearchPage /> },
      { path: "archived-boards", element: <ArchivedBoardsPage /> },
      { path: "members", element: <OrganizationMembersPage /> },
      { path: "leave", element: <OrganizationLeavePage /> },
      { path: "invite", element: <OrganizationInvitationPage /> },
      { path: "boards/:boardId/invite", element: <BoardInvitationPage /> },
      { path: "boards/:boardId/visibility", element: <BoardVisibilityPage /> },
      { path: "boards/:boardId/date-policy", element: <BoardDatePolicyPage /> },
      { path: "boards/:boardId/members", element: <BoardMembersPage /> },
      { path: "boards/:boardId/archived-lists", element: <ArchivedListsPage /> },
      { path: "boards/:boardId/archived-cards", element: <ArchivedCardsPage /> },
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
