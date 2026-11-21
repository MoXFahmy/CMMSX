import { span } from './sla';

/** How long an asset has been down: "2 h 15 min", "1 d 3 h" (the unit letters follow the language). */
export function downFor(since: string, now: Date): string {
  return span(now.getTime() - new Date(since).getTime());
}

/** The part picker's groups: the asset's own spare parts first, then the other matches, each in the order given. */
export function groupPicked<T extends { onAsset?: number | null }>(items: T[]): { onAsset: T[]; others: T[] } {
  return {
    onAsset: items.filter((p) => p.onAsset != null),
    others: items.filter((p) => p.onAsset == null),
  };
}
