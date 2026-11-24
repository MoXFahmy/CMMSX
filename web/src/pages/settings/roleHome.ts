import type { TFunction } from 'i18next';
import { CARD_INFO, CLASSIC_LAYOUT, HOME_CARD_TYPES, type HomeCard, type HomeCardType } from '@/pages/home/cards';

/** Cards that can still be added to a role's home page, in catalogue order (each card at most once). */
export const addableCards = (current: HomeCard[]): HomeCardType[] => HOME_CARD_TYPES.filter((t) => !current.some((c) => c.type === t));

/** The first permission group a role doesn't meet for a card (any one of them would do), or null when it can use it. */
export function missingFor(type: HomeCardType, rolePermissions: string[]): string[] | null {
  return CARD_INFO[type].needs.find((anyOf) => !anyOf.some((p) => rolePermissions.includes(p))) ?? null;
}

/** The cards after moving one a place up (-1) or down (1); at either end the same list comes back. */
export function moveCard(cards: HomeCard[], index: number, by: -1 | 1): HomeCard[] {
  const j = index + by;
  if (index < 0 || index >= cards.length || j < 0 || j >= cards.length) return cards;
  const next = [...cards];
  [next[index], next[j]] = [next[j], next[index]];
  return next;
}

/** "Start from today's dashboard". */
export const classicCards = (): HomeCard[] => CLASSIC_LAYOUT.map((type) => ({ type }));

/** A CMX address: one leading "/", no backslash, no scheme, at most 300 characters (the server checks the same). */
export function isSafeLink(to: string): boolean {
  const s = to.trim();
  if (!s || s.length > 300 || s[0] !== '/') return false;
  if (s.length > 1 && (s[1] === '/' || s[1] === '\\')) return false;
  if (s.includes('\\')) return false;
  const end = s.search(/[?#]/);
  const path = end < 0 ? s : s.slice(0, end);
  // eslint-disable-next-line no-control-regex
  return !path.includes(':') && !/[\u0000-\u001f\u007f]/.test(s);
}

/** Addresses a tile can open: the menu's pages and the common "new" forms. Any other CMX address can be typed. */
export function linkChoices(t: TFunction, terms: { assets: string; locations: string }): { value: string; label: string }[] {
  return [
    { value: '/', label: t('nav.home') },
    { value: '/work-requests?new=1', label: t('roles.links.newRequest') },
    { value: '/work-orders/new', label: t('roles.links.newWorkOrder') },
    { value: '/assets/new', label: t('roles.links.newAsset') },
    { value: '/work-requests', label: t('nav.workRequests') },
    { value: '/work-orders', label: t('nav.workOrders') },
    { value: '/work-orders?view=mine', label: t('home.cards.my-work') },
    { value: '/planning', label: t('nav.planning') },
    { value: '/pm', label: t('nav.preventive') },
    { value: '/locations', label: terms.locations },
    { value: '/assets', label: terms.assets },
    { value: '/inventory', label: t('nav.inventory') },
    { value: '/purchasing', label: t('nav.purchasing') },
    { value: '/approvals', label: t('nav.myApprovals') },
    { value: '/contracts', label: t('nav.contracts') },
    { value: '/reports', label: t('nav.reports') },
    { value: '/people', label: t('nav.people') },
  ];
}
