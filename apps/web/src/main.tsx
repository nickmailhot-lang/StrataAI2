import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { App } from './app/App';
import { viewFailureRootOptions } from './app/ViewFailure';

const rootElement = document.getElementById('root');

if (!rootElement) {
  throw new Error('StrataAI2 root element was not found.');
}

createRoot(rootElement, viewFailureRootOptions()).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
