import DashboardOutlinedIcon from "@mui/icons-material/DashboardOutlined";
import DescriptionOutlinedIcon from "@mui/icons-material/DescriptionOutlined";
import EmailOutlinedIcon from "@mui/icons-material/EmailOutlined";
import GroupsOutlinedIcon from "@mui/icons-material/GroupsOutlined";
import MeetingRoomOutlinedIcon from "@mui/icons-material/MeetingRoomOutlined";
import SettingsOutlinedIcon from "@mui/icons-material/SettingsOutlined";
import ViewKanbanOutlinedIcon from "@mui/icons-material/ViewKanbanOutlined";
import {
  AppBar,
  Box,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import type { ReactNode } from "react";
import { Link, Outlet, useLocation, useParams } from "react-router-dom";
import { SurfaceAdmission } from './SurfaceAdmission';

const drawerWidth = 248;

type NavigationItem = {
  label: string;
  icon: ReactNode;
  destination?: "home" | "organization";
};

const navigation: NavigationItem[] = [
  {
    label: "Organizations",
    icon: <DashboardOutlinedIcon />,
    destination: "home",
  },
  {
    label: "Boards",
    icon: <ViewKanbanOutlinedIcon />,
    destination: "organization",
  },
  { label: "Meetings", icon: <MeetingRoomOutlinedIcon /> },
  { label: "Correspondence", icon: <EmailOutlinedIcon /> },
  { label: "Documents", icon: <DescriptionOutlinedIcon /> },
  { label: "Owners & Units", icon: <GroupsOutlinedIcon /> },
];

export function InternalAppShell() {
  const { pathname } = useLocation();
  const boardView = /^\/app\/[^/]+\/boards\/[^/]+(?:\/cards\/[^/]+)?\/?$/.test(pathname);
  return <SurfaceAdmission surface="INTERNAL" deniedContent={boardView ? <Box component="main" sx={{ p: 2 }}><Outlet /></Box> : undefined}>
    <InternalLayout />
  </SurfaceAdmission>;
}

function InternalLayout() {
  const { organizationId } = useParams();

  return (
    <Box sx={{ display: "flex", minHeight: "100vh" }}>
      <AppBar
        position="fixed"
        elevation={0}
        sx={{ zIndex: (theme) => theme.zIndex.drawer + 1 }}
      >
        <Toolbar>
          <Typography variant="h6" component="h1" sx={{ flexGrow: 1 }}>
            StrataAI2
          </Typography>
          <Typography
            variant="body2"
            sx={{ mr: 1, display: { xs: "none", sm: "block" } }}
          >
            Organization: {organizationId ?? "Unknown"}
          </Typography>
          <Tooltip title="Organizations">
            <IconButton
              component={Link}
              to="/app"
              color="inherit"
              aria-label="Open organizations"
              sx={{ display: { xs: "inline-flex", sm: "none" } }}
            >
              <GroupsOutlinedIcon />
            </IconButton>
          </Tooltip>
          <Tooltip title="Boards">
            <IconButton
              component={Link}
              to={`/app/${organizationId}`}
              color="inherit"
              aria-label="Open boards"
              sx={{ display: { xs: "inline-flex", sm: "none" } }}
            >
              <ViewKanbanOutlinedIcon />
            </IconButton>
          </Tooltip>
          <Tooltip title="Profile">
            <IconButton
              component={Link}
              to={`/app/${organizationId}/profile`}
              color="inherit"
              aria-label="Open profile"
            >
              <SettingsOutlinedIcon />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        sx={{
          display: { xs: "none", sm: "block" },
          width: { xs: 0, sm: drawerWidth },
          flexShrink: 0,
          "& .MuiDrawer-paper": {
            width: drawerWidth,
            boxSizing: "border-box",
          },
        }}
      >
        <Toolbar />
        <List aria-label="Internal application navigation">
          {navigation.map((item) => (
            <ListItemButton
              key={item.label}
              component={Link}
              to={
                item.destination === "home" ? "/app" : `/app/${organizationId}`
              }
              disabled={!item.destination}
            >
              <ListItemIcon>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} />
            </ListItemButton>
          ))}
        </List>
        <Divider />
      </Drawer>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          bgcolor: "background.default",
          p: { xs: 2, md: 3 },
        }}
      >
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  );
}
