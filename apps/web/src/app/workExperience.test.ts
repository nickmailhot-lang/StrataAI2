afterEach(() => { vi.unstubAllEnvs(); vi.resetModules(); });

it.each([[undefined, true], ['1', true], ['0', false]] as const)('PRD-03-VS-06-T-03: build setting %s selects the documented disclosure mode', async (value, enabled) => {
  vi.resetModules(); vi.stubEnv('VITE_STRATAAI_WORK_UX', value);
  expect((await import('./workExperience')).workExperienceEnabled).toBe(enabled);
});
