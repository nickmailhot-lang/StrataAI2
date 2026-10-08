import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { App } from './app/App';
import { viewFailureRootOptions } from './app/ViewFailure';
import { installRuntimeExceptionObservers } from './app/runtimeExceptions';

const rootElement = document.getElementById('root');

if (!rootElement) {
  throw new Error('StrataAI2 root element was not found.');
}

installRuntimeExceptionObservers();
createRoot(rootElement, viewFailureRootOptions()).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
