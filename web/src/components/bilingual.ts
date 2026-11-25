import i18n from '@/i18n';

/**
 * 0.28.0: a record's stored main text or its Arabic twin, by the reader's language. Used where a page shows a record's own
 * texts as stored (its detail, the Settings lists); lists and references already come from the server in the reader's language.
 */
export function pick(main: string, ar?: string | null, lang: string = i18n.language): string {
  return lang?.startsWith('ar') && ar && ar.trim() ? ar : main;
}

/** {@link pick} for texts that may be missing (notes). */
export function pickNote(main?: string | null, ar?: string | null, lang: string = i18n.language): string | null {
  return lang?.startsWith('ar') && ar && ar.trim() ? ar : main ?? null;
}
