import { Box, Typography } from '@mui/material';
import { createBuildIdentity } from './buildIdentity';

// These values are compiled into the SPA by Vite, like build-metadata.json.
// Runtime configuration and browser storage cannot relabel this build.
const identity = createBuildIdentity(import.meta.env.VITE_STRATAAI_BUILD_REVISION, import.meta.env.VITE_STRATAAI_BUILD_VERSION);

export function BuildIdentityFooter() {
  return <Box component="footer" aria-label="Application version" sx={{ px: 2, py: 1, color: 'text.secondary', overflowWrap: 'anywhere' }}>
    <Typography variant="caption">Version {identity.version} · Build {identity.revision}</Typography>
  </Box>;
}
