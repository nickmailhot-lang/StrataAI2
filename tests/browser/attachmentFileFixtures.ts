export type AttachmentFileFormat = 'png' | 'jpeg' | 'webp' | 'pdf';

// Real 1x1 JPEG/WebP encodings produced once by the installed Chromium canvas.
// Keep WebP's complete RIFF length; appended bytes are deliberately not allowed.
const jpeg = '/9j/4AAQSkZJRgABAQAAAQABAAD/4gHYSUNDX1BST0ZJTEUAAQEAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADb/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAACAn/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCLwBTX8f/Z';
const webp = 'UklGRiICAABXRUJQVlA4WAoAAAAgAAAAAAAAAAAASUNDUMgBAAAAAAHIAAAAAAQwAABtbnRyUkdCIFhZWiAH4AABAAEAAAAAAABhY3NwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAA9tYAAQAAAADTLQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlkZXNjAAAA8AAAACRyWFlaAAABFAAAABRnWFlaAAABKAAAABRiWFlaAAABPAAAABR3dHB0AAABUAAAABRyVFJDAAABZAAAAChnVFJDAAABZAAAAChiVFJDAAABZAAAAChjcHJ0AAABjAAAADxtbHVjAAAAAAAAAAEAAAAMZW5VUwAAAAgAAAAcAHMAUgBHAEJYWVogAAAAAAAAb6IAADj1AAADkFhZWiAAAAAAAABimQAAt4UAABjaWFlaIAAAAAAAACSgAAAPhAAAts9YWVogAAAAAAAA9tYAAQAAAADTLXBhcmEAAAAAAAQAAAACZmYAAPKnAAANWQAAE9AAAApbAAAAAAAAAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAACAAAAAcAEcAbwBvAGcAbABlACAASQBuAGMALgAgADIAMAAxADZWUDggNAAAABACAJ0BKgEAAQAAgAgloAJ0ugH4AfoAA8gA/v1Xs//rlrQT2z/1y7//us+eMXef/+6hAAA=';

function pdf(): Buffer {
  const stream = '% PRIVATE BROWSER ORIGINAL PDF METADATA\n';
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << >> /Contents 4 0 R >>',
    '<< /Length ' + Buffer.byteLength(stream) + ' >>\nstream\n' + stream + 'endstream',
  ];
  let body = '%PDF-1.4\n'; const offsets: number[] = [];
  for (let i = 0; i < objects.length; i++) {
    offsets.push(Buffer.byteLength(body));
    body += (i + 1) + ' 0 obj\n' + objects[i] + '\nendobj\n';
  }
  const start = Buffer.byteLength(body);
  body += 'xref\n0 5\n0000000000 65535 f \n';
  for (const offset of offsets) body += String(offset).padStart(10, '0') + ' 00000 n \n';
  body += 'trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n' + start + '\n%%EOF\n';
  return Buffer.from(body);
}

export function attachmentFileFixture(format: AttachmentFileFormat, rejected: boolean) {
  if (rejected && format !== 'png') throw new Error('Quarantine fixture requires PNG.');
  const tail = Buffer.from(rejected ? 'STRATAAI_CI_HARMLESS_REJECT_FIXTURE' : 'PRIVATE BROWSER ORIGINAL TRAILING METADATA');
  const fixture = {
    png: { mimeType: 'image/png', extension: 'png', image: true,
      buffer: Buffer.concat([Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64'), tail]) },
    jpeg: { mimeType: 'image/jpeg', extension: 'jpg', image: true, buffer: Buffer.concat([Buffer.from(jpeg, 'base64'), tail]) },
    webp: { mimeType: 'image/webp', extension: 'webp', image: true, buffer: Buffer.from(webp, 'base64') },
    pdf: { mimeType: 'application/pdf', extension: 'pdf', image: false, buffer: pdf() },
  }[format];
  return fixture;
}
