import { render, screen } from '@testing-library/react';
import { CardDescriptionPreview, DescriptionParserWarmup, descriptionLink } from './CardDescriptionPreview';

it('initializes only fixed public grammar without creating any DOM content or links', () => {
  const { container } = render(<DescriptionParserWarmup />);
  expect(container).toBeEmptyDOMElement();
});

it('renders the required Markdown subset with semantic MUI content and preserves literal code', () => {
  const description = '# Scope\n\n**Strong** and *emphasis*, [Manual](https://example.test/manual), `inline`\n\n- First\n- Second\n\n3. Third\n4. Fourth\n\n> Retained quote\n\n```html\n<script>literal code</script>\n```';
  const { container } = render(<CardDescriptionPreview description={description} />);
  expect(screen.getByRole('region', { name: 'Description preview' })).toBeVisible();
  expect(screen.getByRole('heading', { name: 'Scope', level: 3 })).toBeVisible();
  expect(container.querySelector('strong')).toHaveTextContent('Strong');
  expect(container.querySelector('em')).toHaveTextContent('emphasis');
  expect(container.querySelectorAll('li')).toHaveLength(4);
  expect(container.querySelector('ol')).toHaveAttribute('start', '3');
  expect(container.querySelector('blockquote')).toHaveTextContent('Retained quote');
  expect(container.querySelector('pre code')).toHaveTextContent('<script>literal code</script>');
  expect(screen.getByRole('link', { name: 'Manual' })).toHaveAttribute('rel', 'noopener noreferrer nofollow');
  expect(container.querySelector('script')).toBeNull();
});

it('omits raw HTML capabilities and renders image alternatives without image or embed elements', () => {
  const { container } = render(<CardDescriptionPreview description={'<script>globalThis.descriptionAttack = true</script>\n\n<img src="https://example.test/pixel" onerror="alert(1)">\n\n<iframe src="https://example.test/embed"></iframe>\n\n![Diagram alternative](https://example.test/markdown-pixel)\n\n<svg onload="alert(1)"></svg>'} />);
  expect(container.querySelectorAll('script,img,iframe,svg,object,embed')).toHaveLength(0);
  expect(screen.getByText('Diagram alternative')).toBeVisible();
  expect(container.querySelector('[onerror],[onload]')).toBeNull();
});

it.each(['javascript:alert(1)', 'JaVaScRiPt:alert(1)', 'data:text/html,attack', 'vbscript:attack', 'ftp://example.test/file'])('refuses executable or unsupported link target %s', value => {
  expect(descriptionLink(value)).toBe('');
  const { container } = render(<CardDescriptionPreview description={`[Blocked](${value})`} />);
  expect(container.querySelector('a[href]')).toBeNull();
});

it('refuses entity-encoded executable schemes after Markdown parsing decodes their destinations', () => {
  const { container } = render(<CardDescriptionPreview description='[Encoded attack](java&#x73;cript&#58;alert(1))' />);
  expect(screen.getByText('Encoded attack')).toBeVisible();
  expect(container.querySelector('a[href]')).toBeNull();
});

it.each(['https://example.test/manual', 'http://example.test/manual', '/app/organizations', '#section', 'mailto:reader@example.test'])('preserves safe link target %s', value => {
  expect(descriptionLink(value)).toBe(value);
  render(<CardDescriptionPreview description={`[Reference](${value})`} />);
  expect(screen.getByRole('link', { name: 'Reference' })).toHaveAttribute('href', value);
});

it('replaces rendered source without retaining another description and exposes the empty state', () => {
  const { rerender } = render(<CardDescriptionPreview description='# Previous private context' />);
  rerender(<CardDescriptionPreview description='## Current context' />);
  expect(screen.queryByText('Previous private context')).toBeNull();
  expect(screen.getByRole('heading', { name: 'Current context' })).toBeVisible();
  rerender(<CardDescriptionPreview description='   ' />);
  expect(screen.queryByText('Current context')).toBeNull();
  expect(screen.getByText('No description.')).toBeVisible();
});
it('keeps long literal code keyboard focusable for independent horizontal scrolling', () => {
  const literal = 'long literal segment '.repeat(12);
  const { container } = render(<CardDescriptionPreview description={'```text\n' + literal + '\n```'} />);
  const block = container.querySelector('pre')!;
  expect(block.textContent).toBe(literal + '\n');
  block.focus();
  expect(block).toHaveFocus();
});
