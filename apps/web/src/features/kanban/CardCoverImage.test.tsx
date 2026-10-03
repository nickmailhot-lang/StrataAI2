import { fireEvent, render, screen } from '@testing-library/react';
import { CardCoverImage } from './CardCoverImage';
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const props = { organizationId: id(1), boardId: id(2), unavailable: false,
  card: { id: id(3), title: 'Card title', description: null, rank: 'a', version: 4, hasCover: true } };
it('uses only current Card identity/revision and a lazy no-referrer sanitized image route', () => {
  render(<CardCoverImage {...props} />); const image = screen.getByRole('img', { name: 'Card cover' });
  expect(image).toHaveAttribute('src', `/cards/${id(3)}/cover/image?cardVersion=4`);
  expect(image).toHaveAttribute('loading', 'lazy'); expect(image).toHaveAttribute('referrerpolicy', 'no-referrer');
  expect(image).toHaveAttribute('decoding', 'async'); expect(image.getAttribute('src')).not.toContain('attachment');
});
it.each([{ unavailable: true }, { organizationId: 'foreign' }, { boardId: 'foreign' },
  { card: { ...props.card, id: 'unknown' } }, { card: { ...props.card, version: 0 } },
  { card: { ...props.card, hasCover: false } }, { card: { ...props.card, hasCover: undefined } }])('does not probe a Card outside a current admitted snapshot %j', patch => {
  render(<CardCoverImage {...props} {...patch} />); expect(screen.queryByRole('img')).not.toBeInTheDocument();
});
it('retires decoded content on access uncertainty, removal and changed Card version, without reviving an old failed request', () => {
  const view = render(<CardCoverImage {...props} />); const original = screen.getByRole('img'); fireEvent.error(original);
  expect(screen.queryByRole('img')).not.toBeInTheDocument(); expect(screen.getByText('Card cover unavailable.')).toBeVisible();
  view.rerender(<CardCoverImage {...props} card={{ ...props.card, version: 5 }} />);
  expect(screen.getByRole('img')).not.toBe(original); expect(screen.getByRole('img')).toHaveAttribute('src', `/cards/${id(3)}/cover/image?cardVersion=5`);
  view.rerender(<CardCoverImage {...props} unavailable />); expect(screen.queryByRole('img')).not.toBeInTheDocument();
  view.rerender(<CardCoverImage {...props} card={{ ...props.card, hasCover: false }} />); expect(screen.queryByRole('img')).not.toBeInTheDocument();
});
it('loads detail eagerly and changes the image instance on organization/Board/Card context switches', () => {
  const view = render(<CardCoverImage {...props} detail />); const original = screen.getByRole('img'); expect(original).toHaveAttribute('loading', 'eager');
  view.rerender(<CardCoverImage {...props} boardId={id(8)} detail />); expect(screen.getByRole('img')).not.toBe(original);
  view.rerender(<CardCoverImage {...props} card={{ ...props.card, id: id(9) }} detail />);
  expect(screen.getByRole('img')).toHaveAttribute('src', `/cards/${id(9)}/cover/image?cardVersion=4`);
});
