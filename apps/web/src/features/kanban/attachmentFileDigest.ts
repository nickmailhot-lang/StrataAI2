import { sha256 } from '@noble/hashes/sha2.js';

// This is only the original retry-binding claim. The server independently
// inspects/measures complete bytes before it can publish quarantined metadata.
export async function attachmentFileDigest(file: Pick<Blob, 'size' | 'slice'>, maximumBytes: number,
  signal: AbortSignal, onProgress?: (bytesRead: number) => void): Promise<string> {
  signal.throwIfAborted();
  if (!Number.isSafeInteger(maximumBytes) || maximumBytes < 1 || maximumBytes > 1073741824
    || !Number.isSafeInteger(file.size) || file.size < 1 || file.size > maximumBytes) throw new Error('The selected file exceeds the upload size limit.');
  const hash = sha256.create(); const chunkBytes = 524288; const total = file.size;
  try {
    for (let offset = 0; offset < total; offset += chunkBytes) {
      signal.throwIfAborted();
      const end = Math.min(total, offset + chunkBytes);
      const bytes = new Uint8Array(await file.slice(offset, end).arrayBuffer());
      try {
        signal.throwIfAborted();
        if (bytes.length !== end - offset || file.size !== total) throw new Error('Unable to read the complete selected file.');
        hash.update(bytes); onProgress?.(end);
      } finally { bytes.fill(0); }
    }
    signal.throwIfAborted();
    const digest = hash.digest();
    try { return Array.from(digest, value => value.toString(16).padStart(2, '0')).join(''); }
    finally { digest.fill(0); }
  } finally { hash.destroy(); }
}
