/** The site switch in the header (0.9.0): the one site this browser works in, sent with every request; none = every site. */
export const SITE_KEY = 'cmx.site';
export const SITE_HEADER = 'X-Cmx-Site';

export function getSite(): string | null {
  try { return localStorage.getItem(SITE_KEY) || null; } catch { return null; } // private mode
}

export function setSite(id: string | null): void {
  try { if (id) localStorage.setItem(SITE_KEY, id); else localStorage.removeItem(SITE_KEY); } catch { /* private mode */ }
}

/** The header that narrows a request to the chosen site, if one is chosen. */
export function siteHeader(): Record<string, string> {
  const site = getSite();
  return site ? { [SITE_HEADER]: site } : {};
}
