# 0.28.0 — Bilingual data

Roadmap item from the DimoMaint comparison ("data translation"), the last of the four releases the roadmap's 0.25.0 was split
into: optional Arabic and English texts for checklist steps, asset names and list values; each person sees their own language
on screen, on the phone and in prints. Picked in the conversation (2026-11-24): **an Arabic text beside each text, chosen on the
server**; Arabic texts are filled **by hand, through Excel, and from AI drafts**.

## Why

CMX's screens already speak English and Arabic (each person picks one; without a choice, the organization's default), but the
data people set up has one text per field. In the organizations CMX serves, engineers and managers set CMX up in English and
read it in English, while technicians read Arabic. Today a technician on the phone sees "Check coupling alignment" on a checklist
step and "Chiller pump 2" as the asset, in English, in an Arabic app.

Success looks like: an engineer downloads the current work templates and steps from Excel, or presses "Fill in missing Arabic"
and checks the AI's drafts; from then on the technician's phone, in Arabic, shows "مضخة التبريد 2" and each step in Arabic, while
the manager's reports, job cards and e-mails, in English, still show "Chiller pump 2" and the English steps. Where an Arabic text
is missing, everyone sees the main text, never a blank.

## Goals

1. **An optional Arabic text** beside the main text of the records a technician reads while working (the list below).
2. **Each person sees their own language** wherever CMX shows those texts: web screens, the phone (online and its stored copy),
   prints and labels, reports and their Excel exports, e-mails and push messages. Arabic readers see the Arabic text when there is
   one, else the main text; English readers see the main text.
3. **Work orders carry both texts** of their steps and of a title taken from a PM or a template, copied when the work order is
   made, so each technician reads them in their language.
4. **Search finds a record by either text.**
5. **Excel:** an Arabic column beside each of these texts on the import sheets and in the current-data download, so an engineer
   can download, have the column filled, and import it back.
6. **AI drafts:** a "Fill in missing Arabic" action that drafts the missing Arabic texts in batches for a person to check and save,
   when the organization has the AI assistant switched on.

### The records and texts in scope

| Record | Text(s) that get an Arabic twin | Edited where |
|---|---|---|
| Asset | Name, Notes to technician | asset form |
| Location | Name, Notes to technician | location form |
| Part | Name | part form |
| Asset category | Name | Settings |
| Symptom, cause (failure code), remedy | Name | Settings › codes |
| Request type | Name | Settings |
| Cancel reason | Name | Settings |
| Work template (PM task) | Name, Title, Safety notes | template form |
| Work template step (PM task step) | Description | template form, steps |
| PM schedule | Name | PM form |
| Work order | Title (copied from the PM or template; editable), Safety notes (copied from the template) | work order form |
| Work order step | Description (copied from the template step; editable) | work order steps |

Work types, priorities, asset statuses and criticality already have their Arabic wording (0.21.0 list labels). Units of measure
already have an Arabic name (0.21.0) and are left as they are.

## Non-goals

- **Text people type as they work** stays as written: request descriptions, comments, remarks, completion notes, step answers,
  and titles of work orders typed by hand (a hand-typed title has one text; its Arabic field stays empty unless someone fills it).
- **Purchasing and suppliers:** purchase orders, requests, supplier e-mails and purchase papers keep the part's main name; suppliers
  are outside the organization and managers read these.
- **Other records** keep one text: sites, stores, trades, teams, cost centres, brands and models, asset groups, measurement points,
  people, contracts, custom-field values, and the other descriptions and notes on assets, locations, parts and templates (of
  their notes, only "Notes to technician" and a template's "Safety notes" are in scope: engineers write them for technicians).
- **More languages.** Two languages only, as on the screens; CMX adds a language when a customer needs one.
- **An English twin for organizations that write their main texts in Arabic.** The main text is whatever people type today
  (English in the organizations that asked for this); an organization that types Arabic main texts simply leaves the Arabic field
  empty.
- **AI drafts from Arabic to English,** and AI translation of free text (requests, comments).
- **The 4CSQL importer** is unchanged.
- **Marking AI drafts** once saved: a saved draft is a normal Arabic text, editable like any other.

## Anti-goals

- **An Arabic text must never be saved into a main text.** A screen that shows names in the reader's language must not feed them
  back into an edit form: if a form misses this release's change, it shows and saves the main text, and Arabic readers merely see
  English there. Data must never be changed by someone simply reading in Arabic.
- **No blanks:** a missing Arabic text falls back to the main text everywhere, including prints, e-mails and the phone.
- **AI never overwrites** an Arabic text that exists, and nothing the AI drafts is saved without a person pressing Save.
- **No new leaks:** the extra lookups this needs (location paths, names in reports) keep the organization and site filters; the
  0.26.1 lesson applies (no `AllSites()` or `IgnoreQueryFilters` inside a filtered query; look names up in a second query).
- **No slower lists:** choosing a name by language happens in the same query (a `CASE` in SQL), not by loading rows twice.

## Approaches (picked by the user, 2026-11-24)

1. **An Arabic field beside each text, chosen on the server — picked.** Each text in scope gets an optional `…Ar` twin (as
   units of measure, list labels and print layouts already have). The server knows each person's language (the app sends
   `Accept-Language`; background work uses `Translator.Use`), so it sends names in that language; screens, phone, prints,
   reports and Excel follow without each being changed. Costs: touching the server code that sends these names (about 40 places
   for asset names, fewer for the others).
2. **One translations table** (record kind, id, field, language, text), DimoMaint's way. A third language costs nothing, but every
   list, search, sort and report joins it: slower and more fragile with CMX's organization and site filters. Rejected: the
   flexibility serves a language CMX doesn't plan.
3. **Both texts to the client; each screen chooses.** Less server work, but every screen, the phone's stored copy and each print
   change one by one, and reports and Excel are made on the server anyway. Rejected.

## The design

### 1. Storing the texts

- Each text in the table above gets a nullable Arabic twin of the same maximum length: `NameAr`, `TitleAr`, `DescriptionAr`,
  `NotesToTechnicianAr`, `SafetyNotesAr` (one migration). Blank input is stored as null.
- Every form that edits one of these texts shows the Arabic field beside it (right-to-left, labelled "Name (Arabic)" and so on),
  as the units and lists settings already do. No automatic clearing: editing the main text leaves the Arabic text as it is; both
  are on the form for the editor to see.
- Changes to Arabic texts are recorded in the record's history and the activity log like any other field.

### 2. Choosing the text by language

- One small helper picks the text: Arabic reader and a non-blank Arabic text → the Arabic text; otherwise the main text. In
  database queries it becomes a `CASE` expression in the projection, so lists keep one query.
- **Where it applies:** every name or text from the table that the server sends for display, on every endpoint: lists, detail
  pages' references to other records (the asset and location on a work order, the category on an asset, the part on a stock
  line), pickers, the phone's bundle and pickers, location paths (built from names), reports and report Excel files, the
  current-data and problem-row Excel downloads (main column stays the main text; see 5), prints and labels, e-mails and push
  messages, the AI assistant's own pages (names shown, not prompts), and API answers for API keys (by `Accept-Language`).
- **A record's own edit data stays raw:** an endpoint that feeds an edit form sends the stored main text and the Arabic text as
  they are, never the chosen one (anti-goal 1). Where a record's own list or page shows its name, it shows the chosen text from
  a separate field (naming left to the builder), so no form can pick it up by mistake.
- **Sorting** by name sorts by the text the reader sees.
- **Search** that matches a name also matches its Arabic twin (asset, location, part, template and PM searches; pickers; the
  phone's search over its stored copy, which therefore keeps both texts for searching).
- **E-mails** are written in each recipient's language (as today); names go in as the existing `LocalWords` pair, so each
  recipient reads their own. Push messages are built under the recipient's language (as today) and pick names the same way.
- **Background and server-made texts** (PM generation, notifications, scheduled e-mails) have no request language: they store
  both texts (work orders, section 3) or pick at the moment of sending under the recipient's language.

### 3. Work orders

- When a work order is made from a PM or a template (PM generation, "new from template", follow-up work from a failed step,
  requests turned into work with a template), its title's Arabic twin is copied from the same source as its title (the template's
  Arabic title, else its Arabic name; PM work: the PM's Arabic name, as PM work keeps the PM's name today), and each step's
  Arabic text is copied from its template step.
- Work orders made before an Arabic text existed keep what they had; changing a template later doesn't change existing work
  orders (as for the main texts today).
- The work order form shows the Arabic title beside the title; the steps editor shows each step's Arabic text beside it. Steps
  added by hand may have one.
- Round work orders (one step per asset) copy the same way.
- **Safety notes get their own place on the work order.** Today PM generation and grouped work append the template's safety
  notes to the work order's description ("Safety: …"), which is one text. From this release a work order made from a template
  copies the template's safety notes and their Arabic twin into its own `SafetyNotes` / `SafetyNotesAr`, and no longer appends
  them to the description. The work order page, the phone's job page and the job card show them as a "Safety" section in the
  reader's language (on the job card, inside the block that prints the description today, so existing print layouts keep
  showing them). Work orders made before keep their description as it is.
- Notes to technician are read live from the asset and the location (as today), in the reader's language.

### 4. The phone

- The phone shows names, titles and steps in its language from its stored copy. The stored copy keeps both texts where search
  needs them (2), so changing the language never needs data the phone doesn't have; switching language (online only, as today)
  refreshes the stored copy so everything shown is in the new language.
- Offline answers and changes are unaffected.

### 5. Excel

- Each import sheet with one of these texts gets an Arabic column right after it: Locations, Categories, Symptoms, Causes,
  Remedies, Assets, Parts ("Name (Arabic)" / "الاسم (عربي)"), Work templates ("Name (Arabic)", "Title (Arabic)", "Safety notes
  (Arabic)"), PM steps ("Description (Arabic)"), PM schedules ("Name (Arabic)"); Assets and Locations also "Notes to technician
  (Arabic)". Keys `nameAr`, `titleAr`, `descriptionAr`, `safetyNotesAr`, `notesToTechnicianAr`.
- The empty template and the current-data download include these columns; the current-data download fills them.
- As on every sheet: a blank cell keeps what's there; a value replaces it. Same length limits as the main text.
- Request types and cancel reasons have no import sheet today and get none; they are edited in Settings.

### 6. AI drafts

- **Where:** a "Fill in missing Arabic" button on the pages that list these records: assets, locations, parts, work templates
  (their names, titles, safety notes and steps), PM schedules, and the Settings lists (categories, symptoms, causes, remedies, request types,
  cancel reasons). Shown to people who can edit that list, when the AI assistant is on for the organization; with no allowance
  left it says so (the assistant's existing message) instead of drafting.
- **What one press does:** takes the next batch of texts in that list with a main text and no Arabic text, among the records the
  person can see there (the whole list, not the current filter; in code or number order): up to 50 names or titles, up to 100
  step texts across one or more templates, or up to 20 notes (notes to technician, safety notes); sends their main texts to Claude, and opens a review table: main text,
  Arabic draft (editable), and a tick to keep (all ticked). "Save" writes the ticked ones; "Draft the next batch" moves on. A text
  that gained an Arabic text since the draft (someone else saved one) is skipped on Save, never overwritten.
- **Consistency:** the request includes, as cached shared context, up to 200 of the organization's existing main/Arabic pairs
  for these records (most recently changed first), so terms stay consistent ("Chiller" is always the same word). Instructions
  tell Claude to translate for maintenance technicians in Egypt, keep codes, numbers, units, model numbers and brand names as
  they are, and answer with one Arabic text per input id (JSON schema).
- **Counting:** one press is one AI use against the organization's monthly allowance, logged in the AI call log as a new feature
  "Translation" (`AiFeature.Translation`), like reports and diagnoses. A refusal or an answer missing some ids drafts what it can
  and says how many it couldn't.
- Nothing is saved by the AI itself; the server's save endpoint for drafts is the same validation as the forms.

## Who may do what

- Arabic texts follow the permission that edits the record today (the asset form's permission for asset names, Settings
  permissions for the Settings lists, template and PM permissions for those, work order edit for work order titles and steps).
- "Fill in missing Arabic" needs that same edit permission plus the AI assistant being on; saving drafts needs the edit
  permission.
- Reading in either language needs nothing new.

## Arabic for new texts

Every new label, button, message, column heading and e-mail word has its Arabic wording, as in every release (web locales,
server `ar.json`, import column names).

## Testing

Server (tests first, against the real database as usual):

- A list, a detail reference, a picker and a report return the Arabic text for `Accept-Language: ar` and the main text for `en`;
  a blank Arabic text falls back to the main text.
- A record's own edit data returns the stored main and Arabic texts unchanged under `ar` (anti-goal 1), and saving it under `ar`
  leaves the main text unchanged.
- Search by the Arabic text finds the record; sorting under `ar` follows the Arabic texts.
- PM generation and "new from template" copy the Arabic title and step texts; a template changed afterwards doesn't change the
  work order.
- A work order made from a template with safety notes has them in its own safety fields (both texts) and not in its description;
  the phone job and the job card show them in the reader's language; older work orders are unchanged.
- Notes to technician on the phone job and place pages follow the phone's language.
- Location paths read in Arabic for Arabic readers and keep the site filters (a site-limited person sees no other site's names).
- The phone bundle carries both texts and shows the phone's language.
- Import: the Arabic columns fill, a blank cell keeps the current Arabic text, the current-data download includes them.
- E-mail and push to an Arabic recipient use the Arabic names; to an English recipient the main names.
- AI drafts with the fake AI client: only records missing Arabic are sent, at most the batch size; Save skips texts that gained
  an Arabic text meanwhile; refused when the assistant is off or the allowance is used up; one call logged as Translation;
  permission checks.

Web (Vitest): the Arabic fields on forms, the review table (edit, untick, save, next batch), and the language switch refreshing
the phone's stored copy.

## My calls (for the user to check)

- Purchase orders, purchase papers and supplier e-mails keep the part's main name.
- Sites, stores, trades, teams, cost centres, brands, asset groups and measurement points stay one text.
- No automatic clearing: changing a main text leaves its Arabic text; both are on the form.
- Search matches either text; sorting follows the text the reader sees.
- Request types and cancel reasons get no import sheet (edited in Settings, with AI drafts there).
- AI batches: 50 names or titles, 100 steps, or 20 notes per press, over the whole list the person can see, with up to 200 of the
  organization's existing pairs sent for consistent wording.
- Safety notes move out of new work orders' descriptions into their own fields, shown as a "Safety" section.
- Labels and prints follow the language of the person printing.
- Switching the phone's language refreshes its stored copy.

## Left to the builder

- Field names in API answers for the chosen text versus the stored pair, as long as anti-goal 1 holds.
- The exact helper shape (a static method plus an EF-translatable expression, or similar).
- Batch sizes within the limits above, the review table's layout, and where the button sits on each page.
- Which existing tests need their expected payloads updated.
- docs/API.md: say that names follow `Accept-Language` and list the new Arabic fields.
