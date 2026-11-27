import type { ComboboxItem, OptionsFilter } from '@mantine/core';

/** A stored place for searching (0.28.0): the name shown, the other language's name, and the label a picker shows. */
export type SearchablePlace = { name: string; otherName?: string | null; label?: string | null };

/** True when every word of the search is in the place's shown name, its other name or its label (case ignored). */
export function matchesPlace(place: SearchablePlace, term: string): boolean {
  const words = term.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return true;
  const text = `${place.label ?? ''} ${place.name} ${place.otherName ?? ''}`.toLowerCase();
  return words.every((w) => text.includes(w));
}

/** A picker filter that also finds places by their other name; <paramref>others</paramref> maps an option's value to that name. */
export function placeFilter(others: Map<string, string | null | undefined>): OptionsFilter {
  return ({ options, search }) => (options as ComboboxItem[]).filter((o) =>
    matchesPlace({ name: o.label, otherName: others.get(o.value), label: o.label }, search));
}
