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
 * A text passed on while the window is open (0.29.2), sent back as `skip`: its field and its record. Per field, because one record has
 * several texts (an asset's name and notes to technician, a template's name, title and safety notes): a record passed on for one field
 * is still offered for the next.
 */
export type Skipped = { field: string; id: string };

/** Each text once, in first-seen order. */
function unique(xs: Skipped[]): Skipped[] {
  const seen = new Set<string>();
  return xs.filter((x) => {
    const key = `${x.field}\n${x.id}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/**
 * What the window does when the person saves or skips a batch (0.29.2): close when nothing was left to draft; otherwise save the
 * kept rows (none: a skip, no request) and ask for the next batch without the texts passed on so far (the earlier passes, this
 * batch's unticked or blank rows and the ones the assistant couldn't draft, each under this batch's field). It always asks: a field's
 * last batch goes on to the list's next field, and when nothing is left the server says so without asking the assistant.
 */
export type NextStep = { kind: 'close' } | { kind: 'next'; save: ReturnType<typeof toSaveBody> | null; skip: Skipped[] };

export function nextStep(list: TranslationList, draft: TranslationDraft | null, rows: ReviewRow[], passed: Skipped[]): NextStep {
  if (!draft?.field) return { kind: 'close' };
  const field = draft.field;
  const body = toSaveBody(list, field, rows);
  return {
    kind: 'next',
    save: body.items.length > 0 ? body : null,
    skip: unique([...passed, ...[...unsavedIds(rows), ...draft.couldNotIds].map((id) => ({ field, id }))]),
  };
}

/** How many texts are left out so far ("N left out"): the earlier passes plus this batch's; a record left out on two fields counts twice. */
export function leftOutCount(list: TranslationList, draft: TranslationDraft | null, rows: ReviewRow[], passed: Skipped[]): number {
  const after = nextStep(list, draft, rows, passed);
  return after.kind === 'next' ? after.skip.length : passed.length;
}

/**
 * Runs a request for the window as it is now (0.29.2): `done` gets its answer and `failed` its error only while that window lasts.
 * Closing the window bumps `session`, so a late answer doesn't reopen it and a late failure doesn't show an error.
 */
export async function inSession<T>(session: { current: number }, request: () => Promise<T>, done: (value: T) => void, failed: (error: unknown) => void): Promise<void> {
  const mine = session.current;
  let value: T;
  try {
    value = await request();
  } catch (e) {
    if (session.current === mine) failed(e);
    return;
  }
  if (session.current === mine) done(value);
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
