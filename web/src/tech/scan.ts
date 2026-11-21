import type { TechPlaces } from './types';

export type Scanned = { kind: 'asset' | 'location' | 'part'; id: string } | { kind: 'code'; code: string };

const LINK = /\/q\/(asset|location|part)\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:[/?#]|$)/i;

/**
 * What a scanned or typed code points at. Label links (`…/q/asset/<id>`) are read from their path only, whatever the
 * address before it, so labels keep working after a change of domain; anything else is a code to look up.
 */
export function parseCode(text: string): Scanned {
  const t = text.trim();
  const m = LINK.exec(t);
  return m ? { kind: m[1].toLowerCase() as 'asset' | 'location' | 'part', id: m[2].toLowerCase() } : { kind: 'code', code: t };
}

/** A code in the phone's copy: an asset number first (ignoring case), then an asset's barcode. */
export function lookupCode(places: TechPlaces | undefined, code: string): { kind: 'asset'; id: string } | null {
  const c = code.trim().toLowerCase();
  if (!c || !places) return null;
  const byNumber = places.assets.find((a) => a.assetNumber.toLowerCase() === c);
  const byBarcode = byNumber ?? places.assets.find((a) => a.barcode?.toLowerCase() === c);
  return byBarcode ? { kind: 'asset', id: byBarcode.id } : null;
}

/**
 * Where a scanned asset or location label leads: the technician view when it is on the phone; otherwise, with a
 * connection, the full app (it can be on a site this phone doesn't carry); offline, nowhere.
 */
export function placeTarget(kind: 'asset' | 'location', id: string, places: TechPlaces | undefined, online: boolean):
  { tech: string } | { full: string } | { problem: 'notOnPhone' } {
  const known = kind === 'asset' ? places?.assets.some((a) => a.id === id) : places?.locations.some((l) => l.id === id);
  if (known) return { tech: `/tech/place/${kind}/${id}` };
  if (online) return { full: kind === 'asset' ? `/assets/${id}` : '/locations' };
  return { problem: 'notOnPhone' };
}

/** The part a code names exactly: its part number or barcode, ignoring case (a search also returns near matches). */
export function exactPart(found: { id: string; partNumber: string; barcode?: string | null }[], code: string): string | null {
  const c = code.trim().toLowerCase();
  return found.find((p) => p.partNumber.toLowerCase() === c || p.barcode?.toLowerCase() === c)?.id ?? null;
}
