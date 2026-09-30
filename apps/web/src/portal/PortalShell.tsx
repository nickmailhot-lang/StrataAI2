import FolderOutlinedIcon from '@mui/icons-material/FolderOutlined';
import {
  AppBar,
  Box,
  Button,
  Container,
  Paper,
  Stack,
  Toolbar,
  Typography,
} from '@mui/material';
import { useParams } from 'react-router-dom';

export function PortalShell() {
  const { organizationId } = useParams();

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="static" elevation={0}>
        <Toolbar>
          <Typography variant="h6" component="h1" sx={{ flexGrow: 1 }}>
            StrataAI2 Owner Portal
          </Typography>
          <Typography variant="body2">{organizationId}</Typography>
        </Toolbar>
      </AppBar>
      <Container maxWidth="lg" sx={{ py: 4 }}>
        <Stack spacing={3}>
          <Box>
            <Typography variant="h4" component="h2">
              Owner documents
            </Typography>
            <Typography color="text.secondary">
              This is a separate portal authorization surface. Internal Boards and Cards are
              intentionally not exposed here.
            </Typography>
          </Box>
          <Paper variant="outlined" sx={{ p: 3 }}>
            <Stack
              direction="row"
              spacing={2}
              sx={{ alignItems: 'center' }}
            >
              <FolderOutlinedIcon color="primary" />
              <Box sx={{ flexGrow: 1 }}>
                <Typography sx={{ fontWeight: 700 }}>Published documents</Typography>
                <Typography variant="body2" color="text.secondary">
                  Folder/document publication behavior is implemented under PRD-80.
                </Typography>
              </Box>
              <Button variant="outlined">Browse</Button>
            </Stack>
          </Paper>
        </Stack>
      </Container>
    </Box>
  );
}
