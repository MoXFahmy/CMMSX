type Person = { id: string; fullName: string };

/**
 * The options of a "who is told about requests" picker (0.10.0): only people who can view requests, plus anyone already chosen
 * who can't (marked), so they can still be taken off. <paramref name="viewers"/> come from /users/lookup?requestViewers=true.
 */
export function requestRecipientOptions(viewers: Person[] | undefined, everyone: Person[] | undefined, chosen: string[], mark: (name: string) => string) {
  const offered = (viewers ?? []).map((u) => ({ value: u.id, label: u.fullName }));
  const kept = (everyone ?? []).filter((u) => chosen.includes(u.id) && !(viewers ?? []).some((v) => v.id === u.id))
    .map((u) => ({ value: u.id, label: mark(u.fullName) }));
  return [...offered, ...kept];
}
