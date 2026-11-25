/**
 * The extra recipients who can stay when the request is for another site (0.29.1): only people the new site's list offers. While that
 * list is still loading (or could not be loaded) nobody stays, so the form never sends someone the site can't tell about the request.
 */
export function usableRecipients(ids: string[], people: { id: string }[] | undefined): string[] {
  const offered = new Set((people ?? []).map((p) => p.id));
  return ids.filter((id) => offered.has(id));
}
