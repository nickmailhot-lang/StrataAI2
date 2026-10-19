// Build-time rollback for action disclosure and the Card panel migration.
// No domain data, permissions, routes or command receipts depend on this flag.
export const workExperienceEnabled = import.meta.env.VITE_STRATAAI_WORK_UX !== '0';
