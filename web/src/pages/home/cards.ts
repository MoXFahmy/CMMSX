import { P } from '@/auth/AuthContext';
import type { Can } from '@/components/list/apiQueries';

/** The cards a role's home page can hold (0.22.0), in catalogue order; the server keeps the same keys. */
export const HOME_CARD_TYPES = [
  'work-summary', 'work-by-site', 'urgent-work', 'asset-summary', 'assets-by-status', 'assets-by-category', 'assets-by-site',
  'warranty-soon', 'recent-assets', 'my-work', 'unassigned-work', 'late-work', 'assets-down', 'low-stock', 'my-approvals',
  'quick-request', 'location-tree', 'shortcuts',
] as const;

export type HomeCardType = (typeof HOME_CARD_TYPES)[number];
export type HomeTile = { label: string; labelAr?: string | null; color: string; to: string };
export type HomeCard = { type: HomeCardType; tiles?: HomeTile[] | null };
export type CardGroup = 'dashboard' | 'work' | 'assets' | 'tools';

/** Tile colours: Mantine palette names, so they follow the light and dark themes. */
export const TILE_COLORS = ['blue', 'teal', 'green', 'orange', 'red', 'grape', 'indigo', 'gray'] as const;

const dash = [P.dashboardView];
const wo = [P.woView];

/**
 * Each card's width, its group in the role window, and what it needs: every inner list must be met, by any one of its
 * permissions.
 */
export const CARD_INFO: Record<HomeCardType, { width: 'full' | 'half'; group: CardGroup; needs: string[][] }> = {
  'work-summary': { width: 'full', group: 'dashboard', needs: [dash, wo] },
  'work-by-site': { width: 'full', group: 'dashboard', needs: [dash, wo] },
  'urgent-work': { width: 'half', group: 'dashboard', needs: [dash, wo] },
  'asset-summary': { width: 'full', group: 'dashboard', needs: [dash] },
  'assets-by-status': { width: 'half', group: 'dashboard', needs: [dash] },
  'assets-by-category': { width: 'half', group: 'dashboard', needs: [dash] },
  'assets-by-site': { width: 'half', group: 'dashboard', needs: [dash] },
  'warranty-soon': { width: 'half', group: 'dashboard', needs: [dash] },
  'recent-assets': { width: 'half', group: 'dashboard', needs: [dash] },
  'my-work': { width: 'half', group: 'work', needs: [wo] },
  'unassigned-work': { width: 'half', group: 'work', needs: [wo] },
  'late-work': { width: 'half', group: 'work', needs: [wo] },
  'assets-down': { width: 'half', group: 'assets', needs: [[P.assetsView]] },
  'low-stock': { width: 'half', group: 'assets', needs: [[P.inventoryView]] },
  'my-approvals': { width: 'half', group: 'tools', needs: [[P.purchasingApprove, P.purchasingApproveRequests]] },
  'quick-request': { width: 'half', group: 'tools', needs: [[P.requestsCreate]] },
  'location-tree': { width: 'full', group: 'tools', needs: [[P.locationsView]] },
  shortcuts: { width: 'full', group: 'tools', needs: [] },
};

/** Today's dashboard as cards, in its order: what "Start from today's dashboard" adds. */
export const CLASSIC_LAYOUT: HomeCardType[] = [
  'work-summary', 'work-by-site', 'urgent-work', 'asset-summary', 'assets-by-status', 'assets-by-category', 'assets-by-site', 'warranty-soon', 'recent-assets',
];

/** Whether this person can use a card: its permissions, an employee record for My work, and at least one tile for shortcuts. */
export function canUseCard(card: HomeCard, can: Can, me: { employeeId?: string | null }): boolean {
  const info = CARD_INFO[card.type];
  if (!info || !info.needs.every((anyOf) => anyOf.some((p) => can(p)))) return false;
  if (card.type === 'my-work' && !me.employeeId) return false;
  if (card.type === 'shortcuts' && !(card.tiles?.length)) return false;
  return true;
}

/** Rows of the home grid: a full card alone; half cards two by two, a half card before a full one alone. */
export function gridRows(cards: HomeCard[]): HomeCard[][] {
  const rows: HomeCard[][] = [];
  for (let i = 0; i < cards.length; i++) {
    const card = cards[i];
    const next = cards[i + 1];
    if (CARD_INFO[card.type]?.width === 'half' && next && CARD_INFO[next.type]?.width === 'half') { rows.push([card, next]); i++; }
    else rows.push([card]);
  }
  return rows;
}

export type HomeChoice = { kind: 'role'; cards: HomeCard[] } | { kind: 'classic' } | { kind: 'empty' };

/** The role's cards this person can use; with none, today's dashboard if they may see it, otherwise a message. */
export function homeChoice(cards: HomeCard[], can: Can, me: { employeeId?: string | null }): HomeChoice {
  const usable = cards.filter((c) => canUseCard(c, can, me));
  if (usable.length > 0) return { kind: 'role', cards: usable };
  return can(P.dashboardView) ? { kind: 'classic' } : { kind: 'empty' };
}
