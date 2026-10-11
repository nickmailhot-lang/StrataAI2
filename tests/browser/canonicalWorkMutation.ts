const workRoot = /^\/(organizations|boards|lists|cards|attachments|labels)(\/|$)/;
const negotiationPaths = new Set([
  '/boards/live/negotiate',
  '/boards/live/stars/negotiate',
  '/organizations/live/negotiate',
  '/organizations/live/lifecycle/negotiate',
  '/organizations/live/operational-lifecycle/negotiate',
  '/organizations/live/metadata/negotiate',
  '/organizations/live/configuration/negotiate',
]);

/** Count work commands while excluding only the mapped hubs' POST handshakes. */
export function isCanonicalWorkMutation(method: string, url: string): boolean {
  const path = new URL(url).pathname;
  if (method === 'GET' || method === 'HEAD' || !workRoot.test(path)) return false;
  return !(method === 'POST' && negotiationPaths.has(path));
}
