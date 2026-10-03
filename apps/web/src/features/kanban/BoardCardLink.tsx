import { memo } from 'react';
import { Card, CardContent } from '@mui/material';
import { Link } from 'react-router-dom';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
import { CardDueBadge, cardDueDescriptionId } from './BoardDateBadges';
import { CardLabelIndicators } from './CardLabelIndicators';
import { CardMemberIndicators } from './CardMemberIndicators';
import { CardCoverImage } from './CardCoverImage';

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
    sx={{ display: 'block', color: 'inherit', textDecoration: 'none', '&:focus-visible': { outline: '3px solid', outlineColor: 'primary.main' } }}>
    {organizationId && boardId && <CardCoverImage organizationId={organizationId} boardId={boardId} card={card} unavailable={coverUnavailable !== false} />}
    <CardContent>{card.title}<CardLabelIndicators preview={labels} /><CardMemberIndicators version={card.version} preview={members} /><CardDueBadge card={card} /></CardContent>
  </Card>;
});
