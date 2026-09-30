import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import EmailOutlinedIcon from '@mui/icons-material/EmailOutlined';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import MeetingRoomOutlinedIcon from '@mui/icons-material/MeetingRoomOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import ViewKanbanOutlinedIcon from '@mui/icons-material/ViewKanbanOutlined';
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
} from '@mui/material';
import type { ReactNode } from 'react';
import { Outlet, useParams } from 'react-router-dom';

const drawerWidth = 248;

type NavigationItem = {
  label: string;
  icon: ReactNode;
};

const navigation: NavigationItem[] = [
  { label: 'Dashboard', icon: <DashboardOutlinedIcon /> },
  { label: 'Board', icon: <ViewKanbanOutlinedIcon /> },
  { label: 'Meetings', icon: <MeetingRoomOutlinedIcon /> },
  { label: 'Correspondence', icon: <EmailOutlinedIcon /> },
  { label: 'Documents', icon: <DescriptionOutlinedIcon /> },
  { label: 'Owners & Units', icon: <GroupsOutlinedIcon /> },
];

export function InternalAppShell() {
  const { organizationId } = useParams();

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar
        position="fixed"
        elevation={0}
        sx={{ zIndex: (theme) => theme.zIndex.drawer + 1 }}
      >
        <Toolbar>
          <Typography variant="h6" component="h1" sx={{ flexGrow: 1 }}>
            StrataAI2
          </Typography>
          <Typography variant="body2" sx={{ mr: 1 }}>
            Organization: {organizationId ?? 'Unknown'}
          </Typography>
          <Tooltip title="Settings">
            <IconButton color="inherit" aria-label="Open settings">
              <SettingsOutlinedIcon />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        sx={{
          width: drawerWidth,
          flexShrink: 0,
          '& .MuiDrawer-paper': {
            width: drawerWidth,
            boxSizing: 'border-box',
          },
        }}
      >
        <Toolbar />
        <List aria-label="Internal application navigation">
          {navigation.map((item) => (
            <ListItemButton key={item.label}>
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
          bgcolor: 'background.default',
          p: { xs: 2, md: 3 },
        }}
      >
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  );
}
