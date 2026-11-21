# TrackOps — pre-submission fixes and review (Sat 21/11, due Sun 22/11 08:00)

## Context

Requests the day before submission:

1. Visual Studio fails to load all five projects (MSB4236 "SDK Microsoft.NET.Sdk not found").
2. No dead ends: no unused API endpoints (remove them from the draw.io diagram too), dependencies,
   functions, fields, classes or files. **Keep `/health/live` and `/health/ready`.**
3. Remove the coding-standards PDF and its folder; the conventions stay only as text.
4. No "now", "today" or local day from the computer (calendar included), only the external API.
5. No off-by-one bugs at the day switch (23:59 → 00:00).
6. README: complete local-run instructions (prerequisites, .NET SDK, internet access for the
   time API, how to obtain every `.env` value).
7. Everything the team lead's email asks for is delivered.
8. Question: can the feature branches be shown on GitHub (only `main` is there now)?

All code work on one branch, `feature/pre-submission-review`, small commits in the required
format, merged into `main` with `--no-ff`. No GitHub work by Claude (CLAUDE.md rule 4).
Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## 8. Feature branches on GitHub (answered in chat; the user pushes)

- All 23 `feature/*` branches still exist locally, and `origin` points to the GitHub repo. The
  user publishes them unchanged with `git push origin --all` after this round is merged (the
  new `feature/pre-submission-review` included). Claude does not push (rule 4).
  This rewrites nothing: every commit keeps its original author date, and the branch heads are
  the same commits already merged into `main`.
- `main` alone already shows the branch work: each `--no-ff` merge is a "Merge feature/…" commit,
  and GitHub's commit graph (Insights → Network) draws the branch lines.
- What cannot be hidden, and should not be: GitHub's Activity log records *when* each branch was
  pushed. The honest framing for the team lead is "branches were local; pushed together at
  submission". No dates are altered, no history is faked.

## 1. Visual Studio — cause found; install VS 2026 (user approved)

**Cause (verified):** `backend/global.json` pins SDK 11.0.100, whose
`minimumMSBuildVersion` is 18.7.0 (10.0.404 needs 18.0.0; 9.0.317 needs 17.12.0). The user's
Visual Studio Community 2022 was just updated to 17.14.45, but its MSBuild is still 17.14, so it
still rejects .NET 11. Only Visual Studio 2026 (18.x) can open the solution. The code, `dotnet
build` and the containers are unaffected.

**Fix (first step after approval, in the background while the rest is done):**
`winget install --id Microsoft.VisualStudio.Community --exact --override "--wait --passive
--add Microsoft.VisualStudio.Workload.NetWeb --includeRecommended"` → VS Community 2026 18.12.1
next to VS 2022; the user approves one Windows admin prompt. Then verify with `vswhere -version
"[18.7,)" -requires Microsoft.VisualStudio.Workload.NetWeb` and build `backend/TrackOps.sln` with
that install's `MSBuild.exe -restore -v:m` (0 warnings). ReSharper for VS 2026 is the user's own
JetBrains install. README and CLAUDE.md state the requirement (section 6).

## 2. Dead code and unused API (audited: backend, frontend, infra, tools, tests)

No unused NuGet or npm package, no dead component or route, no scaffold leftovers, no unused
Python. Removed:

**Unused endpoint `GET /shifts/{shiftId}`** — the frontend never calls it; only tests and the
clock-in `Location` header use it. Remove the action (`ShiftsController.GetById`),
`ShiftReadService.GetByIdAsync`, the `Location` header on clock-in, its row in `docs/api/API.md`,
the leaf in `tools/build_api_diagram.py` and the "Location: /shifts/{shiftId}" text in the
clock-in-flow page; regenerate the `.drawio`, export both PNGs and look at them. Tests switch to
what the app uses: `test_clock.py` (Location assert and the 404-for-a-stranger test →
"a stranger's history does not contain it" via `QUERY /shifts`), `test_external_time_only.py:55,171`
and `test_manager.py:147` read the shift from `QUERY /shifts`. DECISIONS' "another user's shift
returns 404" becomes "a shift id that is not that employee's returns 404" (manager routes).
`/health/live` and `/health/ready` **stay** (user decision), as do their test and the diagram's
"Internal" part.

**API fields nobody reads** (backend DTO + `frontend/src/shared/api/types.ts` + API.md):
`MonthTotalsDto.AverageDailyOvertimeMinutes` and `DaysWorked` (drop the two asserts in
`tests/test_dst_overtime.py:104,107`), `ActivityItemDto.ShiftId`, `TimeDto.Source`. Fields that
tests or tools read (`shiftMinutes`, break `minutes`, `pageSize`, `username`, `zone`,
`offsetSeconds`) stay as tested, documented contract.

**Backend tidy-ups:** empty folder `TrackOps.Infrastructure/Auth/`; duplicate `<summary>` above
`AuthRules` moved to `WorkRules`; `"AllowedHosts": "*"`; one `k_LikeEscape` (in `ShiftSearch`);
one `truncateToMilliseconds` (small static helper in `TrackOps.Domain`, used by
`ExternalTimeProvider` and `EmployeeService`); `EmployeeService` uses `ShiftMapper.ToDto` for
`UserDto`; constants used only inside their class become private. Kept on purpose:
`AppDbContext.Breaks` (names the table), `Shift.TimeSource`, `eRequestStatus.Approved/Rejected`,
problem `type`/`traceId`, reporting views, `InvariantGlobalization` (ICU resolves the zone),
`ASPNETCORE_ENVIRONMENT=Production`.

**Frontend tidy-ups:** `zurichDate`, `minutesBetween` (`shared/time/zurich.ts`); `Notice`'s
unused `'info'` kind and `.notice-info`; the no-op `.field legend` selector; tokens `--success`,
`--success-tint` and their comment line, and fix the comment claiming request statuses are the
only extra colours; `export` dropped from names used only in their own file; stock Vite
`frontend/.gitignore` (root `.gitignore` covers it).

**Infra/root:** CSP `img-src 'self' data:` → `'self'` (no data: images remain; check the console
on every screen); `.gitattributes` drops `*.sh`/`*.woff2` (match nothing) and covers
`*.conf.template`.

## 3. Coding-standards PDF

`docs/reference/CodingStandards_NoSC.pdf` is the only file in `docs/reference/`; never committed
(history checked). Delete file and folder; remove its two `.gitignore` lines. The conventions stay
as text in CLAUDE.md, enforced by `.editorconfig` and `backend/TrackOps.sln.DotSettings`.

## 4. Time sources (audited end to end: the rule holds)

Every recording, correction, request filing, auto-close round and idempotency clean-up takes a
fresh external reading. Every "today"/"now so far" (calendar, dashboard, Forms and data) comes
from the display clock = one external reading + monotonic time. The frontend never reads the
computer's clock or zone; Zurich days use the named zone `Europe/Zurich`. The only machine-clock
use is JWT lifetimes (`SystemClock`), allowed by design. Fixes for the hidden gaps found:

- **Correction times without `Z`:** `EmployeeService` calls `ToUniversalTime()`, which reads an
  offset-less value in the server's zone. Validation rejects `clockInUtc`/`clockOutUtc` whose kind
  is not UTC (400); two new cases in `test_invalid_corrections_are_rejected`; API.md says "UTC,
  ending in Z".
- **timeapi.io parser** (`TimeApiParsers.ParseTimeApiIo`): `RoundtripKind`; a value carrying its
  own zone is a provider fault.
- **Display clock race** (`DisplayClock`): anchor and Stopwatch timestamp become one immutable
  pair swapped in one assignment.
- **Optimistic placeholder** (`dashboard/shiftActions.ts`): skipped until the display clock has a
  time (today it can draw 1970 for a moment).
- **Calendar cache race** (`RedisCalendarCache`): `GetAsync` returns the version it read;
  `SetAsync` stores under it (`ICalendarCache` in `Abstractions.cs`, caller `GetMonthAsync`).
- **Static clock test** tightened: per-file allowlists (`Stopwatch`/`DateTime.UtcNow` only in
  `Time/DisplayClock.cs`, `performance.now` only in `serverClock.ts`); backend also
  `TimeZoneInfo.Local`, `DateTimeKind.Local`, `.ToLocalTime(`, `.ToUniversalTime(`,
  `SYSDATETIMEOFFSET(`, `CURRENT_TIMESTAMP`, `defaultValueSql`; frontend also local-zone getters,
  `toLocale*String(`, and every `Intl.DateTimeFormat(` must name a `timeZone`; Python in `tools/`
  and `tests/` (stub allowlisted); comment stripping no longer cuts at `//` inside URLs.

## 5. Day switch (23:59 → 00:00)

Checked: day windows are half-open `[local midnight, next local midnight)` everywhere
(`WorkCalculator`, `loadShiftsTouchingAsync`, the History `from`/`to` filter); a shift ending
exactly at midnight is not counted on the next day; "today" comes from the display clock through
`Europe/Zurich`; clocks format with `hourCycle: 'h23'` (never "24:00"); the frontend's date
arithmetic is UTC-only; month and week edges in `summariseWork` stay inside the two months it reads.

**One real off-by-one found:** a shift split at midnight loses a minute. `WorkCalculator` floors
each day's seconds separately, while a shift's own minutes floor its total: 23:59:30–00:01:00
(90 s) is 1 minute in History but 0 + 0 in the Calendar; 23:30:30–00:30:30 shows 29 + 30 = 59
instead of 60. **Fix:** give each shift's minutes to days by cumulative flooring — for a shift,
day minutes = ⌊worked seconds up to the day's end / 60⌋ − ⌊worked seconds up to the day's start /
60⌋ (same for break minutes), then sum over shifts. A shift's day minutes then always add up to
exactly its own worked minutes; DST days are unaffected (instants only). Overtime is still
`max(0, worked − target)` per day.

**New tests** (`tests/test_dst_overtime.py` or a new `tests/test_day_boundaries.py`), using the
stub in winter (UTC+1) and summer (UTC+2):
- 23:59:30 → 00:01:00 local: day 1 = 0, day 2 = 1, sum = the shift's `workedMinutes` = 1.
- A night shift with a break across midnight: per-day minutes add up to the shift's minutes.
- Month boundary (30 Nov 23:30 → 1 Dec 00:30): split between the two months' calendars.
- "today" switches exactly at Zurich midnight: stub 22:59:58Z → `2026-11-17`, 23:00:01Z →
  `2026-11-18`; summer 21:59:58Z / 22:00:01Z.
- History `to=D` includes a clock-in at 23:59:59 local and excludes 00:00:00 the next day.
Existing DST tests keep passing (they compare with a one-minute tolerance).

## 6. README: complete local-run instructions

Rewrite "Run it locally" so a reviewer with a fresh machine succeeds:
- **Prerequisites table:** Docker Desktop with Compose v2 (`docker compose version`), at least
  4 GB memory for Docker (SQL Server needs 2 GB), free port 8080, about 3 GB of disk for images;
  **internet access** while building (images, NuGet, npm inside Docker) **and while running**
  (the API containers call `https://timeapi.io` and `https://time.now` on port 443; behind a proxy
  set it in Docker Desktop), with a one-line check of the time API. Not needed to run the app:
  .NET SDK, Node, Python.
- **Optional, only to run the tests:** Python 3.12+ (with the `tests/.venv` steps). Running the
  app never needs Python.
- **Optional, only for working on the code:** .NET SDK 11.0.100 or newer 11.0 (pinned by
  `backend/global.json`, download page, `dotnet --list-sdks` check); Visual Studio 2026 18.7+
  (VS 2022 cannot load .NET 11 projects) or the `dotnet` CLI; Node 22 + npm for the frontend dev
  server; draw.io Desktop only for diagrams.
- **`.env`, every variable:** a table of all 14 variables in `.env.example` — required or not,
  what it is, how to obtain or generate it, rules (SQL password policy; 32+ characters for the
  two keys; values without `$`, quotes or `;`). New helper **`tools/make_env.py`** (standard
  library only) copies `.env.example` to `.env` and fills every `CHANGE_ME` with a fresh random
  value that meets those rules; it refuses to overwrite an existing `.env`. Because Python is
  optional, the README runs it **through Docker** by default (no local Python):
  PowerShell `docker run --rm -v "${PWD}:/app" -w /app python:3.13-alpine python tools/make_env.py`,
  macOS/Linux the same with `"$PWD"` and `--user "$(id -u):$(id -g)"`, Git Bash with
  `MSYS_NO_PATHCONV=1`; with local Python simply `python tools/make_env.py`. Manual filling is
  still described for anyone who prefers it. Verified on this machine in PowerShell and Git Bash.
- **Check it works:** `docker compose ps` (migrator `Exited (0)`, the rest up/healthy), open the
  app, clock in.
- **Troubleshooting:** `sqlserver` exits (password policy or memory), port in use
  (`PUBLIC_PORT` + `PUBLIC_ORIGIN`), clock-in returns 503 (time API unreachable), slow `localhost`
  on Windows (use `127.0.0.1` for scripts), stop vs reset (`down` vs `down -v`).
- Test-only variables (`TEST_MANAGER_USERNAME`, `TEST_SIGN_IN_PER_MINUTE`, `EXPECT_HTTPS`,
  `EXPECT_NO_MANAGER`, `TRACKOPS_URL`…) listed in the Tests section.
- `docs/SECURITY-REPORT.md` settings table stays in step.

## 7. Team lead's requirements (email re-read)

| The email asks for | State |
|---|---|
| Clock in/out on an external API time for Europe/Zurich, never browser/server clock | Done; re-audited (sections 4, 5) |
| Repo with full commit history | Done locally (77 commits, 23 feature branches, `--no-ff`); push is the user's step (section 8) |
| README with local run incl. database setup | Done; made complete (section 6) |
| DECISIONS.md: decisions and why, edge cases, what was not done, known limits | Done; this round adds to it |
| CLAUDE.md, `.claude/` and the work plans, where Claude helped and was corrected | Done; gaps below |

Also re-checked against the brief: routes = API.md = diagram (21 + 2 health; 20 + 2 after this
round); README embeds both diagrams; NuGet and npm scans clean; no secret file ever committed; the
assignment's e-mail addresses appear nowhere in history.

**Gaps closed:** save this plan as `.claude/plans/02-pre-submission-review.md`; README gets a
short "How this was built with Claude Code" section linking `CLAUDE.md`, `.claude/WORKLOG.md`
and `.claude/plans/`; WORKLOG gets this round honestly (the .NET 11 move was made without
checking Visual Studio 2022 could open it; the hidden time gaps, the cache race and the midnight
minute, and their fixes).

## Commits (on `feature/pre-submission-review`)

1. Remove the local reference folder from `.gitignore` (PDF deleted from disk; never tracked).
2. Reject correction times that are not UTC (+ test, API.md).
3. Harden the time parser and the display clock (+ tightened static test).
4. Store calendar months under the version they were read with.
5. Split shift minutes across midnight without losing a minute (+ day-boundary tests).
6. Skip the optimistic placeholder until the display clock has synced.
7. Remove the unused shift-by-id endpoint and unused API fields (API.md, diagram, tests).
8. Remove unused frontend code, styles and config (incl. CSP, `.gitattributes`).
9. Tidy the backend (shared helpers, private constants, stray comment, empty folder).
10. Add `tools/make_env.py` and complete the local-run instructions.
11. Document the IDE requirement and the Claude Code material (CLAUDE.md, DECISIONS, WORKLOG,
    `.claude/plans/02-…`).

## Verification

- `dotnet build backend/TrackOps.sln`: 0 warnings; and with VS 2026's MSBuild once installed.
- Test stack: full `pytest` (all pass; the 2 environment-gated tests skipped) incl. the new
  day-boundary tests; `test_static_clock.py` and its self-check catch planted violations.
- `npm --prefix frontend run build`; `dotnet list package --vulnerable --include-transitive`;
  `npm --prefix frontend audit`.
- Browser: every screen loads with no CSP errors; clock in/out works; the calendar shows the
  night shift of the demo user split correctly.
- Lighthouse runner: 100 on every screen.
- **Clean-clone run following only the new README:** stop the main stack (volumes kept),
  `git clone` to a temp folder, create `.env` with the Docker command (no local Python),
  `docker compose -p trackops-clean up
  -d --build`, sign in, clock in/out against the real time API, file a request; then
  `docker compose -p trackops-clean down -v`, delete the temp folder, restart the main stack
  (demo data intact).
- `graphify update .`; end on the normal stack.
