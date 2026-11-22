# CMX 0.15.0 — AI assistant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Technicians dictate or type notes and AI fills the completion report; engineers get a diagnosis drawn from the asset's history; every AI call is counted against a monthly allowance and shown on a usage page.

**Architecture:** One server gateway (`IAiClient`) wraps the official Anthropic C# SDK; tests swap it for a fake. `AiService` checks the switch, the key and the allowance, reserves a call-log row under a per-organization SQL application lock, calls Claude outside the lock and finishes the row. Prompts name codes, parts and work orders by their codes and numbers; the server maps answers back to ids and drops anything it did not offer.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 / SQL Server, `Anthropic` NuGet (structured outputs, prompt caching, server-side fallback beta), React 19 + Mantine 8 + @mantine/charts, i18next, Capacitor 8 (Android), xUnit + Vitest.

**Spec:** `docs/superpowers/specs/2026-11-23-ai-assistant-0.15.0-design.md`

## Global Constraints

- House rules: tenant and site query filters on every query; Arabic for every new text (web `ar.json`, server `Localization/ar.json`); the same checks on the web and the phone; tests written first.
- One migration (`AiAssistant`): new tables only (switch and limit live in the `TenantSettings` JSON column).
- Tests never call Claude: everything goes through `IAiClient`; `FakeAiClient` replaces it in `ApiFixture`.
- Model `Ai:Model` default `claude-opus-5-5`; ceiling `Ai:MonthlyCeiling` default 300; key `Ai:ApiKey` (env `Ai__ApiKey`). Price table in config: input $4, output $20, cache read $0.20, cache write $5 per million tokens.
- Report effort low, diagnosis effort medium; one retry at most (`MaxRetries = 1`); 60-second limit per call; refusals are not retried by CMX (the server-side fallback beta handles them).
- Notes up to 8,000 characters; each past work order's texts cut to 1,000 characters in the prompt; at most 40 past work orders; at most 5 actions.
- Never in a prompt: people's names, e-mail addresses, phone numbers, photos or files.
- Exact copy: "The limit can be at most {0} a month." · "This month's AI allowance ({0}) is used up. It renews on {1}." · "From AI — check before completing" · "Log {0} h for me" · "Use {0} × {1}".
- Every call that reaches Claude counts (answered or refused); a failed call or one refused for the allowance doesn't. The month runs in the organization's time zone. Limit 0 = no AI. Effective limit = min(organization limit ?? ceiling, ceiling), computed at call time.
- The phone keeps working on 0.14 servers: AI controls show only when the bundle carries `ai.enabled = true`.
- Helper scripts in the scratchpad `bin` default to the 0.14.0 worktree: before Task 1, point their `REPO` default at `.worktrees/ai-assistant-0.15.0`.

## Review Focus

- Two AI presses at the same moment with one action left: exactly one reaches Claude, the other gets the allowance message (Task 2 test `Concurrent_calls_cannot_pass_the_limit`).
- Claude answers with a code, part or work order number that wasn't offered (made up, inactive, another organization's, or another site's): it is dropped, never saved or shown (Task 2 `Report_drops_codes_and_parts_not_offered`, Task 4 `Diagnosis_keeps_only_sources_and_parts_that_were_sent`).
- A call at 23:30 on the last day of the month in the organization's time zone counts in that month, and the renewal date is the local 1st (Task 2 `Month_follows_the_organization_time_zone`).
- Switch turned off, or limit lowered below this month's use, mid-month: the next press is refused at once and nothing reaches Claude (Task 2 `Lowered_limit_and_switch_off_refuse_at_once`).
- Claude times out or errors: the call isn't counted, the person gets a retry message, and completing still works offline and online (Task 2 `Failed_call_is_logged_but_not_counted`; Task 9 `aiChanges` tests prove completion payloads don't depend on a suggestion).

---

### Task 1: AI records, switch, limit and status

**Files:**
- Create: `server/FourC.Cmms.Core/Ai/Ai.cs`
- Modify: `server/FourC.Cmms.Core/Tenancy/Tenant.cs` (TenantSettings), the DbContext and its configuration (DbSets, filters, indexes) wherever the other `TenantEntity` sets are registered
- Create: `server/FourC.Cmms.Api/Features/Ai/AiOptions.cs`, `server/FourC.Cmms.Api/Features/Ai/AiClient.cs` (interface + records only in this task), `server/FourC.Cmms.Api/Features/Ai/AiEndpoints.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (options binding, `api.MapAiEndpoints()`), `server/FourC.Cmms.Api/Features/AdminEndpoints.cs:151` (settings PUT)
- Create: migration `AiAssistant` (via `wt-ef AiAssistant`)
- Test: `server/FourC.Cmms.Tests/AiSettingsTests.cs`, `server/FourC.Cmms.Tests/FakeAiClient.cs`; modify `server/FourC.Cmms.Tests/ApiTests.cs` (fixture registers the fake)

**Interfaces:**
- Produces (Core, namespace `FourC.Cmms.Core.Ai`):
  - `enum AiFeature { Report, Diagnosis }`; `enum AiResult { Pending, Answered, Refused, Failed, OverLimit }`
  - `class AiCall : TenantEntity { Guid? UserId; string UserName; AiFeature Feature; Guid? WorkOrderId; Guid? WorkRequestId; string Model; int InputTokens; int OutputTokens; int CacheReadTokens; int CacheWriteTokens; decimal EstimatedCost; AiResult Result; int DurationMs; }` (time = `CreatedAt`)
  - `class AiDiagnosis : TenantEntity { Guid? WorkOrderId; Guid? WorkRequestId; Guid AssetId; string AnswerJson; int SentCount; string CreatedByName; }`
  - `class AiDiagnosisVote : TenantEntity { Guid DiagnosisId; Guid UserId; bool Up; }` — unique index (DiagnosisId, UserId)
  - `TenantSettings.AiEnabled` (bool, default false), `TenantSettings.AiMonthlyLimit` (int?, null = the ceiling)
- Produces (Api, namespace `FourC.Cmms.Api.Features.Ai`):
  - `sealed class AiOptions { string? ApiKey; string Model = "claude-opus-5-5"; int MonthlyCeiling = 300; decimal InputPrice = 4m; decimal OutputPrice = 20m; decimal CacheReadPrice = 0.2m; decimal CacheWritePrice = 5m; int TimeoutSeconds = 60; }` bound from section `Ai`
  - `interface IAiClient { bool Configured { get; } Task<AiReply> AskAsync(AiAsk ask, CancellationToken ct); }`
  - `sealed record AiAsk(AiFeature Feature, string Instructions, string SharedContext, string Prompt, JsonObject Schema, AiEffort Effort, int MaxTokens)`; `enum AiEffort { Low, Medium }`
  - `sealed record AiReply(bool Refused, string? Json, string Model, int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens)`
  - `sealed class AiUnavailableException(string reason, Exception? inner) : Exception` — the client throws it for timeouts, network and API errors
  - `GET /api/ai/status` (any signed-in person) → `{ configured, enabled, ceiling, limit, used, renewsOn }` (`limit` = effective limit; `renewsOn` = local date `yyyy-MM-dd`)
- Produces (tests): `FakeAiClient : IAiClient` with `bool Configured = true`, `Func<AiAsk, Task<AiReply>> Responder`, `List<AiAsk> Asks`, `Reset()`; exposed on `ApiFixture` as `Ai`.

- [ ] **Step 1: Point the scratchpad helpers at this worktree and create the workspace ledger**

Run: `sed -i '' 's/failure-analysis-0.14.0/ai-assistant-0.15.0/' <scratchpad>/bin/wt-*` then `grep -l ai-assistant-0.15.0 <scratchpad>/bin/wt-*`
Expected: every `wt-*` script listed.

- [ ] **Step 2: Write the failing tests** in `AiSettingsTests.cs`

```csharp
[SkippableFact] Status_reports_ceiling_limit_and_use()
  // new org: configured true (fake), enabled false, ceiling 300, limit 300, used 0, renewsOn = 1st of next month (org tz)
[SkippableFact] Limit_above_ceiling_is_refused()
  // PUT settings with AiMonthlyLimit 301 → 400, problem detail "The limit can be at most 300 a month."
  // AiMonthlyLimit -1 → 400 "The limit can't be below 0."
  // AiMonthlyLimit 50 → 200, status.limit == 50
[SkippableFact] Switch_cannot_go_on_without_a_key()
  // fixture.Ai.Configured = false (restore in finally): PUT AiEnabled true → 400 "AI isn't set up on this server."
  // with Configured true: PUT AiEnabled true → 200, status.enabled true
[SkippableFact] Settings_save_keeps_working_when_the_key_is_removed_later()
  // enable with key; then Configured = false; PUT settings with AiEnabled still true and another field changed → 200
```

- [ ] **Step 3: Run them to verify they fail**

Run: `wt-test AiSettingsTests`
Expected: compile errors (no `AiMonthlyLimit`, no `fixture.Ai`).

- [ ] **Step 4: Implement** the entities, settings fields, DbContext registration (named tenant filter + soft delete like other `TenantEntity` sets), `AiOptions`, `IAiClient` and records, `FakeAiClient`, the fixture registration (`RemoveAll<IAiClient>(); AddSingleton<IAiClient>(Ai)`), `GET /api/ai/status`, and the settings PUT checks. Until Task 3 registers the real client, register a placeholder `NoAiClient` (`Configured => false`, `AskAsync` throws `AiUnavailableException`) in `Program.cs`. `used` counts this month's `Answered` + `Refused` rows plus `Pending` rows younger than 5 minutes. Month bounds come from `Settings.TimeZone` (`TimeZoneInfo.TryFindSystemTimeZoneById`, UTC fallback, as `ListExport` does). The "can't go on without a key" check applies only when the stored switch is off and the new one is on. Add the three new messages to `Localization/ar.json`.

- [ ] **Step 5: Generate the migration**

Run: `wt-ef AiAssistant` and inspect the migration
Expected: creates `AiCalls`, `AiDiagnoses`, `AiDiagnosisVotes` only; no other table altered.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `wt-test AiSettingsTests`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git add -A server docs
git commit -m "feat(ai): AI records, switch, monthly limit and status"
```

---

### Task 2: AI report endpoint and the monthly allowance

**Files:**
- Create: `server/FourC.Cmms.Api/Features/Ai/AiService.cs`, `server/FourC.Cmms.Api/Features/Ai/AiReport.cs`
- Modify: `server/FourC.Cmms.Api/Features/Ai/AiEndpoints.cs`, `server/FourC.Cmms.Api/Features/WorkOrderEndpoints.cs` (StatusInput, ChangeStatusAsync), `server/FourC.Cmms.Api/Features/TechActions.cs` (StatusPayload), `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/AiReportTests.cs`, `server/FourC.Cmms.Tests/AiAllowanceTests.cs`

**Interfaces:**
- Consumes: Task 1 entities, `IAiClient`, `AiOptions`, `FakeAiClient`.
- Produces:
  - `sealed class AiService` (scoped): `Task<AiStatus> StatusAsync(CancellationToken ct)` (Task 1's status endpoint moves onto it) and `Task<AiRun> RunAsync(AiFeature feature, Guid? workOrderId, Guid? workRequestId, AiAsk ask, CancellationToken ct)`.
  - `sealed record AiRun(AiReply Reply, Guid CallId)`; failures surface as `AiRefusedException(int status, string message)` → the endpoints return `Results.Problem(title: message, statusCode: status)`.
  - `POST /api/ai/report` (`workorders.execute`) body `{ workOrderId, notes }` → `AiReportResult { string WorkDone; string? CauseDetails; CodeRef? Symptom; CodeRef? Cause; CodeRef? Remedy; decimal? Hours; List<PartSuggestion> Parts }` with `record CodeRef(Guid Id, string Code, string Name)` and `record PartSuggestion(Guid PartId, string PartNumber, string Name, string? UnitOfMeasure, decimal Quantity)`.
  - `StatusInput.SymptomId` and `StatusPayload.SymptomId` (optional): when given on completion it is checked like the other codes (`anyState` from the phone) and set; null leaves the work order's symptom as it is.

**Allowance rules (RunAsync, in this order):**
1. `!client.Configured` → 409 "AI isn't set up on this server."; `!settings.AiEnabled` → 409 "The AI assistant is off for this organization."
2. In a transaction, take `sp_getapplock` (resource `ai:{tenantId}`, exclusive, owner Transaction, 10 s timeout); count `used` as in Task 1; if `used >= limit`, insert an `OverLimit` row and return 429 "This month's AI allowance ({0}) is used up. It renews on {1}." ({1} = local renewal date, `d MMM yyyy`); otherwise insert a `Pending` row; commit. (Wrap in the context's execution strategy if one is configured.)
3. Outside the lock, call `client.AskAsync` with a `TimeoutSeconds` cancellation; finish the row: `Answered` or `Refused` with tokens, model, duration and `EstimatedCost = (in*InputPrice + out*OutputPrice + cacheRead*CacheReadPrice + cacheWrite*CacheWritePrice) / 1_000_000`, rounded to 6 places; on `AiUnavailableException` or timeout, `Failed` with duration and 502 "The AI assistant didn't answer. Try again in a moment."
4. `UserName` is the signed-in person's display name at call time.

**Report prompt and answer:**
- Instructions (stable, first, cached): fill a maintenance completion report from the technician's notes; write in the notes' language; use only the codes and part numbers listed; leave a field empty when the notes don't support it; hours are the technician's own work.
- Shared context (cached): the organization's active symptoms, causes and remedies as `CODE — name` lines.
- Prompt: work order number, type, title, description, the asset's category, manufacturer and model, candidate parts (`PARTNO — name (unit)`: the work order's planned parts plus the asset's spare parts list), and the notes.
- Schema: `{ workDone: string, causeDetails: string|null, symptomCode: string|null, causeCode: string|null, remedyCode: string|null, hours: number|null, parts: [{ partNumber: string, quantity: number }] }`, all required, `additionalProperties: false`.
- Mapping: codes and part numbers not offered → dropped; hours outside (0, 24] → null; quantity ≤ 0 → dropped; repeated part numbers keep the first; `workDone`/`causeDetails` trimmed and cut to the completion fields' maximum lengths. Refused → 422 "The AI assistant couldn't help with these notes." Effort low, `MaxTokens` 4000.
- Notes empty → 400 "Type or dictate some notes first."; over 8,000 characters → 400 "Notes can be at most 8,000 characters."; work order not visible → 404.

- [ ] **Step 1: Write the failing tests** — `AiReportTests.cs`

```csharp
Report_fills_fields_from_the_answer()
  // enable AI; WO with asset (category, manufacturer, model), planned part P1, asset spare part P2, active codes SYX/CXA/RMX
  // fake returns {workDone:"Replaced seal", causeDetails:"Worn", symptomCode:"SYX", causeCode:"CXA", remedyCode:"RMX", hours:1.5, parts:[{partNumber:P1,quantity:2},{partNumber:P2,quantity:1}]}
  // → 200, Symptom.Code == "SYX", Hours == 1.5m, Parts.Count == 2 with names; one Answered AiCall with Feature Report, WorkOrderId set, EstimatedCost > 0
Report_drops_codes_and_parts_not_offered()
  // answer cites an inactive cause, another org's remedy code, an unlisted part number, quantity 0, hours 30 → those come back null/absent
Report_prompt_lists_offered_codes_and_parts_and_no_people()
  // fake.Asks[0]: SharedContext contains "SYX —"; Prompt contains P1 and P2 part numbers and the notes; Prompt contains neither the assignee's name nor e-mail
Report_validates_notes() // "" → 400 "Type or dictate some notes first."; 8001 chars → 400 "Notes can be at most 8,000 characters."
Report_refusal_counts_and_says_so() // fake returns Refused → 422 "The AI assistant couldn't help with these notes."; status.used == 1
Completion_sets_symptom_when_given() // POST status Completed with SymptomId → WO symptom set; without SymptomId → unchanged
```

`AiAllowanceTests.cs`

```csharp
Off_or_without_key_never_calls() // switch off → 409, fake.Asks empty; Configured false → 409
Allowance_is_enforced_with_the_message() // limit 2: two calls 200; third → 429 "This month's AI allowance (2) is used up. It renews on 1 Dec 2026."-style text; fake.Asks.Count == 2; OverLimit row logged
Concurrent_calls_cannot_pass_the_limit() // limit 1, fake delays 500 ms; two calls via Task.WhenAll → one 200, one 429; fake.Asks.Count == 1
Failed_call_is_logged_but_not_counted() // fake throws AiUnavailableException → 502 "The AI assistant didn't answer. Try again in a moment."; Failed row; status.used == 0
Lowered_limit_and_switch_off_refuse_at_once() // 3 used, limit lowered to 2 → 429; limit 0 → 429; switch off → 409
Month_follows_the_organization_time_zone() // org TimeZone "Africa/Cairo"; AiCall rows back-dated (DbAsync, IgnoreQueryFilters) to last day of previous month 23:30 local and 1st 00:30 local → only the second counts; renewsOn is the local 1st of next month
Stale_pending_rows_stop_counting() // Pending row 6 minutes old → not counted
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-test "AiReportTests|AiAllowanceTests"`
Expected: compile errors (no `/api/ai/report`, no `AiService`).

- [ ] **Step 3: Implement** `AiService`, `AiReport` (prompt, schema, mapping) and the endpoint, plus `SymptomId` on completion; add every new message to `Localization/ar.json`.

- [ ] **Step 4: Run them to verify they pass**

Run: `wt-test "AiReportTests|AiAllowanceTests|AiSettingsTests"`
Expected: all passed.

- [ ] **Step 5: Run the whole server suite** (completion changed)

Run: `wt-test`
Expected: all passed (rerun once on the known TemplatesModelTests cleanup flake).

- [ ] **Step 6: Commit**

```bash
git add -A server
git commit -m "feat(ai): AI report from notes with the monthly allowance and call log"
```

---

### Task 3: Anthropic client

**Files:**
- Modify: `server/FourC.Cmms.Api/FourC.Cmms.Api.csproj` (PackageReference `Anthropic`, current stable version)
- Create: `server/FourC.Cmms.Api/Features/Ai/AnthropicAiClient.cs`
- Modify: `server/FourC.Cmms.Api/Program.cs` (register `AnthropicAiClient` as the `IAiClient` singleton; remove `NoAiClient`)
- Test: `server/FourC.Cmms.Tests/AnthropicAiClientTests.cs`

**Interfaces:**
- Consumes: `IAiClient`, `AiAsk`, `AiReply`, `AiOptions`, `AiUnavailableException` (Task 1).
- Produces: `AnthropicAiClient(IOptions<AiOptions>)`; `Configured => !string.IsNullOrWhiteSpace(ApiKey)`; `internal static` request builder `BuildRequest(AiAsk ask, AiOptions o)` returning the SDK's beta create-params object.

Request shape: model `o.Model`; `MaxTokens = ask.MaxTokens`; system = two text blocks (`Instructions`, then `SharedContext` with ephemeral `CacheControl`); one user message with `Prompt`; output config with JSON schema format `ask.Schema` and effort low/medium; beta `server-side-fallback-2026-07-01` with the default fallback; no thinking parameter (Opus 5.5 thinks on its own). Client: `ApiKey`, `MaxRetries = 1`, `Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds)`. `StopReason == "refusal"` → `Refused = true`; otherwise the first text block is `Json`. Map `Usage` input, output, cache read and cache creation tokens; `Model` from the response. Any SDK exception, `HttpRequestException` or `TaskCanceledException` not caused by the caller's token → `AiUnavailableException`. Type names in the beta namespace are irregular: write the code, then fix it against the compiler.

- [ ] **Step 1: Write the failing tests**

```csharp
Not_configured_without_key() // new AnthropicAiClient(Options.Create(new AiOptions())).Configured == false; with ApiKey "x" → true
Request_caches_shared_context_and_uses_settings()
  // BuildRequest(ask(Report, Low, 4000), options{Model="claude-opus-5-5"}) → model "claude-opus-5-5", max tokens 4000,
  // two system blocks, only the second has cache control, user message == ask.Prompt, JSON schema format present,
  // effort low, betas contain "server-side-fallback-2026-07-01"
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-test AnthropicAiClientTests`
Expected: compile error (no `AnthropicAiClient`).

- [ ] **Step 3: Add the package and implement the client**

Run: `dotnet add server/FourC.Cmms.Api package Anthropic` then build.

- [ ] **Step 4: Run them to verify they pass**

Run: `wt-test AnthropicAiClientTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(ai): Anthropic client with caching, structured output and refusal fallback"
```

---

### Task 4: Diagnosis, earlier diagnoses and votes

**Files:**
- Create: `server/FourC.Cmms.Api/Features/Ai/AiDiagnose.cs`
- Modify: `server/FourC.Cmms.Api/Features/Ai/AiEndpoints.cs`, `server/FourC.Cmms.Api/Localization/ar.json`
- Test: `server/FourC.Cmms.Tests/AiDiagnosisTests.cs`

**Interfaces:**
- Consumes: `AiService.RunAsync`, `AiDiagnosis`, `AiDiagnosisVote`.
- Produces:
  - `POST /api/work-orders/{id}/diagnose` (`workorders.execute`), `POST /api/work-requests/{id}/diagnose` (`requests.manage`) → `DiagnosisDto`
  - `GET /api/work-orders/{id}/diagnoses` (the permission that shows a work order) → the work order's diagnoses and its request's, newest first; `GET /api/work-requests/{id}/diagnoses` (the permission that shows a request) → the request's
  - `PUT /api/ai/diagnoses/{id}/vote` body `{ up: bool? }` (null removes the vote; the diagnosis's record must be visible) → `{ up, down, myVote }`
  - `DiagnosisDto { Guid Id; DateTimeOffset CreatedAt; string CreatedByName; Guid? WorkOrderId; Guid? WorkRequestId; string Context; string Observed; string? ProbableCause; string? Confidence /* "low"|"medium"|"high" */; List<DiagnosisAction> Actions; bool HistoryTooThin; string? Message; int SentCount; int Up; int Down; bool? MyVote; }`
  - `DiagnosisAction { string Title; string Why; List<PartSuggestion> Parts; decimal? Hours; List<SourceRef> Sources; }`, `record SourceRef(Guid Id, string Number, string Title)`

**History (server picks, site access applies through the query filters):** completed work orders other than the current one, on the same asset or on assets of the same category; ordered same asset first, then same manufacturer and model, then the rest, each newest completion first; at most 40. Each item: number, completion date, type, title, symptom/cause/remedy names, work done and cause details (each cut to 1,000 characters), parts issued to it (part number, name, quantity), labour hours (regular + overtime). No people fields. The record itself: title, description, symptom, asset name and number, category, manufacturer, model.

**Thin history:** with fewer than 3 history items the server answers itself without calling Claude (nothing counted, nothing stored): `HistoryTooThin = true`, `Message = "There are only {0} completed work orders on this asset and similar ones — too few for a diagnosis."`, empty actions, 200. With 3 or more, Claude may still set `historyTooThin`.

**Answer schema:** `{ context: string, observed: string, probableCause: string|null, confidence: "low"|"medium"|"high"|null, actions: [{ title, why, parts: [{ partNumber, quantity }], hours: number|null, sources: [workOrderNumber] }], historyTooThin: boolean, message: string|null }`. Mapping: at most 5 actions kept; sources only among the numbers sent; parts only among the part numbers in the history; quantity ≤ 0 dropped; hours outside (0, 1000) → null. The answer is written in the person's language (their CMX language, Arabic or English). The stored `AnswerJson` holds the mapped answer (ids, numbers, titles, part names). Effort medium, `MaxTokens` 12000. No asset → 400 "Choose an asset first: a diagnosis works from the asset's history." Refused → 422 "The AI assistant couldn't help with this one." (counted, not stored).

**Listing:** sources the viewer can't see now (site access) are left out of the listed answer; vote counts and `MyVote` come from `AiDiagnosisVotes`.

- [ ] **Step 1: Write the failing tests**

```csharp
Diagnosis_sends_related_history_newest_first_and_caps_at_40()
  // asset A (category K, maker M, model X); 2 completed WOs on A, 1 on another K asset with M/X, 1 on K asset other maker; 1 open WO on A (excluded)
  // → prompt order: A's two (newest first), then the M/X one, then the other; 41+ related completed → 40 sent (SentCount == 40)
Diagnosis_excludes_other_sites_and_people()
  // a related completed WO on a site the caller can't access is absent from the prompt; assignee/requester names and e-mails absent
Diagnosis_keeps_only_sources_and_parts_that_were_sent()
  // answer cites an unsent WO number, a part not in history, 6 actions → 5 actions, unsent source and part dropped
Thin_history_answers_without_calling() // 2 related → HistoryTooThin, Message with "2", fake.Asks empty, status.used == 0, nothing stored
Diagnosis_is_stored_and_listed_with_the_request_one()
  // diagnose request R (asset A) then WO made from R → GET /work-orders/{wo}/diagnoses returns both, newest first; CreatedByName set
Votes_are_one_per_person_and_changeable()
  // user1 up, user2 down → (1,1); user1 changes to down → (0,2); user1 null → (0,1); MyVote reflects the caller
Diagnose_needs_an_asset() // WO without asset → 400 with the message
Listed_sources_hide_what_the_viewer_cannot_see() // stored answer cites a WO on site S2; viewer without S2 → source absent
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-test AiDiagnosisTests`
Expected: 404s / compile errors (no diagnose endpoints).

- [ ] **Step 3: Implement** history gathering, prompt, schema, mapping, storing, listing and votes; add messages to `Localization/ar.json`.

- [ ] **Step 4: Run them to verify they pass**

Run: `wt-test AiDiagnosisTests`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(ai): diagnosis from the asset's history with sources, earlier diagnoses and votes"
```

---

### Task 5: Usage summary and call log

**Files:**
- Create: `server/FourC.Cmms.Api/Features/Ai/AiUsage.cs`
- Modify: `server/FourC.Cmms.Api/Features/Ai/AiEndpoints.cs`
- Test: `server/FourC.Cmms.Tests/AiUsageTests.cs`

**Interfaces:**
- Produces (all `settings.manage`):
  - `GET /api/ai/usage` → `{ ceiling, limit, used, renewsOn, months: [{ month: "yyyy-MM", actions, cost }] /* 12, oldest first, org time zone, zero-filled */, byFeature: [{ feature, actions, cost }], byPerson: [{ userId, name, actions, cost }] /* this month, most actions first */ }` — actions = Answered + Refused; cost sums every row's `EstimatedCost`
  - `GET /api/ai/calls?from&to&feature&result&userId&page&pageSize` → `PagedResult<AiCallRow>` newest first, `AiCallRow { Id, CreatedAt, UserName, Feature, WorkOrderId, WorkOrderNumber, WorkRequestId, RequestNumber, Model, InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens, EstimatedCost, Result, DurationMs }`; `from`/`to` are local dates in the org time zone
  - `GET /api/ai/calls/export` → `ListExport.FileAsync(http, db, "ai-calls", …)` with the same filters

- [ ] **Step 1: Write the failing tests**

```csharp
Usage_sums_this_month_and_twelve_months()
  // seed rows (DbAsync): Answered/Refused/Failed/OverLimit this month for two users and both features, plus rows 2 and 13 months back
  // → used counts Answered+Refused only; months.Count == 12, the 13-month-old row absent; byFeature and byPerson match
Call_log_filters_pages_and_exports()
  // filter result=Failed → only failed rows; feature=Diagnosis; userId; page size 2 → TotalCount; /export → xlsx content type
Usage_needs_settings_manage() // technician → 403 on all three
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-test AiUsageTests`
Expected: 404s.

- [ ] **Step 3: Implement** the three endpoints (export column titles translated like other lists).

- [ ] **Step 4: Run them to verify they pass**

Run: `wt-test AiUsageTests`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add -A server
git commit -m "feat(ai): usage summary and call log with export"
```

---

### Task 6: Web — AI assistant settings and AI usage page

**Files:**
- Create: `web/src/pages/settings/AiSettings.tsx` (the Organization section), `web/src/pages/settings/AiUsageSettings.tsx`, `web/src/pages/settings/aiUsage.ts`, `web/src/pages/settings/aiUsage.test.ts`
- Modify: `web/src/pages/settings/SettingsPage.tsx` (section in `OrganizationSettings`, tab `ai-usage` for `settings.manage`), `web/src/locales/en.json`, `web/src/locales/ar.json`

**Interfaces:**
- Consumes: `GET /api/ai/status`, settings GET/PUT (`aiEnabled`, `aiMonthlyLimit`), `GET /api/ai/usage`, `GET /api/ai/calls`, `/api/ai/calls/export` (Tasks 1, 5).
- Produces: `aiUsage.ts` — `limitError(value: number | '' , ceiling: number, t): string | null` (null when empty or 0..ceiling), `usageColor(used: number, limit: number): 'teal' | 'yellow' | 'red'` (red at ≥ 100 %, yellow at ≥ 80 %), `fmtUsd(n: number): string` (`$0.0123` below $1, else 2 decimals).

Section copy: switch *Use the AI assistant*; *Monthly limit* (placeholder = ceiling, hint "Empty uses the server's ceiling of {0} a month."); when `!configured`: "AI isn't set up on this server." and the switch disabled; the privacy line: "Pressing an AI button sends the work's texts, codes, parts, hours and the asset's category, maker and model to Anthropic — never names, e-mails, phone numbers or photos. Anthropic doesn't train on it and deletes it within 30 days." Usage page: used/limit with progress in `usageColor`, renewal date, a 12-month `BarChart` (actions) with cost in the tooltip, tables by feature and by person, and the call log built with `useListColumns`/`FilterDef` chips (feature, result, person, date range) with export.

- [ ] **Step 1: Write the failing tests** in `aiUsage.test.ts`

```ts
limitError('', 300, t) === null; limitError(0, 300, t) === null; limitError(301, 300, t) === 'The limit can be at most 300 a month.'
usageColor(79, 100) === 'teal'; usageColor(80, 100) === 'yellow'; usageColor(100, 100) === 'red'; usageColor(0, 0) === 'red'
fmtUsd(0.01234) === '$0.0123'; fmtUsd(12.5) === '$12.50'
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-vitest aiUsage`
Expected: FAIL, module not found.

- [ ] **Step 3: Implement** helpers, the section, the tab and the page; add en and ar keys (`loc-add.py`).

- [ ] **Step 4: Verify**

Run: `wt-vitest` then `wt-web` (typecheck + lint + build)
Expected: all tests pass (including `locales.test.ts`), build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A web
git commit -m "feat(ai): web AI assistant settings and AI usage page"
```

---

### Task 7: Web — Fill from notes in the completion dialog

**Files:**
- Create: `web/src/components/aiReport.ts`, `web/src/components/aiReport.test.ts`
- Modify: `web/src/pages/work/WorkOrderDetailPage.tsx:427` (`CompleteModal`), locales

**Interfaces:**
- Consumes: `POST /api/ai/report` → `AiReportResult` (Task 2); `GET /api/ai/status`; `StatusInput.SymptomId` (Task 2).
- Produces: `aiReport.ts` — type `AiReportResult` (mirror of Task 2), `applyReport(values, r)` returning the form values with `completionNotes`, `causeNotes`, `symptomId`, `failureCodeId`, `remedyId` replaced only where the answer has a value; `reportHints(r, t): string[]` ("Log {0} h for me" style lines for the web: hours "{0} h of work", parts "Use {0} × {1}").

The dialog gains a symptom `CodeSelect` (`/symptoms`, current = the work order's symptom) sent as `symptomId`, and a **Fill from notes** button under *Work done* when AI is enabled (disabled with an empty notes box). While waiting: button loading. After filling: badge "From AI — check before completing" and an Alert listing `reportHints` for the person to add themselves. Problems (409/429/422/502) show the server's message in a notification; the form is untouched.

- [ ] **Step 1: Write the failing tests** in `aiReport.test.ts`

```ts
applyReport({completionNotes:'raw', causeNotes:'', symptomId:'s0', failureCodeId:null, remedyId:null}, {workDone:'Clean', causeDetails:null, symptom:null, cause:{id:'c1',code:'C',name:'n'}, remedy:null, hours:null, parts:[]})
  // → completionNotes 'Clean', causeNotes '' (kept), symptomId 's0' (kept), failureCodeId 'c1'
reportHints({ hours: 1.5, parts: [{partNumber:'P1', name:'Seal', quantity:2, ...}] ...}, t) // → ['1.5 h of work', 'Use 2 × Seal']
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-vitest aiReport`
Expected: FAIL, module not found.

- [ ] **Step 3: Implement** the helpers and the dialog changes; locales.

- [ ] **Step 4: Verify**

Run: `wt-vitest` then `wt-web`
Expected: pass; build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A web
git commit -m "feat(ai): fill the completion dialog from notes"
```

---

### Task 8: Web — Diagnosis panel

**Files:**
- Create: `web/src/components/DiagnosisPanel.tsx`, `web/src/components/diagnosis.ts`, `web/src/components/diagnosis.test.ts`, `web/src/components/DiagnosisPanel.test.tsx`
- Modify: `web/src/pages/work/WorkOrderDetailPage.tsx` (panel on the page), `web/src/pages/work/WorkRequestsPage.tsx` (panel in the request drawer), locales

**Interfaces:**
- Consumes: diagnose, diagnoses, vote endpoints and `DiagnosisDto` (Task 4); `POST /api/work-orders/{id}/planned-parts` `{ partId, quantity }` (existing); `GET /api/ai/status`.
- Produces:
  - `DiagnosisPanel({ kind: 'work-order' | 'request', id: string, hasAsset: boolean, canRun: boolean, canPlan: boolean, plannedPartIds: string[] })`
  - `diagnosis.ts` — `nextVote(current: boolean | null, clicked: boolean): boolean | null` (same button again clears), `confidenceColor(c?: string | null)` (`high` teal, `medium` yellow, `low` gray), `toPlan(action, plannedPartIds): PartSuggestion[]` (parts not yet planned)

Panel: **Diagnose** (or **Run again** once one exists) with a loading state and the line "Reading the asset's history… this can take up to half a minute."; hidden when AI isn't enabled, disabled with a hint without an asset. The latest diagnosis shows context, observed, probable cause with a confidence badge, actions (why, parts with quantities, hours, source links to `/work-orders/{id}` with number and title), the thin-history message, thumbs up/down with counts. *Earlier diagnoses* lists older ones collapsed (date, who). **Add to planned parts** (work orders, `canPlan`) posts each `toPlan` part; parts already planned are skipped; a server refusal for a part is shown and the rest continue; then the page's work order query refreshes.

- [ ] **Step 1: Write the failing tests**

```ts
// diagnosis.test.ts
nextVote(null, true) === true; nextVote(true, true) === null; nextVote(true, false) === false
confidenceColor('high') === 'teal'; confidenceColor(null) === 'gray'
toPlan({parts:[{partId:'a'},{partId:'b'}]}, ['a']) // → only b
// DiagnosisPanel.test.tsx (renderHtml with a stub DiagnosisDto)
// renders the probable cause, "high" badge, a link to /work-orders/<source id>, "2" up count; thin history → message shown, no actions
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-vitest diagnosis`
Expected: FAIL, modules not found.

- [ ] **Step 3: Implement** helpers, panel, page and drawer wiring; locales.

- [ ] **Step 4: Verify**

Run: `wt-vitest` then `wt-web`
Expected: pass; build succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A web
git commit -m "feat(ai): diagnosis panel on work orders and requests"
```

---

### Task 9: Phone — Fill with AI, suggestions and dictation

**Files:**
- Modify: `server/FourC.Cmms.Api/Features/TechEndpoints.cs:41` (bundle `ai`), `server/FourC.Cmms.Tests/AiReportTests.cs` (bundle test)
- Create: `web/src/tech/aiFill.ts`, `web/src/tech/aiFill.test.ts`, `web/src/tech/speech.ts`, `web/src/tech/speech.test.ts`
- Modify: `web/src/tech/types.ts` (`TechBundle.ai?: { enabled: boolean }`), `web/src/tech/api.ts` (`fillReport`), `web/src/tech/JobPage.tsx:211` (`CompleteSheet`), locales
- Modify: `mobile/package.json` (speech-recognition plugin), `mobile/android/app/src/main/AndroidManifest.xml` (`RECORD_AUDIO`), then `npx cap sync android`

**Interfaces:**
- Consumes: `POST /api/ai/report` (Task 2), `StatusPayload.SymptomId` (Task 2), `bridge()` from `native.ts`, the existing `labor.add` (`{ workDate, hours, overtimeHours, notes }`) and `part.use` (`{ partId, storeId, quantity, lineId? }`) changes.
- Produces:
  - Bundle: `ai = new { enabled = client.Configured && settings.AiEnabled && can(workorders.execute) }`
  - `api.ts`: `fillReport(workOrderId: string, notes: string): Promise<AiReportResult>` (throws an Error carrying the server's problem title)
  - `aiFill.ts`: `aiEnabled(b?: TechBundle | null): boolean` (false when `ai` is missing — a 0.14 server); `partStore(job: TechJob, partId: string): string | null` (the planned line's store for that part, else the job's default store); `aiChanges(job, r: AiReportResult, ticks: { hours: boolean; parts: string[] }, canIssue: boolean, today: string): { type: 'labor.add' | 'part.use'; payload: object }[]` (hours only when ticked; a part only when ticked, `canIssue` and `partStore` give a store; `lineId` from the planned line when there is one)
  - `speech.ts`: `speechAvailable(b = bridge()): Promise<boolean>`; `dictate(lang: 'ar' | 'en', b = bridge()): Promise<string | null>` (asks for the microphone permission, `start({ language: 'ar-EG' | 'en-US', maxResults: 1, partialResults: false, popup: true })`, first match or null on refusal/cancel)

Sheet: a microphone button next to *Work done* when `speechAvailable` (adds the text after a space); **Fill with AI** when `aiEnabled` — disabled offline with "Needs a connection"; fills work done, cause details, symptom (a new symptom select sent as `symptomId`), cause, remedy; shows "From AI — check before completing"; hours as a ticked checkbox "Log {0} h for me", each part with a store as a ticked "Use {0} × {1}", parts without a store or without stock rights as plain hint lines. On complete, `aiChanges` are queued before the signatures and the completion; a ticked hours box satisfies the "log labour" rule. Errors show the server's message; the sheet stays as typed.

Speech plugin: choose the community speech-recognition plugin release that supports Capacitor 8; if none does, leave the microphone out, keep *Fill with AI*, and record the ruling (spec decision).

- [ ] **Step 1: Write the failing tests**

```csharp
// AiReportTests
Bundle_says_when_ai_is_on() // switch off → bundle.ai.enabled false; on → true; Configured false → false
```

```ts
// aiFill.test.ts
aiEnabled(undefined) === false; aiEnabled({ ...bundle, ai: undefined }) === false; aiEnabled({ ...bundle, ai: { enabled: true } }) === true
partStore(jobWithLine('p1','s1'), 'p1') === 's1'; partStore({...job, defaultStoreId:'s9'}, 'p2') === 's9'; partStore({...job, defaultStoreId:null}, 'p2') === null
aiChanges(job, r{hours:1.5, parts:[p1×2, p2×1]}, {hours:true, parts:['p1']}, true, '2026-11-23')
  // → [{type:'labor.add', payload:{workDate:'2026-11-23', hours:1.5, overtimeHours:0, notes:null}}, {type:'part.use', payload:{partId:'p1', storeId:'s1', quantity:2, lineId:'l1'}}]
aiChanges(job, r, {hours:false, parts:['p1']}, false, d) // → [] (no stock rights)
// speech.test.ts
speechAvailable(undefined) === false; dictate('ar', stubBridge(['مرحبا'])) resolves 'مرحبا' and called start with language 'ar-EG'; permission denied → null
```

- [ ] **Step 2: Run them to verify they fail**

Run: `wt-test AiReportTests` and `wt-vitest "aiFill|speech"`
Expected: FAIL (no `ai` in bundle; modules not found).

- [ ] **Step 3: Implement** server bundle flag, helpers, `fillReport`, sheet changes, plugin and permission; locales.

- [ ] **Step 4: Verify**

Run: `wt-test AiReportTests`, `wt-vitest`, `wt-web`, and in `mobile/`: `npx cap sync android`
Expected: pass; build succeeds; sync lists the speech plugin (or the ruling is recorded).

- [ ] **Step 5: Commit**

```bash
git add -A server web mobile
git commit -m "feat(ai): phone Fill with AI, ticked hours and parts, and dictation"
```

---

### Task 10: Deployment wiring and documentation

**Files:**
- Modify: `infra/aws/cmms.yml` (parameters `AnthropicSecretArn` default '' and `AiMonthlyCeiling` default 300; condition `HasAi`; task role `secretsmanager:GetSecretValue` resource list; container secret `Ai__ApiKey` from the ARN when `HasAi`; env `Ai__MonthlyCeiling`), `.github/workflows/deploy-aws.yml` (pass `AnthropicSecretArn=${{ vars.CMMS_ANTHROPIC_SECRET_ARN }}` next to the push parameter)
- Create: `docs/AI_ASSISTANT.md`
- Modify: `README.md` (feature list), `docs/TECHNICIAN_APP.md` (microphone, Fill with AI)

**Interfaces:**
- Consumes: env names `Ai__ApiKey`, `Ai__MonthlyCeiling` (Task 1 options).

`AI_ASSISTANT.md`: what the two features do; turning it on; the limit and the ceiling; the usage page; what is sent and Anthropic's terms (from the spec's Data and privacy); setting up the key — the operator creates a Secrets Manager secret (plain text, their own key) in eu-west-2, sets the GitHub variable `CMMS_ANTHROPIC_SECRET_ARN` to its ARN, and redeploys; nobody pastes the key anywhere else.

- [ ] **Step 1: Validate the template change**

Run: `python3 -c "import yaml,sys; yaml.load(open('infra/aws/cmms.yml'), Loader=yaml.BaseLoader)"` (CloudFormation tags parse as plain scalars under BaseLoader) and `grep -n "AnthropicSecretArn\|Ai__" infra/aws/cmms.yml .github/workflows/deploy-aws.yml`
Expected: parses; parameter, condition, IAM resource, secret and env lines present, mirroring `PushSecretArn`.

- [ ] **Step 2: Write the docs**

- [ ] **Step 3: Full verification**

Run: `wt-test`, `wt-vitest`, `wt-web`
Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add -A infra .github docs README.md
git commit -m "docs(ai): AI assistant guide and AWS key wiring"
```
