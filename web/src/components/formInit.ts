/** A record an edit form is set up from: its id and its version (the row version, or the change time where there is none). */
type Versioned = { id: string; rowVersion?: string | null; updatedAt?: string | null };

/**
 * What a full-page edit form's set-up effect keys on (0.29.2): the identity and version of the records it is set up from, in order.
 * The form re-initialises (setValues + resetDirty) only when this changes: on first load, for another record, or for a newer version
 * after a save. A refetch of the same version, such as the one the language switch makes (the record comes back as a new object with
 * names in the new language), leaves what the person typed alone.
 */
export function formKey(...records: (Versioned | null | undefined)[]): string {
  return records.map((r) => (r ? `${r.id}@${r.rowVersion ?? r.updatedAt ?? ''}` : '-')).join('|');
}

/** Whether a form set up from <c>prev</c> must re-initialise for <c>next</c>. */
export function shouldInitForm(prev: Versioned | null | undefined, next: Versioned | null | undefined): boolean {
  return formKey(prev) !== formKey(next);
}
