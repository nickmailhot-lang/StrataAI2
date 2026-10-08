import './rootFailure.css';

// React has already failed and removed its tree. This last-resort view must not
// depend on that renderer, router, theme provider, or private diagnostic data.
export function renderRootFailure(host: HTMLElement, reload = () => window.location.reload()) {
  const document = host.ownerDocument;
  const main = document.createElement('main'); main.className = 'strataai-root-failure';
  const heading = document.createElement('h1'); heading.textContent = 'This application is unavailable.'; heading.tabIndex = -1;
  const alert = document.createElement('p'); alert.setAttribute('role', 'alert');
  alert.textContent = 'A submitted change may still have completed. Check the current state before trying again.';
  const warning = document.createElement('p'); warning.textContent = 'Reloading may discard unsaved changes.';
  const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Reload this page';
  button.addEventListener('click', reload);
  main.append(heading, alert, warning, button);
  host.replaceChildren(main);
  heading.focus(); // Focus safe information, not an action that could discard a draft.
}
