import { useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';

type UserProfile = {
  id: string;
  email: string;
  displayName: string;
  locale: string;
  timezone: string;
  status: string;
  emailVerified: boolean;
};

export function ProfilePage() {
  const [profile, setProfile] = useState<UserProfile>();
  const [error, setError] = useState<string>();
  const navigate = useNavigate();

  useEffect(() => {
    let active = true;

    void fetch('/me', { credentials: 'include' })
      .then(async (response) => {
        if (response.status === 401) {
          navigate('/login', { replace: true });
          return;
        }

        if (!response.ok) {
          throw new Error('Unable to load profile.');
        }

        const user = (await response.json()) as UserProfile;
        if (active) {
          setProfile(user);
        }
      })
      .catch(() => {
        if (active) {
          setError('Unable to load your profile.');
        }
      });

    return () => {
      active = false;
    };
  }, [navigate]);

  async function logout() {
    await fetch('/auth/logout', {
      method: 'POST',
      credentials: 'include',
    });
    navigate('/login', { replace: true });
  }

  if (error) {
    return <Alert severity="error">{error}</Alert>;
  }

  if (!profile) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 240 }}>
        <CircularProgress aria-label="Loading profile" />
      </Box>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, maxWidth: 720 }}>
      <Stack spacing={2}>
        <Typography variant="h4" component="h2">
          {profile.displayName}
        </Typography>
        <Typography>{profile.email}</Typography>
        <Typography color="text.secondary">
          {profile.locale} · {profile.timezone}
        </Typography>
        <Typography color="text.secondary">
          Account: {profile.status}
          {profile.emailVerified ? ' · email verified' : ''}
        </Typography>
        <Button onClick={logout} variant="outlined" sx={{ alignSelf: 'flex-start' }}>
          Sign out
        </Button>
      </Stack>
    </Paper>
  );
}
