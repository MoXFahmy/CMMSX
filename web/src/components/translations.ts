/** The lists "Fill in missing Arabic" works on (0.28.0), as the server names them. */
export type TranslationList =
  | 'assets' | 'locations' | 'parts' | 'categories' | 'symptoms' | 'causes' | 'remedies' | 'cancel-reasons' | 'request-types' | 'templates' | 'pm-schedules';

/** One batch of AI drafts (POST /api/translations/draft). */
export type TranslationDraft = {
  field: string | null;
  items: { id: string; text: string; draft: string }[];
  couldNot: number;
  remaining: number;
  /** The texts of this batch with no draft (passed back as `skip` to go on without them). */
  couldNotIds: string[];
  /** Missing texts the skip list left out (0.29.2). */
  skippedLeft: number;
};

/** A row of the review table: the main text, the Arabic draft as edited, and whether to keep it. */
export type ReviewRow = { id: string; text: string; ar: string; keep: boolean };

/** Every drafted row, ticked. */
export function toRows(draft: TranslationDraft): ReviewRow[] {
  return draft.items.map((i) => ({ id: i.id, text: i.text, ar: i.draft, keep: true }));
}

/** The save request: ticked rows with a non-blank Arabic text, trimmed. */
export function toSaveBody(list: TranslationList, field: string, rows: ReviewRow[]) {
  return {
    list, field,
    items: rows.filter((r) => r.keep && r.ar.trim().length > 0).map((r) => ({ id: r.id, ar: r.ar.trim() })),
  };
}

/** The ids of the rows the person unticked: they are not asked for again while the window stays open (0.29.2). */
export function passedIds(rows: ReviewRow[]): string[] {
  return rows.filter((r) => !r.keep).map((r) => r.id);
}

/** The rows a save leaves behind: unticked, or ticked with a blank Arabic text. */
function unsavedIds(rows: ReviewRow[]): string[] {
  return rows.filter((r) => !r.keep || r.ar.trim().length === 0).map((r) => r.id);
}

/**
 * What the window does when the person saves or skips a batch (0.29.2): close when nothing was left to draft; otherwise save the
 * kept rows (none: a skip, no request) and ask for the next batch without the texts passed on so far (the earlier passes, this
 * batch's unticked or blank rows and the ones the assistant couldn't draft). It always asks: a field's last batch goes on to the
 * list's next field, and when nothing is left the server says so without asking the assistant.
 */
export type NextStep = { kind: 'close' } | { kind: 'next'; save: ReturnType<typeof toSaveBody> | null; skip: string[] };

export function nextStep(list: TranslationList, draft: TranslationDraft | null, rows: ReviewRow[], passed: string[]): NextStep {
  if (!draft?.field) return { kind: 'close' };
  const body = toSaveBody(list, draft.field, rows);
  return {
    kind: 'next',
    save: body.items.length > 0 ? body : null,
    skip: [...new Set([...passed, ...unsavedIds(rows), ...draft.couldNotIds])],
  };
}

/** The review's main button: save (and draft the next batch while this field has more), or skip when no row is kept with a text. */
export function primaryAction(rows: ReviewRow[], draft: TranslationDraft | null): 'save' | 'saveNext' | 'skip' {
  if (!rows.some((r) => r.keep && r.ar.trim().length > 0)) return 'skip';
  return draft && draft.remaining > 0 ? 'saveNext' : 'save';
}

/** What the window shows: everything done, a batch the AI drafted nothing for, or drafts to review. */
export function draftState(draft: TranslationDraft): 'done' | 'nothing' | 'review' {
  if (draft.items.length > 0) return 'review';
  return draft.field === null ? 'done' : 'nothing';
}
