# TrackOps — shift-clock home assignment

## Context

Job-application home assignment, due **Sunday 2026-11-22 08:00 Israel time**. Build a shift-clock
system (ASP.NET Core + SQL Server + React) where every clock-in/out timestamp comes from an
external time API for Europe/Zurich. Few features, done deeply and justified; everything else is
documented in DECISIONS.md. The owner presents it in 10 minutes and then changes code live, so
every part must stay simple to explain.

Two approval gates precede any building. **Status: Gate 1 approved 2026-11-16 (19 endpoints,
QUERY only, session-resource auth). Now at Gate 2; approving this plan is the "go".**
Nothing is scaffolded, written or committed yet. The project folder is empty and not a git repo.

Note: plan mode fixes this file's location under `~/.claude/plans/`. After "go" it is copied to
`<repo>/.claude/plans/` so it ships with the repo, as the email requires.

## Inputs read

| Input | Result |
|---|---|
| Assignment email (Hebrew) | Read. No conflict with the prompt. 22/11/2026 is a Sunday. |
| Figma wireframes | Read via Figma MCP: Sign in 2:2, Dashboard 2:609, Data 49:559, Calendar 64:1472, Employees 66:2144. |
| C# coding standards PDF | Read (via `pdftotext`; the Read tool lacks poppler). |
| ReSharper `.DotSettings` | Read. |

Coding rules that shape the backend: `m_` fields, `r_` readonly, `s_`/`sr_` static, `k_` consts,
`i_`/`o_`/`io_` parameters, `e`-prefixed enums, `v_` local const bools, private methods
lowerCamel, explicit types (no `var`), tabs, braces always, one `return` per method.
The PDF (braces always) is stricter than the .DotSettings (braces optional); the PDF wins.

## Environment findings

| Item | Found | Action needed |
|---|---|---|
| .NET SDK | 9.0.317 only (out of support since 2026-11-10) | Gate 2: target .NET 10 LTS; SDK install needs approval |
| Node / npm | 22.14.0 / 10.9.2 | none |
| Python | 3.13.2 | none |
| Docker | CLI 29.7.2, Compose 5.3.1, **daemon not running** | start Docker Desktop before Phase 1 |
| CPU | x86_64 | standard SQL Server image works, no arm64 workaround |
| draw.io MCP | hosted jgraph server is connected (`create_diagram`, `search_shapes`, `get_drawio_reference`) | none |
| draw.io Desktop | **not installed** | `winget install JGraph.Draw` (needed for PNG export) |
| HTTP QUERY | RFC 10008, Proposed Standard, June 2026 | confirmed |
| Time APIs | `timeapi.io` 200 in ~0.25 s; `time.now` 200 in ~1.4 s; `worldtimeapi.org` connection reset | use the first two |

## Gate 1 — REST contract (proposed)

Base URL `http://localhost:8080/api/v1` locally (same origin as the SPA, behind nginx). JSON only.

**Auth and session.** Mock sign-in: any valid-looking credentials pass; username `admin` →
Manager, otherwise Employee; user upserted on first sign-in. Sign-in sets two cookies, both
`HttpOnly; Secure; SameSite=Strict`: a JWT (TTL 86400 s, carries a `sid` claim) and an opaque
session id. A request is authenticated only if the JWT is valid **and** its `sid` equals the
cookie **and** that session exists in Redis. Sign-in rotates the session; sign-out deletes it,
which kills the JWT immediately.

**CSRF.** Double-submit token: `GET /auth/csrf` issues it; POST, PATCH and DELETE must echo it in
`X-CSRF-Token`, else 403. QUERY and GET are safe and need none.

**Errors.** RFC 9457 `application/problem+json` with a stable machine `code`
(`shift_already_open`, `no_open_shift`, `break_already_open`, `no_open_break`,
`time_source_unavailable`, `csrf_invalid`, `rate_limited`, `validation_failed`, `stale_version`),
a `traceId`, and per-field `errors` on 400.

**Status codes.** 200, 201, 204 · 400 validation · 401 unauthenticated · 403 wrong role or CSRF ·
404 not found or not yours · 409 state conflict · 429 with `Retry-After` · 503 time source down.

**Idempotency.** The four recording endpoints (★) require an `Idempotency-Key` UUID header. The
key and the response are stored in SQL Server in the same transaction as the write, so a retry on
either replica replays the original response and never writes twice.

**Pagination.** `page` (1-based), `pageSize` (default 20, max 100); response
`{ items, page, pageSize, totalItems, totalPages }`; stable sort `clockInUtc desc, id desc`.

**QUERY.** Filtered lists use `QUERY` with a JSON body. `QUERY` is allowed in CORS.
**No POST fallback** (owner decision, 2026-11-16, overriding the original brief): if a proxy or
client rejects `QUERY`, the search fails. QUERY through nginx and from browser `fetch` is
therefore verified first thing in Phase 1; the limit is recorded in DECISIONS.md, and the pytest
success metric becomes "QUERY endpoint" without "and POST fallback".

**Role locks.** Employee = any signed-in user (managers included). Everything under `/employees`
is Manager only. Own-data endpoints return 404, not 403, for another user's shift.

| # | Method | Path | Purpose | Role | Main codes |
|---|---|---|---|---|---|
| 1 | GET | `/auth/csrf` | Issue CSRF token | Public | 200 |
| 2 | POST | `/auth/session` | Sign in, rotate session cookies | Public | 200, 400 |
| 3 | GET | `/auth/session` | Current user and role | Employee | 200, 401 |
| 4 | DELETE | `/auth/session` | Sign out, revoke Redis session | Employee | 204 |
| 5 | GET | `/time` | Zurich time, seeds the display clock | Employee | 200, 503 |
| 6 | POST | `/shifts` | Clock in ★ | Employee | 201, 409, 503 |
| 7 | POST | `/shifts/current/clock-out` | Clock out, closes an open break ★ | Employee | 200, 409, 503 |
| 8 | GET | `/shifts/current` | Open shift with its breaks | Employee | 200, 204 |
| 9 | GET | `/shifts/{shiftId}` | One own shift with breaks | Employee | 200, 404 |
| 10 | GET | `/shifts/activity` | Recent in/out/break events | Employee | 200 |
| 11 | QUERY | `/shifts` | History: filter, search, paginate | Employee | 200, 400 |
| 12 | POST | `/shifts/current/breaks` | Start break ★ | Employee | 201, 409, 503 |
| 13 | POST | `/shifts/current/breaks/current/end` | End break ★ | Employee | 200, 409, 503 |
| 14 | GET | `/calendar/months/{yyyy-MM}` | Per-day worked + overtime, month totals | Employee | 200, 400 |
| 15 | GET | `/calendar/days/{yyyy-MM-dd}` | Day summary + that day's shifts (`today` accepted) | Employee | 200, 400 |
| 16 | QUERY | `/employees` | Autocomplete employees by name | Manager | 200, 403 |
| 17 | QUERY | `/employees/shifts` | All employees' shifts: filter, search, paginate | Manager | 200, 403 |
| 18 | PATCH | `/employees/{userId}/shifts/{shiftId}` | Correct times; reason required; audited | Manager | 200, 404, 409 |
| 19 | GET | `/employees/{userId}/shifts/{shiftId}/audit` | Correction audit trail | Manager | 200, 404 |

Internal only, not routed by nginx: `GET /health/live`, `GET /health/ready`.

Screen → endpoint map: Dashboard uses 5, 6, 7, 8, 10, 12, 13, 14, 15 · History uses 11 ·
Calendar uses 14, 15 · Employees uses 16, 17, 18, 19.

### Diagram

Two pages are drafted and rendered inline in the chat (draw.io XML, app palette):
page 1 "API map" (title block, architecture strip, six resource cards, legend) and page 2
"Clock-in flow" (nine steps with rejection branches, fail-closed 503 highlighted).
**Not yet done:** PNG export and the visual clipping/overlap check. Both need draw.io Desktop and
permission to write files, which plan mode withholds.

### Open points for Gate 1 feedback

- Session modelled as a resource (`POST/GET/DELETE /auth/session`) rather than `/auth/login`,
  `/auth/logout`, `/auth/me`.
- PUT is unused; no endpoint was invented to use it.
- Manager corrections change shift clock-in/out only, not individual breaks.
- Local transport was settled at Gate 2: plain HTTP on port 8080 (see below).

## Gate 2 — execution plan

**Can I deliver it? Yes**, Tier 1 in full and Tier 2 with the cut order below. What I cannot
do or promise: any GitHub step (yours by rule); uptime of a free public time API (the app fails
closed by design, so a provider outage during your demo means clock-in returns 503);
Lighthouse >= 90 is a target I will measure, not a guarantee; nothing below is verified until
it is built and run.

### First steps after "go" (before any app code)

1. Install draw.io Desktop (`winget install JGraph.Draw`, approved), save
   `docs/api/rest-api.drawio`, export both pages to PNG at 2x, look at them, fix, re-export.
2. Save the approved contract to `docs/api/API.md`. Copy this plan to `.claude/plans/`.
3. You start Docker Desktop. I install the .NET 10 SDK (approved); builds run in Docker either
   way, and the local SDK is for your IDE during the live-coding session.

### Stack and dependencies (each one justified)

| Piece | Choice | Why |
|---|---|---|
| Backend | ASP.NET Core on .NET 10 LTS, controllers | Controllers suit the course conventions; `[AcceptVerbs("QUERY")]` |
| Data access | EF Core (SQL Server provider) only | Parameterized by default, projections, migrations; no second ORM |
| Redis client | StackExchange.Redis | Sessions, rate limits, cache, job lock |
| Auth | Microsoft JwtBearer + built-in antiforgery | No custom crypto |
| Resilience | Microsoft.Extensions.Http.Resilience | Timeout, retry, circuit breaker for the time API |
| Rate limiting | ~60 lines of own middleware over Redis `INCR`/`EXPIRE` | The built-in limiter is per-process, wrong for 2 replicas |
| Tracing | OpenTelemetry (Tier 2, first cut) | |
| Frontend | React + TypeScript + Vite, React Router, TanStack Query | Query gives de-duplication, caching, optimistic update with rollback, parallel requests |
| Styling | Plain CSS with tokens and logical properties | No UI kit; RTL by `dir="rtl"` |
| Font | Heebo, self-hosted via `@fontsource/heebo`, Hebrew + Latin, 400/500/700, `font-display: swap` | No CDN |
| Dates in UI | `Intl.DateTimeFormat` with `timeZone: 'Europe/Zurich'` | No date library |
| Tests | pytest + requests | Black-box against the compose stack |

Image tags (SQL Server 2022, Redis, nginx, Node, .NET 10) are pinned to exact versions in Phase 1
after checking the registries; I will not guess tag names here.

### Folder structure

```
compose.yaml  .env.example  .gitignore  README.md  DECISIONS.md  CLAUDE.md
backend/   TrackOps.sln  TrackOps.sln.DotSettings  .editorconfig  Dockerfile
  src/TrackOps.Api             controllers, middleware (CSRF, rate limit, headers), Program.cs
  src/TrackOps.Application     use-case services, DTOs, ITimeProvider, overtime calculator
  src/TrackOps.Domain          entities, enums, rules; no dependencies
  src/TrackOps.Infrastructure  EF Core + migrations, Redis, time API clients, background job
  src/TrackOps.Migrator        one-shot: applies migrations, creates the least-privilege login
frontend/  Dockerfile  src/app (router, shell)  src/features/{auth,dashboard,history,calendar,employees}
           src/shared/{ui,api,time,styles}
tests/     pytest suites, static clock check, time-stub/ (stub time API container)
infra/     nginx/ (config), compose.test.yaml
docs/      api/{API.md,rest-api.drawio,*.png}  SECURITY-REPORT.md  reference/ (PDF, gitignored)
.claude/   plans/, work log
```

"Re-use react containers": I read it as you do. Container components fetch and hold state;
presentational components take props only. One `ShiftTable`, one pagination control and one
filter bar are shared by History, the Calendar day list and Employees.

### Database

| Table | Columns | Constraints and indexes |
|---|---|---|
| `users` | id, username, display_name, role, created_at_utc | unique(username); check role in (0,1) |
| `shifts` | id, user_id, clock_in_utc, clock_out_utc null, status, note, time_source, row_version | check clock_out > clock_in; **filtered unique index on (user_id) where clock_out_utc is null** = one open shift per user; index (user_id, clock_in_utc desc); index (clock_in_utc desc) |
| `breaks` | id, shift_id, start_utc, end_utc null | check end > start; **filtered unique index on (shift_id) where end_utc is null** = one open break |
| `shift_audit` | id, shift_id, changed_by, changed_at_utc, kind, reason, old/new clock_in/out | append-only; index (shift_id) |
| `idempotency_keys` | user_id, key, endpoint, status_code, response_json, created_at_utc | primary key (user_id, key) |

- Views `v_shift_report` and `v_break_report` derive shift minutes, break minutes and worked
  minutes per shift with the user name, for reporting.
- "Break lies inside its shift" and "corrected shifts do not overlap" span rows, so a CHECK
  cannot express them. They are checked inside the write transaction with the shift row locked.
- The API connects as a login with read/write on these tables only. Migrations run in a separate
  one-shot container, so the two replicas never race on schema.

**Why instants, not "clock-in + shift minutes":** an open shift has no minutes yet; breaks and
manager corrections need real instants; DST arithmetic is only correct on instants; and a stored
duration can drift from its endpoints. Minutes are always derived.

### Time source

- `ITimeProvider.GetNowAsync()` returns a UTC instant plus the provider name. Every recording
  makes a **fresh** external call; nothing is cached for recording.
- Order: `timeapi.io` (2 s timeout, one retry) → `time.now` (2 s) → fail closed with 503 and
  `Retry-After`. A circuit breaker stops hammering a provider that is down.
- If the external time is earlier than the open shift's clock-in, that is treated as a time
  source fault (503), and the DB check backs it up.
- `GET /time` (display only) serves a per-replica anchor: one external instant plus monotonic
  elapsed time, refreshed at most every 60 s. The browser ticks from it with
  `performance.now()` and re-syncs periodically. DECISIONS.md states that display ticking is not
  recording.
- Audit timestamps and the auto-close job also take their time from `ITimeProvider`.
- Tests point both provider URLs at the stub container, which can set the time, delay and fail.

### Overtime rule (approved 2026-11-16)

- Daily target: 8 h, configurable (`Overtime:DailyTargetMinutes`).
- Worked time for a Zurich local day = the part of each shift that falls between that day's two
  local midnights (as UTC instants), minus the breaks in that window. A shift crossing midnight
  is split across both days.
- Daily overtime = max(0, worked − target). Short days count 0; they do not cancel overtime.
- Monthly overtime = sum of daily overtime. The dashboard card shows today's overtime and its
  difference from the month's average daily overtime over days worked.
- 23 h and 25 h days need no special case: only real elapsed time counts, and the target stays 8 h.
- Ignored on purpose: Swiss public holidays, weekends, per-employee contracts, weekly legal caps.

### Edge cases

| Case | Handling |
|---|---|
| Double clock-in, 20 concurrent, 2 replicas | Filtered unique index; losers get 409 |
| Clock-out or break action with nothing open | 409 with a specific code |
| Clock-out with an open break | Break is closed at the same instant, same transaction |
| Retried request | Idempotency key replays the stored response |
| Forgotten clock-out | Background job (Redis lock, one replica runs) closes shifts open > 16 h at clock-in + 16 h, status AutoClosed, audit row; a manager corrects it |
| Manager correction | PATCH with required reason, `rowVersion` for stale edits (409), audit row |
| Midnight and DST | Split by local day from UTC instants |

### Local transport (decided: plain HTTP)

The app runs at `http://localhost:8080`; base URL `http://localhost:8080/api/v1`. The diagram
title block is corrected to this when the `.drawio` is saved. The cookie `Secure` flag and the
HSTS header are config switches (`Security:RequireHttps`), **on by default and off only in the
local `.env`**. pytest runs one check against a stack started with the switch on and asserts
the `Secure` attribute and the HSTS header. README states, in your words: HTTPS is not enabled
locally because the certificate would be self-signed and browsers reject it; real production
uses a professionally issued certificate.

### Decided at Gate 2

- Overtime rule: approved as written below.
- Logo: **A, "Tick T"** (letter T with a teal seconds dot); SVG logo and favicon drawn in Phase 1.
- .NET 10 SDK: I install it with `winget install Microsoft.DotNet.SDK.10` after "go".
- draw.io Desktop: I install it with `winget install JGraph.Draw` after "go".
- **Docs note on .NET 10 and QUERY** (owner request): README (stack section) and DECISIONS.md
  both say that the HTTP QUERY method was used at the team lead's request and that the backend
  runs on .NET 10. Wording must stay technically true, because evaluators will read it:
  .NET 10 is chosen as the current LTS; QUERY works on it through `[AcceptVerbs("QUERY")]`
  (custom verb mapping); first-class QUERY support (`HttpMethods.Query`, OpenAPI 3.2) only
  arrives in .NET 11. The docs will not claim that .NET 10 has built-in QUERY support.
  What exactly the team lead asked for is the owner's to confirm before the docs are written.

### Tiering

Yours, with two changes. The "background queue" becomes one hosted job (auto-close plus
idempotency cleanup) because nothing else in scope is slow; a generic queue would have no user
and goes to DECISIONS.md. OpenTelemetry moves to the end of Tier 2.

Cut order if time runs short: OpenTelemetry → Redis read cache → optimistic updates (fall back
to a pending state) → audit-trail viewer UI (API and table stay) → History stat tiles.
Never cut: fail-closed time, the unique-index concurrency guarantee, idempotency, auth/CSRF/rate
limiting, the core pytest suites, README and DECISIONS.md.

### Phases and estimates

Estimates are working hours, rough, not measured. Deadline Sun 22/11 08:00; today is Mon 16/11.

| Phase | Content | Est. | Target day |
|---|---|---|---|
| 0 | Diagram PNGs, API.md, toolchain | 0.5 h | Tue |
| 1 | git init, compose, skeletons, tokens, RTL, font, **QUERY spike through nginx** | 2.5 h | Tue |
| 2 | Time provider, shifts, breaks, auth, session, idempotency, job | 4.5 h | Tue–Wed |
| 3 | Five screens, overtime, loaders, optimistic updates, clock | 5 h | Wed–Thu |
| 4 | Security hardening and report | 3 h | Thu |
| 5 | Cache, indexes check, compression, tracing, a11y | 3 h | Fri |
| 6 | pytest suites and stub (core tests are written with Phases 2 and 4) | 3.5 h | Thu–Fri |
| 7 | README, DECISIONS, SECURITY-REPORT, CLAUDE.md, work log | 2 h | Sat |
| — | Clean-clone run, final report, your rehearsal | 2 h | Sat |

About 26 h, leaving Saturday night as buffer. Git: `main` plus one `feature/*` branch per
phase slice, merged with `--no-ff`; commit messages in your format, ending with the
Claude co-author trailer.

### Top risks

1. **QUERY with no fallback.** Kestrel and `fetch` accept custom methods; nginx should proxy
   them but I have not run it. Verified in Phase 1 before anything depends on it.
2. **Free time APIs**: rate limits unknown, `worldtimeapi.org` already down. Two providers and
   fail-closed; tests never touch the real APIs.
3. **SQL Server in Docker**: needs about 2 GB RAM and a slow first start; Docker is not running now.
4. **Course C# conventions vs. framework conventions** (`i_` parameters and model binding,
   single return). Handled with explicit binding names; generated migrations are exempt.
5. **Plain HTTP locally** means `Secure` and HSTS are exercised only by the one switched-on test
   run, not by everyday use.
6. **Lighthouse** needs a Chrome run; if I cannot run it, I will say so rather than claim a score.

### Earlier leanings, now part of the plan

- **Time source.** Primary `timeapi.io` `/api/timezone/zone` (local time plus UTC offset, so the
  repeated hour at DST fall-back is unambiguous); second external provider `time.now`; fail
  closed with 503 when both fail. Its `/api/time/current/zone` endpoint returns local time with
  no offset and is rejected for that reason.
- **Storage.** UTC instants as the source of truth; durations derived, exposed through a view.
- **Runtime.** .NET 10 LTS, `QUERY` mapped with `[AcceptVerbs("QUERY")]`.

## Verification (after build)

`docker compose up` from a clean clone, then `pytest` against the running stack with a stub time
API container; the static system-clock check; `dotnet list package --vulnerable` and `npm audit`;
Lighthouse accessibility; diagram endpoints diffed against API.md and the real route table.
