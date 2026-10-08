// Serialized into the browser: keep this function independent of Node imports.
// Numerical diagnostics only; never retain entity IDs, content or credentials.
export function installCapacityScrollDiagnostics() {
  type Sample = { at: number; kind: string; axis: string; left: number; top: number;
    arguments: (number | null)[]; pointer: { x: number; y: number } | null };
  const target = window as unknown as { __capacityScrollDiagnostics?: { schema: number; dropped: number; samples: Sample[] } };
  if (target.__capacityScrollDiagnostics) return;
  const evidence = { schema: 1, dropped: 0, samples: [] as Sample[] };
  target.__capacityScrollDiagnostics = evidence;
  let pointer: { x: number; y: number } | null = null;
  document.addEventListener('pointermove', event => { pointer = { x: event.clientX, y: event.clientY }; });
  const numeric = (value: unknown) => typeof value === 'number' && Number.isFinite(value) ? value : null;
  const option = (value: object, key: string) => {
    // Do not invoke an accessor a second time before the native operation.
    try { return numeric(Object.getOwnPropertyDescriptor(value, key)?.value); } catch { return null; }
  };
  const record = (element: Element, kind: string, args: (number | null)[]) => {
    // Diagnostic failure must not change native scrolling or its exceptions.
    try {
      const axis = element.getAttribute('data-kanban-scroll-axis');
      if (!element.hasAttribute('data-kanban-scroll') || axis !== 'horizontal' && axis !== 'vertical') return;
      evidence.samples.push({ at: window.performance.now(), kind, axis, left: element.scrollLeft,
        top: element.scrollTop, arguments: args, pointer });
      if (evidence.samples.length > 2048) { evidence.samples.shift(); evidence.dropped++; }
    } catch { /* Keep the original browser operation authoritative. */ }
  };
  const scrollBy = Element.prototype.scrollBy;
  Element.prototype.scrollBy = function (this: Element, ...args: Parameters<typeof scrollBy>) {
    const first: unknown = args[0];
    const options = typeof first === 'object' && first !== null ? first as ScrollToOptions : undefined;
    record(this, 'scrollBy', options ? [option(options, 'left'), option(options, 'top')] : [numeric(first), numeric(args[1])]);
    return scrollBy.apply(this, args);
  } as typeof scrollBy;
  const scrollIntoView = Element.prototype.scrollIntoView;
  Element.prototype.scrollIntoView = function (...args: Parameters<typeof scrollIntoView>) {
    try { record(this.closest('[data-kanban-scroll]') ?? this, 'scrollIntoView', []); } catch { /* Preserve native delegation. */ }
    return scrollIntoView.apply(this, args);
  };
  const scrollLeft = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollLeft');
  if (scrollLeft?.set) Object.defineProperty(Element.prototype, 'scrollLeft', {
    ...scrollLeft, set(this: Element, value: number) {
      record(this, 'scrollLeft', [numeric(value)]); scrollLeft.set!.call(this, value);
    },
  });
}

export function readCapacityScrollDiagnostics() {
  return (window as unknown as { __capacityScrollDiagnostics?: unknown }).__capacityScrollDiagnostics ?? null;
}
