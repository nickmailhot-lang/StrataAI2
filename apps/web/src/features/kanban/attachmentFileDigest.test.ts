import { attachmentFileDigest } from './attachmentFileDigest';

// Native independent oracle is confined to Vitest and never browser code.
const { createHash } = await vi.importActual<{ createHash: (algorithm: string) => {
  update: (bytes: Uint8Array) => { digest: (encoding: 'hex') => string };
} }>('node:crypto');

function source(bytes: Uint8Array, afterRead?: () => void) {
  const buffers: Uint8Array[] = []; const ranges: [number, number][] = [];
  const file = { size: bytes.length, slice: (start = 0, end = bytes.length) => {
    ranges.push([start, end]);
    return { arrayBuffer: async () => { const copy = bytes.slice(start, end); buffers.push(copy); afterRead?.(); return copy.buffer; } } as Blob;
  } };
  return { file, buffers, ranges };
}
it.each([1, 55, 56, 63, 64, 65, 100003, 524288, 1048579])('binds complete %s-byte input to native SHA-256 with bounded reads and no retained buffers', async length => {
  const bytes = Uint8Array.from({ length }, (_, index) => index % 251); const fixture = source(bytes); const progress = vi.fn();
  const digest = await attachmentFileDigest(fixture.file, 20971520, new AbortController().signal, progress);
  expect(digest).toBe(createHash('sha256').update(bytes).digest('hex'));
  expect(fixture.ranges[0][0]).toBe(0); expect(fixture.ranges.at(-1)![1]).toBe(length);
  expect(fixture.ranges.every(([start, end]) => end - start <= 524288)).toBe(true);
  expect(fixture.buffers.every(buffer => buffer.every(value => value === 0))).toBe(true);
  expect(progress).toHaveBeenLastCalledWith(length); expect(bytes[0]).toBe(0); if (length > 1) expect(bytes[1]).toBe(1);
});
it('refuses empty/over-policy/invalid bounds before reading a file', async () => {
  for (const [size, maximum] of [[0, 10], [11, 10], [1, 0], [1, 1073741825], [1.5, 10]]) {
    const slice = vi.fn(); await expect(attachmentFileDigest({ size, slice }, maximum, new AbortController().signal)).rejects.toThrow(); expect(slice).not.toHaveBeenCalled();
  }
});
it('propagates cancellation before/after a read, zeroes read buffers and never returns partial digest', async () => {
  const before = new AbortController(); before.abort(); const unread = source(new Uint8Array(10));
  await expect(attachmentFileDigest(unread.file, 10, before.signal)).rejects.toThrow(); expect(unread.ranges).toHaveLength(0);
  const cancelled = new AbortController(); const fixture = source(new Uint8Array(1048579).fill(7), () => cancelled.abort());
  await expect(attachmentFileDigest(fixture.file, 20971520, cancelled.signal)).rejects.toThrow(); expect(fixture.ranges).toHaveLength(1);
  expect(fixture.buffers[0].every(value => value === 0)).toBe(true);
});
it('refuses a truncated slice or changing file size without manufacturing an original digest', async () => {
  const broken = { size: 10, slice: () => ({ arrayBuffer: async () => new ArrayBuffer(9) } as Blob) };
  await expect(attachmentFileDigest(broken, 10, new AbortController().signal)).rejects.toThrow('complete selected file');
  const fixture = source(new Uint8Array(10), () => { fixture.file.size = 9; });
  await expect(attachmentFileDigest(fixture.file, 10, new AbortController().signal)).rejects.toThrow('complete selected file');
  expect(fixture.buffers[0].every(value => value === 0)).toBe(true);
});
