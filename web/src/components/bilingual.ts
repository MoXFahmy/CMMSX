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

/**
 * 0.29.2: safety notes are never hidden. The reader's language when it has text, else the other language; a blank text (empty or
 * only spaces) counts as missing. Used by the work order page and the job card; the server and the phone follow the same rule.
 */
export function safetyText(main?: string | null, ar?: string | null, lang: string = i18n.language): string | null {
  const [first, second] = lang?.startsWith('ar') ? [ar, main] : [main, ar];
  return first && first.trim() ? first : second && second.trim() ? second : null;
}
