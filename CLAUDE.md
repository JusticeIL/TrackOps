# CLAUDE.md

Guidance for Claude Code when working in this repository. Read `DECISIONS.md` before changing
behaviour; it says why things are the way they are.

## What this is

TrackOps: a shift clock. ASP.NET Core (.NET 11) + SQL Server + Redis + React, run with Docker
Compose. Two API replicas behind nginx. UI in Hebrew (RTL); code, commits and docs in English.

## Rules that must not be broken

1. **Recorded time comes only from `ITimeProvider`** (the external time API). Never use
   `DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow`, `Date.now()` or `new Date()` for anything
   that is stored. The system clock may be touched only in
   `backend/src/TrackOps.Infrastructure/Time/` and `frontend/src/shared/time/`.
   `tests/test_static_clock.py` enforces this; run it after any change.
2. **Fail closed.** If the time source is unavailable, record nothing and return 503. Do not add
   a fallback clock.
3. **Invariants belong in the database** (unique indexes, check constraints, row locks), never in
   in-memory state. There are two replicas.
4. **No GitHub work.** No `gh`, no remote, no push, no `.github/` folder, no CI files. Local git only.
5. **No secrets in the repo.** New settings go in `.env.example` with a placeholder and in the
   table in `docs/SECURITY-REPORT.md`.
6. **Documentation files are limited to:** `README.md`, `DECISIONS.md`, `CLAUDE.md`,
   `docs/api/API.md`, `docs/SECURITY-REPORT.md`, and files under `.claude/`. Do not create other
   `.md` files.
7. **The API contract has three copies that change together:** `docs/api/API.md`, the endpoint
   tree in `tools/build_api_diagram.py` (then regenerate the `.drawio` and PNGs), and the
   controllers.

## Commands

```bash
# Run the stack (real time APIs)
docker compose up -d --build

# Run the stack for tests (stub time API) and test
docker compose -f compose.yaml -f infra/compose.test.yaml up -d --build
tests/.venv/Scripts/python -m pytest                       # all, about 3 minutes
tests/.venv/Scripts/python -m pytest tests/test_clock.py   # one file
tests/.venv/Scripts/python -m pytest tests/test_static_clock.py   # needs no stack

# Demo history for the employee "demo" (developer tool; never run automatically)
python tools/seed_demo_data.py

# Create .env with random secrets (never overwrites; Docker variant in the README)
python tools/make_env.py

# Backend
dotnet build backend/TrackOps.sln            # needs the .NET 11 SDK; must stay at 0 warnings
                                             # IDE: Visual Studio 2026 18.7+ (VS 2022 cannot load .NET 11)
docker compose up -d --build backend-1 backend-2

# Frontend
npm --prefix frontend run build              # type-check + build
npm --prefix frontend run dev                # dev server, proxies /api to localhost:8080

# New migration (run inside backend/)
dotnet ef migrations add <Name> --project src/TrackOps.Infrastructure --startup-project src/TrackOps.Infrastructure --output-dir Persistence/Migrations
```

After generating a migration, read it, and update the tables in `tools/build_db_diagram.py`
(then regenerate `docs/db/schema.drawio` and its PNG) in the same commit. EF Core once merged two indexes on one column and dropped
a unique index without any warning.

## Architecture in one screen

```
Api            controllers, middleware (headers, errors, CSRF, rate limit), JWT + cookies
Application    use-case services, DTOs, interfaces; throws AppException with a stable code
Domain         entities, ZurichCalendar, WorkCalculator; no dependencies
Infrastructure EF Core + migrations, Redis stores, time API clients, auto-close job
Migrator       one-shot: migrations + least-privilege login
```

Dependencies point inward: Api → Application → Domain; Infrastructure implements Application's
interfaces.

Recording flow (`ShiftRecordingService.recordAsync`): replay stored response for a repeated
Idempotency-Key → cheap pre-check → read external time → one transaction: lock the open shift,
re-check, write, store the response.

Frontend: `features/<screen>/` holds a container page plus its presentational parts;
`shared/` holds the API client, the display clock and reusable UI (`ShiftTable`, `ShiftFilters`).

## C# conventions (follow them exactly)

- Fields: `m_Name`; readonly `r_Name`; static `s_Name`; static readonly `sr_Name`; constants
  `k_Name`. Auto-properties are PascalCase.
- Parameters: `i_Name` (in), `o_Name` (out), `io_Name` (ref). Lambda parameters are plain words.
- Private methods start lower-case (`findStoredAsync`); public methods PascalCase.
- Enums are prefixed with `e` (`eShiftStatus`). Local `const bool` flags are `v_Name` and are
  passed as `v_Name` / `!v_Name` instead of `true` / `false`.
- Explicit types, never `var`. Tabs for indentation. Braces on every block.
- One `return` per method (throwing is fine). Blank line after the local declarations and before
  the `return`.
- ASP.NET binds route, query and header values by name, so prefixed parameters need the name
  spelled out: `[FromRoute(Name = "shiftId")] long i_ShiftId`.
- Generated migrations are exempt.

## Frontend conventions

- Hebrew strings inline; wrap Latin text and numbers in `className="latin"`.
- CSS logical properties only (`margin-inline`, `inset-inline-start`, `text-align: start`).
- Colours only from `shared/styles/tokens.css`. Teal (`--accent`) is never text on a light
  surface; use `--accent-ink` there.
- Server state through TanStack Query; no global store.
- No `dangerouslySetInnerHTML`. The CSP forbids inline scripts and styles in markup.

## Commits

Small, logical commits on `feature/*` branches, merged into `main` with `--no-ff` (no squash).
English, imperative summary line, then only the sections that apply:

```
Summary line

Features:        (changes that span backend and frontend)
In Backend:
In Frontend:
```

## Gotchas met while building

- Package versions are pinned centrally in `backend/Directory.Packages.props`; keep them on the
  11.x line together with `global.json`, `dotnet-tools.json` and the image tags in the Dockerfile.
- Searches are mapped with `[HttpQuery]` (`TrackOps.Api.Http`), which wraps `HttpMethods.Query`.
  Do not write the string `"QUERY"` in backend code.
- Switching between the normal stack and the test overlay keeps Redis running, so state such as
  rate-limit counters carries over for up to a minute.
- On Windows, `localhost` is tried over IPv6 first and costs two seconds per request when only
  IPv4 is published. Tests use `127.0.0.1`.
- The stub clock keeps ticking, so minute counts in tests are compared with a one-minute tolerance.
- nginx `add_header` in a location replaces inherited headers instead of adding to them.
- The SQL Server image exists for linux/amd64 only; `compose.yaml` pins that platform. Every other
  image is multi-architecture. Do not add a dependency whose image or native package lacks ARM.
- The manager role is a setting: `MANAGER_USERNAME` (`Auth__ManagerUsername`), empty by default, so
  on the normal stack nobody is a manager. The test overlay sets it to `admin`.
- Sample data lives in the frontend only and is named `SAMPLE_*`: leave days (`features/formsData`),
  the request history (`features/requests`) and events (`features/calendar`). Never put sample
  rows in the database or the API. Everything else on those screens is real: hours come from the
  calendar API, and a filed request is stored through `POST /requests`.
- Day minutes are split from a shift's cumulative minutes (`WorkCalculator`), so the days of a shift
  add up to its own minutes. Keep it that way; `tests/test_day_boundaries.py` checks midnight,
  month ends and when "today" turns.
- Correction times must be UTC (`Z`); `EmployeeService` rejects anything else, because a time
  without a zone would be read in the server's own zone.
- Every endpoint must have a caller in the frontend. Remove an endpoint from all three API copies
  (rule 7) when its last caller goes.
- The API must stay stateless (rule 3). Anything shared between requests goes to Redis or SQL
  Server, never to a static field or an in-memory cache; that is what lets replicas be added.
