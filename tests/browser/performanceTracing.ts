// Snapshot serialization traverses Board DOM/accessibility after each action
// inside timing samples. Keep failed command/network traces and the separately
// configured failure screenshot without adding those recorder traversals.
export const performanceTrace = { mode: 'retain-on-failure', snapshots: false, screenshots: false } as const;
export const performanceTraceCondition = 'commands-and-network' as const;
