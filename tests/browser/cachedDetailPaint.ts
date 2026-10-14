// These functions run in the browser via page.evaluate. Use its clock
// explicitly: a test module's imported Node clock cannot cross that boundary.
export function armCachedDetailPaint({ path, title }: { path: string; title: string }) {
  const state = window as Window & { strataaiDetailPaint?: Promise<number> };
  state.strataaiDetailPaint = new Promise(resolve => {
    let began = 0;
    let finished = false;
    const finish = (value: number) => {
      if (finished) return;
      finished = true; clearTimeout(timer);
      document.removeEventListener('click', clicked, true); resolve(value);
    };
    const ready = () => {
      const input = document.querySelector<HTMLInputElement>('[role="dialog"] input[name="title"]');
      const rect = input?.getBoundingClientRect();
      return !!input && !input.disabled && input.value === title && document.activeElement === input
        && !input.closest('[hidden],[inert],[aria-hidden="true"]') && !!rect && rect.width > 0 && rect.height > 0
        && rect.top < innerHeight && rect.bottom > 0 && rect.left < innerWidth && rect.right > 0;
    };
    const check = () => {
      if (finished) return;
      if (ready()) requestAnimationFrame(() => {
        if (finished) return;
        if (ready()) finish(window.performance.now() - began);
        else requestAnimationFrame(check);
      });
      else requestAnimationFrame(check);
    };
    const clicked = (event: MouseEvent) => {
      if (finished) return;
      const anchor = event.target instanceof Element ? event.target.closest('a') : null;
      if (!anchor || anchor.pathname !== path) return;
      began = window.performance.now(); document.removeEventListener('click', clicked, true);
      requestAnimationFrame(check);
    };
    const timer = setTimeout(() => finish(Infinity), 5_000);
    document.addEventListener('click', clicked, true);
  });
}

export function readCachedDetailPaint() {
  const state = window as Window & { strataaiDetailPaint?: Promise<number> };
  if (!state.strataaiDetailPaint) throw new Error('Cached detail observation was not armed.');
  return state.strataaiDetailPaint.finally(() => { delete state.strataaiDetailPaint; });
}
