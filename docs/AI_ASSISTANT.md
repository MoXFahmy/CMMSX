# AI assistant (0.15.0)

CMX can use Claude (Anthropic's AI) in two places: turning a technician's notes into the completion report, and diagnosing
a fault from the asset's own history. cworks holds one Claude key for the server and pays for it, so every use is counted
and capped.

## Turning it on

Settings › Organization › **AI assistant** (needs *Manage settings*):

- **Use the AI assistant** — off by default. While the server has no key, the section says *AI isn't set up on this
  server* and the switch can't be turned on.
- **Monthly limit** — AI actions a month for this organization. Empty means the server's ceiling (300 unless the server
  says otherwise); a higher number is refused. 0 means no AI.

One AI action is one press of *Fill with AI*, *Fill from notes*, *Diagnose* or *Run again* that reaches Claude, a refusal
included. A call that fails before Claude answers doesn't count. The month runs in the organization's time zone; when it
is used up, the buttons answer *This month's AI allowance (N) is used up. It renews on …*.

## AI report

**Phone.** On the completion sheet:

- The **microphone** next to *Work done* dictates in the app's language (Arabic or English) with the phone's own speech
  recognition; the audio never leaves the phone. Typing works as before. The microphone shows only in the Android app
  0.15.0 and later.
- **Fill with AI** (needs a connection) sends the notes and fills *Work done*, *Cause details*, the symptom, cause and
  remedy, marked *From AI — check before completing*. Codes come only from the organization's active lists.
- Suggested hours become a ticked **Log N h for me** and each suggested part a ticked **Use N × part** (parts come only from
  the job's planned parts and the asset's spare parts list; a part shows as a plain line when the person may not issue
  stock or there is no store for it). On completion the ticked ones are sent as the usual hours and part changes, so stock
  rules, offline sending and conflicts work as for any other change. Nothing is saved until the technician completes.
- Offline, the notes stay on the sheet and *Fill with AI* says it needs a connection. Completing never depends on AI.

**Web.** The completion dialog's **Fill from notes** sends what is typed in *Work done* and fills work done, cause details
and the three codes (a symptom field joins the dialog). Suggested hours and parts are listed for the person to add
themselves.

## AI diagnosis

**Diagnose** shows on a work order (needs *Execute work orders*) and in a request's panel (needs *Manage requests*) when the
record has an asset.

- The server picks the history: completed work orders on the asset first, then on assets of the same category (same
  manufacturer and model first), newest first, at most 40 — only work the person may see (site access applies). With fewer
  than three, CMX says the history is too thin without asking Claude (nothing is counted).
- Claude answers once: what is known, what was seen, the probable cause with a confidence (low, medium, high), and up to
  five actions ranked by relevance, each with why, parts (only parts used in that history) with quantities, estimated hours
  and the work orders it relied on. Work orders it cites are kept only if they were sent, and link to the work order.
- Each diagnosis is kept with who ran it and when; **Earlier diagnoses** lists older ones (a work order made from a request
  also lists the request's). **Run again** makes a new one. Everyone can give a thumbs up or down (one each, changeable);
  the counts show. These votes stay in CMX.
- **Add to planned parts** (work orders, needs *Edit work orders*) copies an action's parts onto the work order's planned
  parts with the usual rules; parts already planned are skipped.

## Usage

Settings › **AI usage** (needs *Manage settings*): this month's actions against the limit and the renewal date; the last 12
months (actions, and estimated cost in the tooltip); this month by feature and by person; and the call log — when, who,
feature, record, model, tokens, estimated cost in US dollars and result (answered, refused, failed, over the limit) — with
filters and export to Excel or CSV. The estimate uses the server's price table (Claude Opus 5.5: $4 / $20 per million input
/ output tokens, cache reads $0.20, cache writes $5).

## What is sent

Nothing is sent unless someone presses an AI button with the switch on. What is sent: the work's texts (title,
description, notes), codes, part names and numbers, quantities, hours, and the asset's category, manufacturer and model.
Never people's names, e-mail addresses or phone numbers, and no photos or files.

Anthropic's terms for the API (privacy.claude.com, read 23 Nov 2026): inputs and outputs aren't used to train models and
are deleted within 30 days, except when flagged for a usage-policy breach or required by law.

## Server settings

| Setting | Default | What it does |
| --- | --- | --- |
| `Ai__ApiKey` | — | The Claude API key. Without it AI is off everywhere. |
| `Ai__MonthlyCeiling` | 300 | The most AI actions a month any organization may use. Lowering it applies at once. |
| `Ai__Model` | `claude-opus-5-5` | The Claude model for both features. |
| `Ai__InputPrice`, `Ai__OutputPrice`, `Ai__CacheReadPrice`, `Ai__CacheWritePrice` | 4, 20, 0.2, 5 | US dollars per million tokens, for the usage estimate. |
| `Ai__TimeoutSeconds` | 45 | How long one call may take. Keep it under CloudFront's 60-second origin limit. |

Calls retry once at most on a network or server error; a refusal is re-served by Anthropic's default fallback model on
the same call.

### Setting up the key on AWS

The key lives in AWS Secrets Manager, like the push-notification key. You create the secret yourself; nobody types the key
anywhere else.

1. In the AWS console, region **eu-west-2**: Secrets Manager › *Store a new secret* › *Other type of secret* ›
   **Plaintext**, and paste your Claude API key as the whole value. Name it, for example, `cmms/anthropic-key`.
2. Copy the secret's ARN.
3. In the GitHub repository: Settings › Secrets and variables › Actions › **Variables** › add `CMMS_ANTHROPIC_SECRET_ARN`
   with that ARN.
4. Deploy again (push to main or run the deploy workflow). The stack gives the app read access to that secret only and
   hands it the key as `Ai__ApiKey`.
5. In CMX, Settings › Organization › AI assistant now offers the switch. Try one *Fill from notes* and check Settings › AI
   usage shows the call.

To change the ceiling, update the stack's `AiMonthlyCeiling` parameter (default 300).
