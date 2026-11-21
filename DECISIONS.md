# Decisions

What was decided, why, which edge cases were found and how they are handled, what was left out,
and what the known limits are. The guiding choice: few features, built deeply, each one
explainable in a sentence.

## 1. Time: the central decision

**Rule.** Every recorded timestamp comes from an external time API for Europe/Zurich. No browser
clock, no server clock.

| Decision | Why |
|---|---|
| One interface, `ITimeProvider`, is the only way to get a timestamp for recording | A single place to reason about, test and audit |
| Every recording makes a fresh external call; nothing is cached for recording | A cached time is a wrong time |
| Providers: `timeapi.io` first, `time.now` second | Two independent free services. `worldtimeapi.org` was refusing connections when this was built |
| From `timeapi.io` the endpoint that returns local time **plus the UTC offset** is used | Local time alone is ambiguous in the hour that repeats when DST ends |
| 2 s timeout per attempt, one retry on the primary, a circuit breaker per provider | Bounded waiting; a dead provider is not hammered |
| **Fail closed:** if no provider answers, the request fails with `503 time_source_unavailable` and nothing is written | Falling back to a local clock would break the rule silently |
| A reading that is not later than what the shift already holds is treated as a provider fault (`503`) | A clock that goes backwards is wrong; recording it would corrupt durations |
| Stored as UTC instants (`datetime2(3)`); Zurich local time is computed when needed | Instants are unambiguous; local days are a presentation and calculation concern |
| A static test fails if the system clock is read outside two modules | The rule is enforced by a check, not by memory |
| No request can carry a recording time | The four recording endpoints take no time in the body or headers; whatever a client sends is ignored. Tested |

**Display clock is not recording.** The dashboard clock is seeded from `GET /time` (the server's
latest external reading plus monotonic elapsed time) and then ticks in the browser with
`performance.now()`, re-syncing every minute. It exists so the user sees a running clock. It is
never sent to the server and never recorded. When the user clicks, the server asks the time API
again; the optimistic value on screen is replaced by the recorded one.

**Two stored times are not readings, and are marked as such.** Everything an employee records
(clock-in, break start, break end, clock-out) is an external reading. Two other writes cannot be:

- *A manager's correction* is a time a person typed, because the point of a correction is that
  the recorded reading was wrong. It never uses the browser or server clock. The shift is flagged
  `isCorrected`, the old and new values go to the audit trail, the audit entry itself is stamped
  with an external reading, "not in the future" is judged against the external time, and a
  correction is refused with 503 when the time source is down.
- *An automatically closed shift* ends at clock-in + the maximum length: an external reading plus
  a configured constant. The shift is flagged `AutoClosed` and audited the same way.

**The one place the server clock is used.** JWT lifetimes (`iat`, `exp`) use the server clock,
because the JWT validator also checks them against the server clock. That is `SystemClock`,
inside the time module, and it is not a recording.

**Consequence to know for a demo.** If both public time APIs are down, clock-in returns 503. That
is the design working.

## 2. Data model

**Stored:** UTC instants only — clock-in, clock-out, each break's start and end. **Never
stored:** durations. Shift, break and worked minutes are derived in code and in the reporting
views `v_ShiftReport` and `v_BreakReport`.

The alternative considered, "clock-in + shift minutes", was rejected: an open shift has no
minutes yet; breaks and corrections need real instants; DST arithmetic is only right on
instants; and a stored duration can drift from its endpoints.

**A shift holds no free text.** An optional clock-out note existed during the build and was
removed: the task does not ask for one, and it was one more input to validate, store, search and
escape. The free text the system stores is the reason of a manager's correction and the title
and content of a request (below).

**Requests and reports** are one table, `EmployeeRequests`: type, status, title, content, the
number of sick days (for a sickness report only, enforced by a check constraint) and the filing
time. The filing time comes from the external time API like every recorded time, so filing fails
closed with 503 when the time source is down. The id starts at 1001 and is the request number
people see.

**Demo data is not a recording.** The application cannot create a shift in the past, by design.
`tools/seed_demo_data.py` is a developer tool that writes six weeks of history for one demo
employee straight into the database, marked `TimeSource = 'demo-seed'`. It is never run
automatically, so a fresh installation starts empty.

The tables, keys, constraints and indexes are drawn in `docs/db/schema.png`.

**Invariants live in the database**, so they hold for any number of replicas:

| Invariant | Enforced by |
|---|---|
| One open shift per user | Filtered unique index on `Shifts(UserId) WHERE ClockOutUtc IS NULL` |
| One open break per shift | Filtered unique index on `Breaks(ShiftId) WHERE EndUtc IS NULL` |
| Clock-out after clock-in; break end after start | Check constraints |
| Status matches clock-out | Check constraint |
| A recording happens once per Idempotency-Key | Primary key `(UserId, Key)`, written in the same transaction |

Two rules span rows and cannot be a check constraint: "a break lies inside its shift" and
"corrected shifts do not overlap". They are checked inside the write transaction while the shift
row is locked (`UPDLOCK`), so a concurrent writer waits instead of reading stale state.

The API connects with a login that can only read and write rows. Schema changes run in a
separate one-shot `migrator` container, so the two replicas never race on migrations.

## 3. Overtime

- Daily target: 8 hours, configurable (`DAILY_TARGET_MINUTES`).
- A day is a Europe/Zurich local day: from one local midnight to the next.
- Worked time of a day = the part of each shift inside that day, minus the breaks inside it. A
  shift crossing midnight is split between the two days.
- Minutes are whole numbers, and a split must not lose one. Flooring each day separately would:
  23:59:30–00:01:00 is one minute, but 0 + 0 per day. So a shift's minutes are counted from its
  start up to each midnight and floored once, and a day gets the difference. The days of a shift
  always add up to exactly the minutes History shows for it (`tests/test_day_boundaries.py`).
- A day window is `[local midnight, next local midnight)`: a shift ending exactly at midnight does
  not touch the next day, and "today" turns at 00:00 Zurich time, in winter and in summer.
- Daily overtime = `max(0, worked − target)`. A short day counts zero; it does not cancel another
  day's overtime.
- Monthly overtime = the sum of the days. Overtime is shown on the Calendar, per day and per
  month. The dashboard shows only today's worked and break time; an overtime card was built and
  removed to keep the home screen about clocking in and out.
- DST needs no special case: a day is 23 h or 25 h long because its midnights are that far apart
  in UTC, and only real elapsed time is counted. A 22:00–06:00 night shift is 7 h in spring and
  9 h in autumn.

**Ignored on purpose:** Swiss public holidays, weekends, per-employee contracts, weekly legal
limits, and time-of-day premiums.

## 4. Edge cases and how each is handled

| Case | Handling |
|---|---|
| Double clock-in (double click, two tabs, two replicas) | The unique index lets one insert win; the others get `409 shift_already_open`. Tested with 20 parallel requests over both replicas |
| Clock-out with no open shift; break actions with nothing open | `409` with a specific code |
| Second break while one is open | `409 break_already_open`, also guarded by the unique index |
| Clock-out during a break | The break is closed at the same instant, in the same transaction |
| Retried request (lost response, flaky network) | The client sends an `Idempotency-Key`; the server replays the stored response and records nothing new |
| Same key sent twice at the same moment | One wins; the other sees the unique-key violation and replays the winner's response |
| Same key reused for a different action | `409 idempotency_key_reused` |
| Concurrent clock-out and break start | Both lock the shift row; the second one re-checks and fails cleanly |
| Forgotten clock-out | A background job closes shifts open longer than `MAX_SHIFT_HOURS` (16) at clock-in + maximum, marks them `AutoClosed`, writes an audit row. A manager corrects the time. A short-lived Redis lock stops two replicas from running a round at the same moment |
| Time API down while the job runs | The round is skipped; the job fails closed too |
| Shift crossing midnight or a month end | One shift; minutes split per local day without losing or doubling a minute |
| DST transitions | Covered above; tested for both 2027 transitions |
| Manager edits a shift someone else just changed | The row version in the request is compared under lock: `409 stale_version` |
| Correction that would cut off a break, overlap another shift, or lie in the future | `400` / `409 shift_overlap` |
| Two first sign-ins of one username at once | Unique index on the username; the loser re-reads the row and must match the winner's password |
| A request's text holds control, bidi-override or zero-width characters | Refused (400): they can make stored text display as something else. Text is normalised to Unicode NFC and one kind of line break; fields the server owns, and enum numbers, are refused |
| Session ended (signed out elsewhere, expired) | Next request returns 401 and the app returns to sign-in |
| Time API returns nonsense (bad JSON, wrong zone, absurd year) | Treated as a failure of that provider |

## 5. API

- REST over resources; errors as RFC 9457 `problem+json` with a stable `code`.
- Sign-in is modelled as a session resource: `POST`, `GET`, `DELETE /auth/session`.
- **`QUERY` for filtered lists, with no POST fallback.** The three searches (my history,
  employee autocomplete, all employees' shifts) use the HTTP `QUERY` method, RFC 10008, published
  in June 2026. The options were:

  | Option | Problem |
  |---|---|
  | `GET` with query-string filters | Names, dates and search text land in the URL, and so in browser history, proxy logs and access logs. URL length also limits the filter |
  | `POST /search` | Keeps the filter out of the URL, but `POST` means "this may change something": it is not safe, not idempotent, must not be retried automatically, and needs a CSRF token for what is only a read |
  | `QUERY` (chosen) | The filter is in the body, and the method is defined as safe and idempotent, so a lost request can simply be sent again and no CSRF token is needed |

  What `QUERY` does **not** do: it does not encrypt or hide the filter from anyone who can read
  the request itself. It keeps it out of URLs and logs; confidentiality on the wire is TLS's job.

  A POST fallback was planned and then removed by decision: one way to search, fewer routes.
  **Known limit:** a client or proxy that rejects `QUERY` cannot search. It was verified through
  nginx and Kestrel on both replicas, and browsers send it through `fetch`.
- **Runtime: .NET 11, chosen because of `QUERY`.** ASP.NET Core has long been able to route an
  arbitrary verb string, so `QUERY` also runs on .NET 9 and 10 (checked on .NET 9). .NET 11 is
  the first version that names the method in the framework: `HttpMethods.Query` and
  `HttpMethods.IsQuery`. The `[HttpQuery]` attribute (ours, a few lines, built on that constant),
  the CORS policy and the CSRF "safe method" check all use them, so the code contains no
  `"QUERY"` string of its own. .NET 11 ships no `[HttpQuery]` attribute and no `MapQuery`.
  **Trade-off:** .NET 11 is a standard-term support release, not LTS, and 11.0.0 was its first
  release, with no patch yet, when this was built. For a product meant to run for years the LTS
  line (.NET 10) with the verb mapped as a string would be the conservative choice.
- `PUT` is not used; no endpoint was invented to use it.
- A shift id that does not belong to the employee in the path returns `404`, not `403`, so ids cannot be probed.
- Every endpoint has a caller in the app. A `GET /shifts/{id}` existed and was removed: the app never
  asked for one shift on its own, and the history search already returns full shifts.

## 6. Authentication (simple on purpose)

**Sign-in and sign-up are one step ("trust on first use").** The first sign-in with a username
creates that user and stores a hash of the password given then. Every later sign-in must give the
same password, or it gets `401 invalid_credentials`; the user's shifts and requests stay with the
username. Usernames ignore case. There is no separate registration, password change or reset: the
task is a shift clock, and a real product would use the company's identity provider instead.

- An earlier version accepted any password every time, so anyone could enter as anyone. Storing
  the first password was a later decision; users created before it get their password at their
  next sign-in.
- **Hash:** PBKDF2-HMAC-SHA256 from .NET itself (no extra package), a random 16-byte salt per
  password, 600,000 iterations (OWASP's figure for this algorithm), stored as
  `pbkdf2-sha256$iterations$salt$key` so the count can be raised later without locking anyone
  out; keys compared in constant time. The test stack uses 1,000 iterations, because the suite
  signs in a few hundred new users.
- **Who was first is decided by the database:** the unique index on the username for a new user,
  and a conditional `UPDATE ... WHERE PasswordHash IS NULL` for an older one. Of ten simultaneous
  first sign-ins with ten passwords exactly one gets in (tested).

Everything around it is real:

- The JWT (24 h) and an opaque session id travel in `HttpOnly`, `SameSite=Strict` cookies.
- A request is accepted only if the JWT is valid, its `sid` claim equals the session cookie, and
  the session exists in Redis. Sign-out deletes the session, which revokes the JWT at once on
  every replica. Signing in again rotates the session.
- CSRF: an HMAC-signed double-submit token bound to the session id.
- Input validation, a sign-in rate limit (10 per minute per address), structured logging of
  refused sign-ins, and role checks.

Two roles only: Employee and Manager. Manager routes are locked at controller level.

**The manager role is switched off by default.** The task is a clock for workers, so a fresh
installation has no manager: `MANAGER_USERNAME` is empty, every sign-in is an employee and the
manager routes answer 403 to everyone. Naming a username in that setting switches the role on for
that user. The code was kept instead of deleted because it is finished and tested, and because
it is the only way to fix a shift the system closed automatically. The test stack sets
`admin`, so the manager routes stay covered; one extra test checks the default. A user's stored
role follows the setting at their next sign-in; a session issued before the change keeps its
role until it ends (24 hours at most).

## 7. Frontend

- **Hebrew only, right-to-left.** `dir="rtl"` and CSS logical properties throughout. English was
  left out deliberately: it would need a full left-to-right variant, not just translated strings.
- **Palette:** near-black, near-white and one teal accent. Teal on white is about 2:1, which fails
  WCAG for text, so teal is used for fills and on dark surfaces, with near-black text on it; a
  darker teal is used for text on light surfaces. Every pairing and its ratio is listed in
  `frontend/src/shared/styles/tokens.css`. Lighthouse accessibility: 100 on every screen.
  Teal marks what matters now: the primary button, the selected day, today, the menu icons and
  the active menu entry. A primary button loses its fill on hover and keeps a teal outline.
  Disabled controls are mid grey (`#808080`).
- **Font:** Heebo, bundled and served by our own nginx (Hebrew + Latin, three weights,
  `font-display: swap`). No CDN.
- **Reusable React components** through a container/presentational split. A container
  fetches and holds state; presentational components take props. One `ShiftTable`, one filter bar
  and one pagination control serve History, the Calendar day list and Employees.
- TanStack Query provides request de-duplication, caching, optimistic updates with rollback and
  parallel requests. React Router provides one lazily loaded chunk per screen.

### Screens beyond the clock: what is real and what is a sample

These screens were first embedded as pictures and then built for real, in the app's own three
colours. The rule for each part: real where the system already holds the data, a sample where it
does not, and never a sample that looks like the user's own data without being it.

- **Forms and data.** Hours this month, the weekly average and this week's chart are derived in
  the browser from two calendar months the API already serves (this month and the one before),
  in one pure function (`summariseWork`). No new endpoint was needed, and the Calendar screen
  shares the same cached answers. The month is compared with the same days of the previous
  month, because comparing half a month with a whole one would always look like a drop. The
  weekly average is over the 28 days before today. The chart's bars grow when the screen opens;
  with "reduce motion" set in the system they do not. Vacation and sick days are fixed sample
  numbers: there is no leave data. The salary and payslip cards and the form downloads were left
  out.
- **Requests and reports.** Filing is real: two forms (a new request, a sickness report) post to
  `POST /requests`, and the request is stored and listed. The five rows numbered 0001–0005 are a
  sample history kept in the frontend; stored requests are numbered from 1001, so the two never
  collide. Statuses are coloured green, amber and red, the one deliberate use of colours outside
  the palette. A filed request stays "in progress": there is no approval flow, because that
  needs a manager and the manager role is off. File attachments and links from the design were
  left out; uploads are a security surface of their own.
- **Events tab** under the calendar (the screen is titled "Shifts and events"): a sample list,
  the same for every day, scrolling inside its own frame. The Shifts tab shows the real shifts
  of the selected day.

### Screens considered and left out

| Dropped | Reason |
|---|---|
| Files and links on a request, the request guidelines pop-up | Uploads need their own limits and scanning; the guidelines are static text |
| Support (three frames) | Static help content adds no engineering depth |
| Profile | Nothing to edit beyond the username; sign-out moved to the header menu |
| Company-rules card | Static content |
| Language switcher | Hebrew only (see above) |
| Salary and payslip cards, form downloads | Payroll and exports are out of scope |
| Add / edit / delete shift on the Calendar | Employees cannot invent shifts; a manager corrects existing ones with an audit trail |
| Phone, e-mail and photo columns on Employees | No such data is collected; the screen shows shifts instead |

None of them has a sidebar entry, so there are no dead links.

## 8. Operations

- **Containers, built on the machine that runs them.** Running everything natively (the reviewer's
  own .NET and npm, no containers) was considered, so that each operating system would fit itself.
  It was rejected: SQL Server cannot be installed on macOS at all and Redis has no native Windows
  build, so a container-free setup cannot run on every system, while containers can. Nothing is
  shipped pre-built: the backend and frontend images are built locally from the Dockerfiles and
  match the local processor. Six of the seven base images are published for ARM as well as
  Intel/AMD. SQL Server is Intel/AMD only, so `compose.yaml` names that platform and an ARM
  machine runs it under emulation.
- **Replication is a property of the design, not of Docker.** Two API replicas run behind nginx,
  and more can be added, because of four choices:
  - the API holds no state in its own memory: no sessions, counters, caches or locks;
  - sessions, rate-limit counters, the calendar cache and the background-job lock are in Redis,
    so every replica sees the same ones;
  - the rules that must never break (one open shift per user, one open break per shift, one
    recording per Idempotency-Key) are enforced by the database, so no replica can disagree;
  - schema migrations run once in a separate container before any replica starts.

  The same holds outside containers: two processes on two ports behind any load balancer behave
  the same way. A third replica is two small edits (README, "More replicas"). It was tried:
  requests were shared evenly, 30 simultaneous clock-ins opened exactly one shift, and a session
  was valid on all three and revoked on all three at once. This is what gives availability (one
  replica can stop, restart or be replaced while the others serve) and maintainability (a new
  version can be rolled out one replica at a time).
- What is still a single point of failure: SQL Server, Redis and the gateway each run once. Making
  those redundant is an infrastructure matter (a database failover group, Redis replication, two
  gateways) and needs no change in the application.
- Rate limits are counted in Redis, so both replicas share them: sign-in 10/min per IP;
  recording 30/min, search 60/min and everything else 240/min per user.
- Read cache: a calendar month with no open shift is cached for 60 s per user; any write to that
  user's shifts invalidates it. Recorded timestamps are never cached.
- Compression: brotli/gzip for API responses, gzip for static files. Hashed assets are cached for
  a year; `index.html` is revalidated.
- **Local transport is plain HTTP.** A self-signed certificate would only produce browser
  warnings. `Secure` cookies and HSTS are on by default and switched off only in the local `.env`;
  production uses a real certificate at the proxy.
- **No CI/CD, by choice.** This is a local repository; there is no `.github` folder and
  no pipeline. The same commands a pipeline would run are in the README.

## 9. Deviations from the approved plan

| Plan | What was built | Why |
|---|---|---|
| ASP.NET's built-in antiforgery | A small HMAC-signed double-submit token | The built-in one needs data-protection keys shared between replicas, which means another package and key storage; 40 lines with one env secret do the same and are easier to explain |
| .NET 10 LTS | .NET 11 | Decided after the build, to use the framework's own `QUERY` support (section 5). Moving took a retarget, one deprecated EF Core call and a rebuild; all tests pass on it |
| snake_case column names | EF Core's default PascalCase | Less mapping code; the names in the plan were illustrative |
| "Background queue for slow jobs" | One hosted job (auto-close and idempotency clean-up) | Nothing else in scope is slow; a general queue would have no user |
| `GET /time` returns 503 when the source is down | It keeps serving from the last reading plus monotonic time, and returns 503 only if no reading was ever obtained | For display that is still correct, and the clock does not go blank during a short outage |

## 10. Not done, and known limits

**Not done (documented only):**

- OpenTelemetry tracing. It was last in the cut order and was cut. Logs are structured JSON.
- Correcting individual breaks. A manager corrects clock-in and clock-out only.
- Approving or rejecting a request, and an `Idempotency-Key` on filing one. A double submit is
  prevented in the UI only (the button is disabled while the request is sent).
- Unit tests inside the .NET solution. All tests are black-box pytest, as required; the DST and
  overtime logic is covered through the API.
- Health checks on the API containers. The base image has no HTTP client for one; nginx detects a
  dead replica and uses the other.
- Tier 3: CDN, server-side rendering, real-user monitoring, third-party script loading, list
  virtualisation, slow-device and slow-network testing. File-upload limits and payment checks do
  not apply: there are no uploads or payments.

**Known limits:**

- Depends on free public time APIs with unknown rate limits and no SLA.
- Built and verified on Windows (Intel/AMD). Not tested on a Mac with Apple Silicon, where SQL
  Server runs under emulation; a public report from October 2025 says macOS 26 broke that
  emulation for a SQL Server container. Not tested on Windows on ARM either.
- Runs on .NET 11.0.0, the first release of a non-LTS line (section 5).
- No POST fallback for `QUERY` (section 5).
- In the hour that repeats when DST ends, a manager typing a wall-clock time gets the earlier of
  the two instants.
- An open Calendar or Forms and data screen keeps showing yesterday as "today" after Zurich midnight
  until it is reloaded or a query refreshes; the dashboard refreshes every minute while a shift is open.
- The solution needs Visual Studio 2026 (18.7 or newer) or the `dotnet` command line. Visual
  Studio 2022 cannot load .NET 11 projects.
- Auto-close sets clock-out to clock-in + maximum. That value is derived, not read from the time
  API; the shift is flagged `AutoClosed` and audited so it is never mistaken for a recording.
- Failed recordings are not stored for idempotent replay; a retry after a 409 or 503 is evaluated
  again, which gives the same answer unless the situation changed.
- The gateway trusts the single proxy hop in front of the API. That is safe only because the API
  is not published outside the Docker network.
- Rate limiting uses fixed one-minute windows, so a burst across a window boundary can reach
  twice the limit.
- Anyone can claim a username nobody has used yet, including a configured manager username that
  has never signed in. A forgotten password cannot be reset: use another username, or reset the
  database (`docker compose down -v`).
- Ten failed sign-ins a minute per address slow guessing down; there is no account lockout.
- Keyboard operation was built in (native buttons, a native dialog, a combobox pattern, visible
  focus, a skip link) and spot-checked, not exhaustively tested with a screen reader.
