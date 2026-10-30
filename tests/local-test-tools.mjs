import { execFileSync } from 'node:child_process';
// Resolve before refusal fixtures deliberately replace PATH with stub tools.
// macOS /bin/bash is 3.2; use the installed modern Bash without changing any
// script assertion, refusal condition, deadline or Windows Git Bash behavior.
export const testBash = process.platform === 'win32'
  ? 'C:/Program Files/Git/bin/bash.exe'
  : execFileSync('bash', ['-c', 'printf %s "$BASH"'], { encoding: 'utf8' }).trim();
if (!testBash) throw new Error('A working Bash executable is required for shell contracts.');
