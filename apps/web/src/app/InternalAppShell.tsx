import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import MailOutlineIcon from '@mui/icons-material/MailOutline';
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
import { Outlet, useParams } from 'react-router-dom';

const drawerWidth = 248;

const navigation = [
  ['Dashboard', <DashboardOutlinedIcon key="dashboard" />],
  ['Board', <ViewKanbanOutlinedIcon key="board" />],
  ['Meetings', <MeetingRoomOutlinedIcon key="meetings" />],
  ['Correspondence', <MailOutlineIcon key="mail" />],
  ['Documents', <DescriptionOutlinedIcon key="documents" />],
  ['Owners & Units', <GroupsOutlinedIcon key="owners" />],
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
          {navigation.map(([label, icon]) => (
            <ListItemButton key={label as string}>
              <ListItemIcon>{icon}</ListItemIcon>
              <ListItemText primary={label as string} />
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
