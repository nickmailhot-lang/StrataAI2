import js from '@eslint/js';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['dist/**', 'coverage/**'],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: {
      globals: {
        ...globals.browser,
      },
    },
    rules: {
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/**/*.test.{ts,tsx}', 'src/api/apiFetch.ts'],
    rules: {
      // ARCH-02-FR-009: feature requests must retain the shared Problem boundary.
      'no-restricted-globals': ['error', { name: 'fetch', message: 'Use apiFetch for feature HTTP requests.' }],
      'no-restricted-properties': ['error',
        { object: 'window', property: 'fetch', message: 'Use apiFetch for feature HTTP requests.' },
        { object: 'globalThis', property: 'fetch', message: 'Use apiFetch for feature HTTP requests.' },
        { object: 'self', property: 'fetch', message: 'Use apiFetch for feature HTTP requests.' },
      ],
    },
  },
);
