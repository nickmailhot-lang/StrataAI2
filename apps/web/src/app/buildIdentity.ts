export function createBuildIdentity(revision = 'development', version = '0.0.0-dev') {
  if (revision !== revision.trim() || (revision !== 'development' && !/^(?:[a-f0-9]{40}|[a-f0-9]{64})$/.test(revision))) {
    throw new Error('Invalid web build revision.');
  }
  if (version !== version.trim() || !/^[0-9A-Za-z.+_-]{1,80}$/.test(version)) {
    throw new Error('Invalid web build version.');
  }
  return { revision, version };
}
