import AddIcon from '@mui/icons-material/Add';
import {
  Box,
  Button,
  Card as MuiCard,
  CardContent,
  Chip,
  Drawer,
  Stack,
  Typography,
} from '@mui/material';
import { Link, useParams } from 'react-router-dom';

const columns = [
  {
    id: 'new',
    title: 'New',
    cards: [
      { id: 'roof-inspection', title: 'Review roof inspection request', priority: 'Normal' },
      { id: 'unit-49-water', title: 'Unit 49 water ingress follow-up', priority: 'High' },
    ],
  },
  {
    id: 'in-progress',
    title: 'In Progress',
    cards: [
      { id: 'fence-project', title: 'Privacy fence project', priority: 'High' },
    ],
  },
  {
    id: 'done',
    title: 'Done',
    cards: [
      { id: 'driveway-light', title: 'Replace driveway light bulb', priority: 'Normal' },
    ],
  },
];

export function BoardScreen() {
  const { organizationId, boardId, cardId } = useParams();

  return (
    <>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        alignItems={{ sm: 'center' }}
        justifyContent="space-between"
        sx={{ mb: 3 }}
      >
        <Box>
          <Typography variant="h4" component="h2">
            Council Operations
          </Typography>
          <Typography color="text.secondary">
            Board {boardId ?? 'Unknown'} · drag-and-drop behavior is introduced under PRD-06.
          </Typography>
        </Box>
        <Button variant="contained" startIcon={<AddIcon />}>
          Add card
        </Button>
      </Stack>

      <Box
        aria-label="Kanban board"
        sx={{
          display: 'grid',
          gridAutoFlow: 'column',
          gridAutoColumns: { xs: '82vw', sm: 320 },
          gap: 2,
          overflowX: 'auto',
          pb: 2,
        }}
      >
        {columns.map((column) => (
          <Box
            key={column.id}
            sx={{
              bgcolor: 'grey.100',
              borderRadius: 2,
              p: 1.5,
              minHeight: 360,
            }}
          >
            <Typography variant="subtitle1" fontWeight={700} sx={{ mb: 1.5 }}>
              {column.title}
            </Typography>
            <Stack spacing={1.25}>
              {column.cards.map((card) => (
                <MuiCard
                  key={card.id}
                  component={Link}
                  to={`/app/${organizationId}/boards/${boardId}/cards/${card.id}`}
                  sx={{
                    textDecoration: 'none',
                    color: 'inherit',
                    '&:focus-visible': {
                      outline: '3px solid',
                      outlineColor: 'primary.main',
                      outlineOffset: 2,
                    },
                  }}
                >
                  <CardContent>
                    <Typography fontWeight={600}>{card.title}</Typography>
                    <Chip
                      size="small"
                      label={card.priority}
                      sx={{ mt: 1.5 }}
                      color={card.priority === 'High' ? 'warning' : 'default'}
                    />
                  </CardContent>
                </MuiCard>
              ))}
            </Stack>
          </Box>
        ))}
      </Box>

      <Drawer
        anchor="right"
        open={Boolean(cardId)}
        component="aside"
        PaperProps={{ sx: { width: { xs: '100%', sm: 440 }, p: 3 } }}
      >
        <Typography variant="h5" component="h2" sx={{ mt: 2 }}>
          Card details
        </Typography>
        <Typography color="text.secondary" sx={{ mt: 1 }}>
          Stable card deep link: {cardId ?? 'None'}
        </Typography>
        <Button
          component={Link}
          to={`/app/${organizationId}/boards/${boardId}`}
          sx={{ mt: 3, alignSelf: 'flex-start' }}
        >
          Close
        </Button>
      </Drawer>
    </>
  );
}
