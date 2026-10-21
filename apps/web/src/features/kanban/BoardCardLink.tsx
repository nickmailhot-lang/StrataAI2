import { memo } from 'react';
import { Box, Card, CardContent, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
import { CardDueBadge, CardDueDate, cardDueDescriptionId } from './BoardDateBadges';
import { CardLabelIndicators } from './CardLabelIndicators';
import { CardMemberIndicators } from './CardMemberIndicators';
import { CardCoverImage } from './CardCoverImage';
import { workExperienceEnabled } from '../../app/workExperience';

type Props = {
  card: WorkCard; boardPath: string; links: Map<string, HTMLAnchorElement>;
  organizationId?: string; boardId?: string; coverUnavailable?: boolean;
  labels?: NonNullable<BoardSnapshot['cardLabels']>[string];
  members?: NonNullable<BoardSnapshot['cardMembers']>[string];
};
// Opening cached details changes drag availability, not every Card's content.
// Date context and canonical preview revisions still update their consumers.
export const BoardCardLink = memo(function BoardCardLink({ card, boardPath, links, labels, members, organizationId, boardId, coverUnavailable }: Props) {
  return <Card component={Link} aria-label={card.title}
    aria-describedby={card.dueAt ? cardDueDescriptionId(card.id) : undefined}
    ref={(node: HTMLAnchorElement | null) => { if (node) links.set(card.id, node); else links.delete(card.id); }}
    to={`${boardPath}/cards/${card.id}`} state={{ cardOverlay: true }}
    sx={{ display: 'block', minWidth: 0, color: 'inherit', textDecoration: 'none', border: '1px solid', borderColor: 'divider', boxShadow: 'none',
      scrollMarginBlock: '4px',
      ...(workExperienceEnabled && { borderRadius: 2, bgcolor: 'background.paper', boxShadow: 1 }),
      '&:hover': { borderColor: 'primary.main' }, '&:focus-visible': { outline: '3px solid', outlineColor: 'primary.main' } }}>
    {organizationId && boardId && <CardCoverImage organizationId={organizationId} boardId={boardId} card={card} unavailable={coverUnavailable !== false} />}
    <CardContent sx={workExperienceEnabled ? { p: 1.5, '&:last-child': { pb: 1.5 } } : undefined}>
      {workExperienceEnabled && <Box sx={{ pr: 4.5, mb: 0.5 }}><CardLabelIndicators preview={labels} /></Box>}
      <Typography component="span" title={card.title} sx={{ display: '-webkit-box', WebkitBoxOrient: 'vertical', WebkitLineClamp: 3,
        overflow: 'hidden', overflowWrap: 'anywhere', fontWeight: 600, lineHeight: 1.5,
        ...(workExperienceEnabled && { pr: 4.5, pt: 0.5 }) }}>{card.title}</Typography>
      {!workExperienceEnabled && <CardLabelIndicators preview={labels} />}
      <CardMemberIndicators compact version={card.version} preview={members} />
      <CardDueDate card={card} /><CardDueBadge card={card} />
    </CardContent>
  </Card>;
});
