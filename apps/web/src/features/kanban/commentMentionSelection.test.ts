import { selectedCommentMentions } from './commentMentionSelection';
const selected = { userId: '11111111-1111-1111-1111-111111111111', handle: 'teammate', handleVersion: 3 };
it('retains immutable account/revision expectations for repeated and moved tokens while dropping deleted tokens', () => {
  const expected = selectedCommentMentions(' Hello (@Teammate)\n@teammate ', [selected]);
  expect(expected).toEqual([selected]); expect(expected[0]).not.toBe(selected);
  expect(selectedCommentMentions('Updated words !@teammate', [selected])).toEqual([selected]);
  expect(selectedCommentMentions('Removed', [selected])).toEqual([]);
});
it.each(['mail@teammate', 'https://example.test/@teammate', 'mailto:(@teammate', 'www.example.test/@teammate',
  '@teammate.example', '@teammate-next', '@teammate@other', '@teammateé', '@teammate🙂'])('does not bind literal email, URL or malformed tokens (%s)', text => {
  expect(selectedCommentMentions(text, [selected])).toEqual([]);
});
it('matches normalized UTF-16 plaintext and bounded parser windows', () => {
  expect(selectedCommentMentions('🙂\r\n\u00a0@teammate', [selected])).toEqual([selected]);
  expect(() => selectedCommentMentions('🙂\r\n\u0085@teammate', [selected])).toThrow();
  expect(() => selectedCommentMentions(Array(65).fill('@teammate').join(' '), [selected])).toThrow();
  expect(() => selectedCommentMentions('@teammate', [selected, selected])).toThrow();
});
