export function requireFeatureChecks(env) {
  if (env.SELECTION_RESULT !== 'success') throw new Error('Test selection did not succeed');
  for (const field of ['WEB_SELECTED','RUNTIME_SELECTED','FULL_SELECTED'])
    if (!['true','false'].includes(env[field])) throw new Error(`Invalid selection: ${field}`);
  if (env.FULL_SELECTED === 'true' && env.RUNTIME_SELECTED !== 'false') throw new Error('Full and bounded runtime cannot both be selected');
  const expected = {
    WEB_RESULT: env.FULL_SELECTED === 'false' && env.WEB_SELECTED === 'true',
    RUNTIME_RESULT: env.RUNTIME_SELECTED === 'true',
    FULL_RESULT: env.FULL_SELECTED === 'true',
  };
  for (const [field, selected] of Object.entries(expected))
    if (env[field] !== (selected ? 'success' : 'skipped')) throw new Error(`Required CI result mismatch: ${field}`);
}
if (process.argv[1]?.endsWith('/require-feature-checks.mjs')) requireFeatureChecks(process.env);
