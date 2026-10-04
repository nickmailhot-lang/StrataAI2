import { normalizeComment } from './cardComments';
import type { CardMentionOption } from './cardMentionOptions';
export type CommentMentionSelection = Pick<CardMentionOption, 'userId' | 'handle' | 'handleVersion'>;
const character = (value: string) => /^[a-z0-9_]$/i.test(value);
const whitespace = (value: string) => {
  const code = value.charCodeAt(0);
  return code >= 9 && code <= 13 || code >= 0x2000 && code <= 0x200a
    || [0x20, 0x85, 0xa0, 0x1680, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000].includes(code);
};
// Mirrors the server's plaintext lexical boundary, never recipient authority.
// Retire selection expectations only when their actual token has been removed.
function commentMentionHandles(content: string) {
  const text = normalizeComment(content); const handles = new Set<string>(); let segment = 0; let count = 0;
  for (let at = 0; at < text.length; at++) {
    if (whitespace(text[at])) { segment = at + 1; continue; }
    if (text[at] !== '@' || at > 0 && !whitespace(text[at - 1]) && !'([{,;:!?'.includes(text[at - 1])) continue;
    const prefix = text.slice(segment, at);
    if (prefix.includes('://') || /^(mailto:|www\.)/i.test(prefix)) continue;
    let end = at + 1; while (end < text.length && character(text[end])) end++;
    const tail = text[end] ?? ''; const unit = text.charCodeAt(end);
    if (end === at + 1 || tail === '@' || /[\p{L}\p{N}]/u.test(tail) || unit >= 0xd800 && unit <= 0xdfff
      || '.-'.includes(tail) && tail !== '' && end + 1 < text.length && character(text[end + 1])) continue;
    const handle = text.slice(at + 1, end).toLowerCase();
    if (handle !== 'card' && handle !== 'board' && !/^[a-z][a-z0-9_]{2,39}$/.test(handle)) continue;
    if (++count > 64) throw new Error('Too many comment mention tokens');
    handles.add(handle); at = end - 1;
  }
  return handles;
}
export function commentMassMentionScopes(content: string) {
  const handles = commentMentionHandles(content);
  return { card: handles.has('card'), board: handles.has('board') };
}
export function selectedCommentMentions(content: string, selected: readonly CommentMentionSelection[]) {
  const handles = commentMentionHandles(content);
  const result = selected.filter(item => handles.has(item.handle));
  if (result.length > 20 || new Set(result.map(item => item.userId.toLowerCase())).size !== result.length
    || new Set(result.map(item => item.handle)).size !== result.length) throw new Error('Invalid selected mentions');
  return result.map(({ userId, handle, handleVersion }) => ({ userId, handle, handleVersion }));
}
