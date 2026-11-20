/** The switched-off e-mails after switching one on or off (from the list saved now, so quick toggles don't undo each other). */
export function withEmail(disabled: string[] | undefined, key: string, on: boolean): string[] {
  const list = disabled ?? [];
  if (on) return list.filter((k) => k !== key);
  return list.includes(key) ? list : [...list, key];
}
