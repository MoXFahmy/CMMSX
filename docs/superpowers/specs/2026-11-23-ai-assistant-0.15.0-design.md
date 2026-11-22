# CMX 0.15.0 — AI assistant (design)

Roadmap release 0.15.0 (DimoMaint plan, version 5). Medium. Builds on 0.14.0 (symptom / cause / remedy codes give the AI
clean history) and the 0.11.0 phone app (offline completion, parts on a job).

## Intent and why

Technicians skip typing on a phone, so reports come back thin. Dictating in Arabic or English, and letting AI turn the
words into the report's fields, saves time and improves the data. A diagnosis drawn from the asset's own past work, with
the work orders it relied on, helps the person in front of a fault. Because cworks pays for the AI, every use must be
counted and capped.

## Decided with the user

- **One CMX Claude key**, held on AWS like the push-notification key; cworks pays. Organizations only switch AI on.
- **One monthly ceiling for every organization**, set on AWS (default 300 AI actions a month). Each organization's admin
  can set a lower limit for itself, never a higher one.
- **Diagnosis: the server picks the history, Claude answers once** in a fixed structure (Approaches A).
- **Speech: the phone's own speech recognition**; audio never leaves the phone.
- **Waiting: on screen with progress** (no background jobs).
- **Model: Claude Opus 5.5** for both features (light effort for reports, deeper for diagnoses); an AI action is one
  report or one diagnosis, "Run again" included; the web completion dialog also gets "Fill from notes".

## Goals

1. **Turning it on.** Settings › Organization › **AI assistant** (`settings.manage`): *Use the AI assistant* (off by
   default) and *Monthly limit* (empty = the ceiling; a higher number is refused: "The limit can be at most {0} a month.").
   When the server has no key, the section says AI isn't set up on this server and the switch can't be turned on. One line
   says what is sent and that Anthropic doesn't train on it (below).
2. **AI report — phone.** On the completion sheet:
   - A **microphone** next to *Work done* dictates in the person's app language (Arabic or English) and adds the text;
     typing works as before.
   - **Fill with AI** (needs a connection) sends the notes and gets back suggestions: the work done (cleaned up), cause
     details, symptom, cause and remedy (codes from the organization's active lists only), the technician's hours, and
     parts used (only from the job's planned parts and the asset's spare parts list), each with a quantity.
   - Suggestions fill the sheet, marked *From AI — check before completing*. Hours become *Log {0} h for me* and each part
     *Use {0} × {1}*, as ticked boxes; on completion the ticked ones are sent as the usual hours and part changes (so
     stock rules, offline sending and conflicts work as today). Nothing is saved until the technician completes.
   - Offline, the notes stay on the sheet and *Fill with AI* says it needs a connection; completing never depends on AI.
3. **AI report — web.** The completion dialog's **Fill from notes** sends what is typed in *Work done* and fills work
   done, cause details and the three codes; suggested hours and parts are listed for the person to add themselves.
4. **AI diagnosis — web.** **Diagnose** on a work order (`workorders.execute`) and on a request (`requests.manage`), when
   it has an asset.
   - The server gathers the record (title, description, symptom, asset and its category, manufacturer, model) and past
     work: completed work orders on the asset first, then on assets with the same category (same manufacturer and model
     first), newest first, at most 40, each with its codes, work done, cause details, parts used and labour hours —
     only work the person may see (site access applies).
   - Claude answers in a fixed structure: what is known, what was seen, the probable cause, and up to five actions ranked
     by relevance, each with why, parts (from the parts in that history) with quantities, estimated hours, and the work
     orders it relied on. With fewer than three related past work orders it says the history is too thin instead of
     guessing.
   - Work order references are kept only if they were among those sent; parts only if they were in that history. The
     result links each source work order.
   - Each diagnosis is kept on its work order or request with who ran it and when; *Earlier diagnoses* lists them (a work
     order made from a request also lists the request's).
     **Run again** makes a new one. Each person can give a diagnosis a thumbs up or down; the counts show.
   - **Add to planned parts** (work orders only, `workorders.edit`) copies an action's parts onto the work order's planned
     parts with the existing planned-parts rules.
5. **Limits.** Every call that reaches Claude counts as one action, a refusal included; a call that fails before an
   answer doesn't. When the month's actions (organization time zone) reach the organization's limit, AI buttons answer
   "This month's AI allowance ({0}) is used up. It renews on {1}." The check and the log entry are taken under a lock per
   organization, so two calls at the same moment can't go over the limit. A limit of 0 means no AI actions; lowering the
   ceiling applies at once.
6. **Usage.** Settings › **AI usage** (`settings.manage`): this month's actions against the limit; the last 12 months
   (actions and estimated cost); this month by feature and by person; and a log of every call (when, who, feature,
   record, model, tokens, estimated cost in US dollars, result: answered / refused / failed / over the limit) with filters
   and export. The estimate uses the price table in the server settings (Claude Opus 5.5: $4 / $20 per million input /
   output tokens, cache reads $0.20).
7. **Deployment.** The key is an AWS Secrets Manager secret wired like the push key (a CloudFormation parameter and a
   GitHub variable `CMMS_ANTHROPIC_SECRET_ARN`, read as `Ai__ApiKey`); the ceiling is `Ai__MonthlyCeiling` (default 300);
   the model is `Ai__Model` (default `claude-opus-5-5`). The user creates the secret with their own key; nobody types the
   key anywhere else.

## Data and privacy

- Nothing is sent unless someone presses an AI button with the switch on.
- What is sent: the work's texts (title, description, notes), codes, part names and numbers, quantities, hours, asset
  category, manufacturer and model. Never people's names, e-mail addresses or phone numbers, and no photos or files.
- Anthropic's terms for the API (privacy.claude.com, read 23 Nov 2026): inputs and outputs aren't used to train models
  and are deleted within 30 days, except when flagged for a usage-policy breach or required by law. The thumbs up / down
  in CMX stay in CMX; nothing is sent to Anthropic as feedback.

## Non-goals

- Allowances per organization, a cworks-only admin screen, billing for AI.
- Diagnosis on the phone; dictation in the web app; audio sent to any server; transcription services.
- A search index of past work (embeddings) — Approaches C, later if histories outgrow 40 work orders.
- AI anywhere else (PM suggestions, request triage, chat).
- Zero-data-retention arrangements with Anthropic.

## Anti-goals

- AI saving anything a person didn't confirm.
- A diagnosis citing a work order that doesn't exist, belongs to another organization, or the person can't see.
- A code or part the organization doesn't have, filled in by AI.
- Any AI call while the switch is off, without a key, or over the limit; a loop or retry that burns the allowance.
- Completing work blocked or slowed because AI is off, out of allowance, offline or failing.
- Personal data in a prompt.

## Constraints

- House rules: tenant and site filters on every query; Arabic for every new text (web locales, server `ar.json`); the
  same checks on the web and the phone; tests written first; one migration with new tables and nullable columns only.
- Tests never call Claude: the server talks to Claude through one interface with a fake in tests; a manual check with the
  real key happens after deployment.
- The official Anthropic C# SDK (`Anthropic` NuGet package), structured outputs for the answers, prompt caching for the
  fixed instructions and the organization's code lists, the server-side refusal fallback, a 60-second limit per call.
- The phone keeps working on 0.14 servers (AI buttons hidden when the bundle doesn't say AI is on).

## Approaches

**Diagnosis.** (A) The server picks up to 40 related past work orders and Claude answers once in a fixed structure; (B)
Claude looks things up itself step by step with tools; (C) a search index of all past work. B is slower, costlier and
harder to cap; C needs a second provider. **Chosen: A.**

**Speech.** The phone's own speech recognition (free, Arabic and English, offline with the language pack) against sending
audio to a transcription service (another provider and cost; Claude doesn't transcribe). **Chosen: the phone's own.**

**Waiting.** On screen with progress against a background job with a notification; a report takes seconds and a
diagnosis about 10–30 seconds. **Chosen: on screen.**

## Decided details

- **Report answer**: `workDone`, `causeDetails`, `symptomId`, `causeId`, `remedyId`, `hours`, `parts[{partId, quantity}]`
  — every id checked against what was offered and dropped otherwise; hours 0–24; quantities above 0. The AI writes in the
  language of the notes.
- **Diagnosis answer**: `context`, `observed`, `probableCause` (with a confidence of low / medium / high), `actions[{title,
  why, parts[{partId, quantity}], hours, sources[workOrderId]}]` (at most 5), `historyTooThin` and its message. Stored as
  JSON with the work order ids and part ids already checked.
- **Records**: `AiCall` (tenant, user and name, feature, work order or request, model, input / output / cache tokens,
  estimated cost, result, duration, time) and `AiDiagnosis` (work order or request, asset, the answer, the sources, who,
  when) with `AiDiagnosisVote` (diagnosis, user, up or down; one per person, changeable).
- **Calls**: one retry at most on a network or server error (the SDK's default is two); a refusal isn't retried by CMX
  (the server-side fallback handles it).
- **Notes length**: dictated or typed notes up to 8,000 characters; each past work order's texts cut to 1,000 characters
  in the prompt.
- **Phone**: a speech-recognition Capacitor plugin with the microphone permission; the bundle carries `ai: { enabled }`.
  If no plugin works with the app's Capacitor version, the microphone is left out and *Fill with AI* works on typed notes
  (a ruling the builder records).

## Left to the builder

- The prompt wording (stable instructions first for caching), the JSON schemas' exact shape, the screens' layout.
- How the usage log is paged and exported (the list toolkit's way).
- Choosing the speech plugin and its version.
