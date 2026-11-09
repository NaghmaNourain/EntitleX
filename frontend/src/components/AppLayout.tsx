import DashboardIcon from '@mui/icons-material/Dashboard'
import FactCheckIcon from '@mui/icons-material/FactCheck'
import GavelIcon from '@mui/icons-material/Gavel'
import HistoryIcon from '@mui/icons-material/History'
import LogoutIcon from '@mui/icons-material/Logout'
import MenuIcon from '@mui/icons-material/Menu'
import MonitorHeartIcon from '@mui/icons-material/MonitorHeart'
import PlaylistAddCheckIcon from '@mui/icons-material/PlaylistAddCheck'
import ReplayIcon from '@mui/icons-material/Replay'
import SearchIcon from '@mui/icons-material/Search'
import TimelineIcon from '@mui/icons-material/Timeline'
import AppBar from '@mui/material/AppBar'
import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import Drawer from '@mui/material/Drawer'
import IconButton from '@mui/material/IconButton'
import List from '@mui/material/List'
import ListItemButton from '@mui/material/ListItemButton'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListItemText from '@mui/material/ListItemText'
import Toolbar from '@mui/material/Toolbar'
import Typography from '@mui/material/Typography'
import { useState, type ReactNode } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const DRAWER_WIDTH = 280

interface NavItem {
  to: string
  label: string
  icon: ReactNode
  roles?: Array<'RenewalRepresentative' | 'RenewalManager' | 'DealDeskManager' | 'PlatformAdmin'>
}

const NAV: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: <DashboardIcon /> },
  { to: '/entitlements', label: 'Entitlement search', icon: <SearchIcon /> },
  { to: '/extensions/new', label: 'Create extension', icon: <PlaylistAddCheckIcon /> },
  { to: '/approvals/renewal-manager', label: 'Renewal Manager queue', icon: <GavelIcon />, roles: ['RenewalManager', 'PlatformAdmin'] },
  { to: '/approvals/deal-desk', label: 'Deal Desk queue', icon: <FactCheckIcon />, roles: ['DealDeskManager', 'PlatformAdmin'] },
  { to: '/fulfillment', label: 'Fulfillment progress', icon: <TimelineIcon /> },
  { to: '/retry', label: 'Retry failed', icon: <ReplayIcon />, roles: ['RenewalRepresentative', 'PlatformAdmin'] },
  { to: '/lineage', label: 'Request lineage', icon: <HistoryIcon /> },
  { to: '/audit', label: 'Audit history', icon: <HistoryIcon /> },
  { to: '/ops', label: 'Ops monitoring', icon: <MonitorHeartIcon />, roles: ['PlatformAdmin'] },
]

export function AppLayout() {
  const { user, logout, hasRole } = useAuth()
  const navigate = useNavigate()
  const [mobileOpen, setMobileOpen] = useState(false)

  const items = NAV.filter((item) => !item.roles || hasRole(...item.roles))

  const drawer = (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }} component="nav" aria-label="Primary">
      <Toolbar sx={{ px: 2 }}>
        <Typography variant="h6" component="div" sx={{ fontWeight: 700, color: 'primary.main' }}>
          EntitleX
        </Typography>
      </Toolbar>
      <Divider />
      <List sx={{ flex: 1, py: 1 }}>
        {items.map((item) => (
          <ListItemButton
            key={item.to}
            component={NavLink}
            to={item.to}
            end={item.to === '/'}
            onClick={() => setMobileOpen(false)}
            sx={{
              mx: 1,
              borderRadius: 2,
              '&.active': {
                bgcolor: 'rgba(15, 110, 86, 0.12)',
                color: 'primary.dark',
                '& .MuiListItemIcon-root': { color: 'primary.main' },
              },
            }}
          >
            <ListItemIcon sx={{ minWidth: 40 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} />
          </ListItemButton>
        ))}
      </List>
      <Divider />
      <Box sx={{ p: 2 }}>
        <Typography variant="body2" fontWeight={600}>
          {user?.displayName}
        </Typography>
        <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
          {user?.role}
        </Typography>
        <IconButton
          aria-label="Sign out"
          onClick={() => {
            void (async () => {
              await logout()
              navigate('/login')
            })()
          }}
          size="small"
        >
          <LogoutIcon fontSize="small" />
        </IconButton>
      </Box>
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar>
          <IconButton
            color="inherit"
            edge="start"
            aria-label="Open navigation"
            onClick={() => setMobileOpen(true)}
            sx={{ mr: 1, display: { md: 'none' } }}
          >
            <MenuIcon />
          </IconButton>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1, fontWeight: 700 }}>
            EntitleX control plane
          </Typography>
          <Typography variant="body2" sx={{ opacity: 0.85 }}>
            {user?.email}
          </Typography>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }} aria-label="Sidebar">
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          ModalProps={{ keepMounted: true }}
          sx={{
            display: { xs: 'block', md: 'none' },
            '& .MuiDrawer-paper': { width: DRAWER_WIDTH },
          }}
        >
          {drawer}
        </Drawer>
        <Drawer
          variant="permanent"
          open
          sx={{
            display: { xs: 'none', md: 'block' },
            '& .MuiDrawer-paper': {
              width: DRAWER_WIDTH,
              boxSizing: 'border-box',
              borderRight: '1px solid',
              borderColor: 'divider',
              backgroundColor: 'rgba(255,255,255,0.92)',
              backdropFilter: 'blur(8px)',
            },
          }}
        >
          {drawer}
        </Drawer>
      </Box>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          width: { md: `calc(100% - ${DRAWER_WIDTH}px)` },
          p: { xs: 2, md: 3 },
        }}
      >
        <Toolbar />
        <Outlet />
      </Box>
    </Box>
  )
}
