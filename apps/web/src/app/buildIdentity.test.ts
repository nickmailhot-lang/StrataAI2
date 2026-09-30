import { describe, expect, it } from 'vitest';
import { createBuildIdentity } from './buildIdentity';

describe('ARCH-01-AC-002 web build identity', () => {
  it('preserves the build-time commit and version', () => {
    const revision = 'a'.repeat(40);
    expect(createBuildIdentity(revision, '0.1.0-42')).toEqual({ revision, version: '0.1.0-42' });
    expect(createBuildIdentity()).toEqual({ revision: 'development', version: '0.0.0-dev' });
  });
  it.each(['', 'runtime-spoof', 'a'.repeat(39), 'a'.repeat(40) + '\n'])('rejects invalid revisions without echoing input', revision => {
    expect(() => createBuildIdentity(revision, '1.0.0')).toThrow('Invalid web build revision.');
  });
  it.each(['', '1.0.0\n', 'a'.repeat(81)])('rejects invalid versions', version => {
    expect(() => createBuildIdentity('development', version)).toThrow('Invalid web build version.');
  });
});
