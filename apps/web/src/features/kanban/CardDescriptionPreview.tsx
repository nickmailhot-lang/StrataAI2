import { memo } from 'react';
import { Box, Typography } from '@mui/material';
import Markdown, { defaultUrlTransform, type Components } from 'react-markdown';

// Parse Markdown into React nodes only. No raw HTML plugin, DOM insertion,
// author-supplied attributes or remote image requests are permitted.
export function descriptionLink(value: string): string {
  const safe = defaultUrlTransform(value);
  if (!safe) return '';
  try {
    const url = new URL(safe, 'https://strataai.invalid/');
    return ['https:', 'http:', 'mailto:'].includes(url.protocol) ? safe : '';
  } catch { return ''; }
}
const elements = ['p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'strong', 'em', 'a', 'ul', 'ol', 'li', 'blockquote', 'pre', 'code', 'br', 'hr', 'img'];
const components: Components = {
  h1: ({ children }) => <h3>{children}</h3>,
  h2: ({ children }) => <h4>{children}</h4>,
  h3: ({ children }) => <h5>{children}</h5>,
  h4: ({ children }) => <h6>{children}</h6>,
  h5: ({ children }) => <h6>{children}</h6>,
  h6: ({ children }) => <h6>{children}</h6>,
  // Code can overflow independently of the Card. Make that scroll region
  // reachable with Tab and native arrow keys on every supported browser.
  pre: ({ children }) => <pre tabIndex={0}>{children}</pre>,
  a: ({ href, children }) => {
    if (!href) return <span>{children}</span>;
    const email = /^mailto:/i.test(href);
    return <a href={href} target="_blank" rel="noopener noreferrer nofollow"
      title={email ? 'Opens your email app.' : 'Opens in a new tab.'}>
      {children}<span aria-hidden="true" data-description-link-hint>{email ? ' (email app)' : ' (new tab)'}</span>
    </a>;
  },
  // Images are outside the supported subset: retain their textual alternative
  // without giving untrusted description text a network/attachment capability.
  img: ({ alt }) => <span>{alt}</span>,
};

// Initialize the shared grammar using only fixed public syntax while the
// admitted shell loads. No Card/profile values or rendered records are cached.
const grammarSample = '# Heading\n\n**Bold** and *italic* with [reference](#reference).\n\n- Item\n\n1. Item\n\n> Quote\n\n```text\nCode\n```';
function discardPublicGrammar() {
  // Applied only to the fixed sample: parse its grammar, then discard its tree
  // before producing any DOM (including inter-block whitespace text nodes).
  return (tree: { children: unknown[] }) => { tree.children = []; };
}
const warmPlugins = [discardPublicGrammar];
export const DescriptionParserWarmup = memo(function DescriptionParserWarmup() {
  return <Markdown skipHtml allowedElements={elements} urlTransform={descriptionLink} remarkPlugins={warmPlugins}>{grammarSample}</Markdown>;
});

export const CardDescriptionPreview = memo(function CardDescriptionPreview({ description }: { description: string }) {
  return <Box component="section" aria-label="Description preview" sx={{ my: 2, minWidth: 0, overflowWrap: 'anywhere',
      '& > h3': { typography: 'subtitle1', m: 0 },
      '& > [data-description-content]': {
          // One theme-owned style scope for semantic Markdown nodes avoids
          // mounting a separate styled component for every paragraph/list/code.
          '& p': { my: 1 },
          '& h3': { typography: 'h5', mt: 2, mb: 1 },
          '& h4': { typography: 'h6', mt: 2, mb: 1 },
          '& h5': { typography: 'subtitle1', mt: 2, mb: 1 },
          '& h6': { typography: 'subtitle2', mt: 2, mb: 1 },
          '& ol, & ul': { pl: 3 },
          '& blockquote': { mx: 0, pl: 2, borderLeft: 3, borderColor: 'divider' },
          '& pre': { maxWidth: '100%', overflowX: 'auto', p: 1, bgcolor: 'action.hover', borderRadius: 1 },
          '& pre:focus-visible': { outline: '3px solid', outlineColor: 'primary.main', outlineOffset: 2 },
          '& code': { fontFamily: 'monospace' },
          '& a': { color: 'primary.main', textDecoration: 'underline', '&:focus-visible': { outline: '3px solid', outlineColor: 'primary.main' } },
          '& [data-description-link-hint]': { typography: 'caption', color: 'text.secondary' },
        },
    }}>
    <h3>Description preview</h3>
    {description.trim()
      ? <div data-description-content><Markdown skipHtml allowedElements={elements} unwrapDisallowed urlTransform={descriptionLink} components={components}>{description}</Markdown></div>
      : <Typography color="text.secondary">No description.</Typography>}
  </Box>;
});
