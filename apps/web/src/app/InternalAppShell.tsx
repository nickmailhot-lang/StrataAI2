import MenuOutlinedIcon from "@mui/icons-material/MenuOutlined";
import CloseOutlinedIcon from "@mui/icons-material/CloseOutlined";
import DashboardOutlinedIcon from "@mui/icons-material/DashboardOutlined";
import { DescriptionParserWarmup } from '../features/kanban/CardDescriptionPreview';
import DescriptionOutlinedIcon from "@mui/icons-material/DescriptionOutlined";
import EmailOutlinedIcon from "@mui/icons-material/EmailOutlined";
import GroupsOutlinedIcon from "@mui/icons-material/GroupsOutlined";
import MeetingRoomOutlinedIcon from "@mui/icons-material/MeetingRoomOutlined";
import SettingsOutlinedIcon from "@mui/icons-material/SettingsOutlined";
import ViewKanbanOutlinedIcon from "@mui/icons-material/ViewKanbanOutlined";
import NotificationsOutlinedIcon from "@mui/icons-material/NotificationsOutlined";
import SearchOutlinedIcon from "@mui/icons-material/SearchOutlined";
import {
  AppBar,
  Box,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from "@mui/material";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link, Outlet, useLocation, useParams } from "react-router-dom";
import { SurfaceAdmission } from './SurfaceAdmission';
import { NavigationConfirmation } from './NavigationConfirmation';
import { internalNavigation } from './navigationModel';
import { designTokens } from '../theme/designTokens';

const drawerWidth = designTokens.navigationWidth;
const navigationIcons = {
  organizations: <DashboardOutlinedIcon />, boards: <ViewKanbanOutlinedIcon />,
  meetings: <MeetingRoomOutlinedIcon />, notifications: <NotificationsOutlinedIcon />,
  correspondence: <EmailOutlinedIcon />, documents: <DescriptionOutlinedIcon />, units: <GroupsOutlinedIcon />,
};

export function InternalAppShell() {
  const { pathname } = useLocation();
  const boardView = /^\/app\/[^/]+\/boards\/[^/]+(?:\/cards\/[^/]+)?\/?$/.test(pathname);
  // The Organization home owns a separate, protected lifecycle admission.
  // Its terminal source can remain readable after ordinary graph access ends.
  const organizationHome = /^\/app\/[^/]+\/?$/.test(pathname);
  return <SurfaceAdmission surface="INTERNAL" deniedContent={boardView || organizationHome ? <Box component="main" sx={{ p: 2 }}><Outlet /></Box> : undefined}>
    <InternalLayout />
  </SurfaceAdmission>;
}

function InternalLayout() {
  const { organizationId } = useParams();
  const { pathname } = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);
  const appBar = useRef<HTMLElement | null>(null);
  const [headerHeight, setHeaderHeight] = useState<number>();
  useLayoutEffect(() => {
    const header = appBar.current;
    if (!header) return;
    const measure = () => {
      const height = Math.ceil(header.getBoundingClientRect().height);
      if (height > 0) setHeaderHeight(height);
    };
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(header);
    return () => observer.disconnect();
  }, []);
  const headerOffset = <Box aria-hidden="true" sx={{ height: headerHeight ?? { xs: 56, sm: 64 }, flexShrink: 0 }} />;
  const mobile = useMediaQuery(useTheme().breakpoints.down("sm"));
  const closeNavigation = () => setMobileOpen(false);
  useEffect(() => { setMobileOpen(false); }, [mobile, pathname]);
  const navigationItems = <List aria-label="Internal application navigation">
    {internalNavigation('INTERNAL', organizationId ?? '', pathname).map(item => {
      const unavailable = 'unavailable' in item;
      return <ListItem key={item.id} disablePadding>
        {unavailable ? <ListItemButton component="div" disabled>
          <ListItemIcon>{navigationIcons[item.id as keyof typeof navigationIcons]}</ListItemIcon>
          <ListItemText primary={item.label} secondary="Not available yet" />
        </ListItemButton> : <ListItemButton component={Link} to={item.to}
          selected={item.current} aria-current={item.current ? 'page' : undefined} onClick={closeNavigation}>
          <ListItemIcon>{navigationIcons[item.id as keyof typeof navigationIcons]}</ListItemIcon>
          <ListItemText primary={item.label} />
        </ListItemButton>}
      </ListItem>;
    })}
  </List>;

  return (
    <Box sx={{ display: "flex", minHeight: "100vh" }}>
      <Box component="a" href="#internal-main-content"
        onClick={event => { event.preventDefault(); document.getElementById('internal-main-content')?.focus(); }}
        sx={{ position: 'fixed', top: 0, left: 0, transform: 'translateY(-150%)',
          zIndex: theme => theme.zIndex.modal + 1, bgcolor: 'background.paper', color: 'primary.main', p: 2,
          '&:focus': { transform: 'translateY(0)' } }}>Skip to main content</Box>
      <AppBar
        ref={appBar}
        position="fixed"
        elevation={0}
        sx={{ zIndex: (theme) => theme.zIndex.drawer + 1 }}
      >
        <Toolbar sx={{ gap: 0.5, flexWrap: 'wrap' }}>
          <IconButton color="inherit" aria-label="Open navigation" aria-expanded={mobileOpen && mobile}
            aria-controls={mobileOpen && mobile ? 'internal-mobile-navigation' : undefined}
            onClick={() => setMobileOpen(true)} sx={{ display: { xs: 'inline-flex', sm: 'none' } }}>
            <MenuOutlinedIcon />
          </IconButton>
          <Typography variant="h6" component="h1" sx={{ flexGrow: 1, minWidth: 'max-content', flexShrink: 0, fontSize: { xs: '1.125rem', sm: '1.25rem' } }}>
            StrataAI2
          </Typography>
          <Typography
            variant="body2"
            sx={{ mr: 1, display: { xs: "none", sm: "block" } }}
          >
            Organization: {organizationId ?? "Unknown"}
          </Typography>
          <Tooltip title="Search Cards">
            <IconButton component={Link} to={`/app/${organizationId}/search`} color="inherit" aria-label="Search Cards">
              <SearchOutlinedIcon />
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
          <Tooltip title="Notifications">
            <IconButton component={Link} to={`/app/${organizationId}/notifications`} color="inherit" aria-label="Open notifications">
              <NotificationsOutlinedIcon />
            </IconButton>
          </Tooltip>
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        slotProps={{ paper: { component: "nav", "aria-label": "Internal application navigation" } }}
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
        {headerOffset}
        <DescriptionParserWarmup />
        {navigationItems}
        <Divider />
      </Drawer>

      {mobile && <Drawer variant="temporary" open={mobileOpen} onClose={closeNavigation}
        sx={{ zIndex: theme => theme.zIndex.modal }}
        slotProps={{ paper: { id: 'internal-mobile-navigation',
          'aria-label': 'Application navigation dialog', sx: { width: drawerWidth, maxWidth: '100vw' } } }}>
        <Box component="nav" aria-label="Mobile application navigation">
        <Toolbar>
          <Typography sx={{ flexGrow: 1 }}>Navigation</Typography>
          <IconButton aria-label="Close navigation" onClick={closeNavigation}><CloseOutlinedIcon /></IconButton>
        </Toolbar>
        {navigationItems}
        </Box>
      </Drawer>}

      <Box
        component="main"
        id="internal-main-content"
        tabIndex={-1}
        sx={{
          flexGrow: 1,
          minWidth: 0,
          bgcolor: "background.default",
          p: { xs: 2, md: 3 },
        }}
      >
        {headerOffset}
        {organizationId && <NavigationConfirmation key={`navigation-context-${organizationId}`}
          target={{ kind: 'context', organization: organizationId }} />}
        <Outlet />
      </Box>
    </Box>
  );
}
