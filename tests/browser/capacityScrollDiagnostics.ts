// Serialized into the browser: keep this function independent of Node imports.
// Numerical diagnostics only; never retain entity IDs, content or credentials.
export function installCapacityScrollDiagnostics() {
  type InputPoint = { at: number | null; x: number | null; y: number | null; buttons: number | null };
  type InputChannel = { count: number; last: InputPoint | null };
  const input = { pointerCapture: { count: 0, last: null } as InputChannel,
    pointerBubble: { count: 0, last: null } as InputChannel,
    mouseCapture: { count: 0, last: null } as InputChannel,
    mouseBubble: { count: 0, last: null } as InputChannel,
    pointerCancel: { count: 0, last: null } as InputChannel,
    pointerUp: { count: 0, last: null } as InputChannel };
  type Sample = { at: number; kind: string; axis: string; left: number; top: number;
    arguments: (number | null)[]; pointer: { x: number; y: number } | null;
    viewport: { left: number | null; top: number | null; right: number | null; bottom: number | null;
      width: number | null; height: number | null } | null };
  const target = window as unknown as { __capacityScrollDiagnostics?: { schema: number; dropped: number; samples: Sample[]; input: typeof input } };
  if (target.__capacityScrollDiagnostics) return;
  const evidence = { schema: 3, dropped: 0, samples: [] as Sample[], input };
  target.__capacityScrollDiagnostics = evidence;
  let pointer: { x: number; y: number } | null = null;
  const numeric = (value: unknown) => typeof value === 'number' && Number.isFinite(value) ? value : null;
  const observeInput = (channel: InputChannel, event: MouseEvent) => {
    try {
      channel.last = { at: numeric(window.performance.now()), x: numeric(event.clientX), y: numeric(event.clientY), buttons: numeric(event.buttons) };
      channel.count = Math.min(Number.MAX_SAFE_INTEGER, channel.count + 1);
    } catch { /* Input diagnostics must not interfere with native event dispatch. */ }
  };
  document.addEventListener('pointermove', event => observeInput(input.pointerCapture, event), true);
  document.addEventListener('pointermove', event => {
    observeInput(input.pointerBubble, event);
    pointer = { x: event.clientX, y: event.clientY };
  });
  document.addEventListener('mousemove', event => observeInput(input.mouseCapture, event), true);
  document.addEventListener('mousemove', event => observeInput(input.mouseBubble, event));
  document.addEventListener('pointercancel', event => observeInput(input.pointerCancel, event), true);
  document.addEventListener('pointerup', event => observeInput(input.pointerUp, event), true);
  const option = (value: object, key: string) => {
    // Do not invoke an accessor a second time before the native operation.
    try { return numeric(Object.getOwnPropertyDescriptor(value, key)?.value); } catch { return null; }
  };
  const record = (element: Element, kind: string, args: (number | null)[]) => {
    // Diagnostic failure must not change native scrolling or its exceptions.
    try {
      const axis = element.getAttribute('data-kanban-scroll-axis');
      if (!element.hasAttribute('data-kanban-scroll') || axis !== 'horizontal' && axis !== 'vertical') return;
      let viewport: Sample['viewport'] = null;
      try {
        const rect = element.getBoundingClientRect();
        viewport = { left: numeric(rect.left), top: numeric(rect.top), right: numeric(rect.right),
          bottom: numeric(rect.bottom), width: numeric(element.clientWidth), height: numeric(element.clientHeight) };
      } catch { /* Geometry failure must not suppress the numerical operation evidence. */ }
      evidence.samples.push({ at: window.performance.now(), kind, axis, left: element.scrollLeft,
        top: element.scrollTop, arguments: args, pointer, viewport });
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
