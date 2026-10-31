import { render, screen } from '@testing-library/react';
import { CardDescriptionPreview } from './CardDescriptionPreview';

it.each(['https://example.test/manual', 'http://example.test/manual', '/app/organizations', '#section'])(
  'explains the new-tab action without replacing the reference name: %s', href => {
    render(<CardDescriptionPreview description={`[Reference](${href})`} />);
    const link = screen.getByRole('link', { name: 'Reference' });
    expect(link).toHaveAccessibleDescription('Opens in a new tab.');
    expect(link).toHaveAttribute('href', href);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer nofollow');
    expect(screen.getByText('(new tab)')).toBeVisible();
    link.focus(); expect(link).toHaveFocus();
  },
);

it('identifies the email-app action rather than promising a browser tab', () => {
  render(<CardDescriptionPreview description='[Contact](mailto:reader@example.test)' />);
  const link = screen.getByRole('link', { name: 'Contact' });
  expect(link).toHaveAccessibleDescription('Opens your email app.');
  expect(link).toHaveAttribute('href', 'mailto:reader@example.test');
  expect(screen.getByText('(email app)')).toBeVisible();
  expect(screen.queryByText('(new tab)')).toBeNull();
});

it('keeps formatted reference names intact and retires hints with protected source', () => {
  const { rerender } = render(<CardDescriptionPreview description='[**Private reference**](https://example.test/private)' />);
  expect(screen.getByRole('link', { name: 'Private reference' })).toHaveAccessibleDescription('Opens in a new tab.');
  rerender(<CardDescriptionPreview description='' />);
  expect(screen.queryByRole('link')).toBeNull();
  expect(screen.queryByText('(new tab)')).toBeNull();
  expect(screen.getByText('No description.')).toBeVisible();
});

it('does not turn a blocked destination into an actionable link or hint', () => {
  render(<CardDescriptionPreview description='[Blocked](javascript:alert(1))' />);
  expect(screen.queryByRole('link')).toBeNull();
  expect(screen.queryByText('(new tab)')).toBeNull();
  expect(screen.getByText('Blocked')).toBeVisible();
});
