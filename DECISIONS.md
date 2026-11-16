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
- Daily overtime = `max(0, worked − target)`. A short day counts zero; it does not cancel another
  day's overtime.
- Monthly overtime = the sum of the days. The dashboard compares today's overtime with the
  month's average daily overtime over days worked.
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
| Shift crossing midnight | One shift; hours split per local day |
| DST transitions | Covered above; tested for both 2027 transitions |
| Manager edits a shift someone else just changed | The row version in the request is compared under lock: `409 stale_version` |
| Correction that would cut off a break, overlap another shift, or lie in the future | `400` / `409 shift_overlap` |
| Two first sign-ins of one username at once | Unique index on the username; the loser re-reads the row |
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
- Another user's shift returns `404`, not `403`, so ids cannot be probed.

## 6. Authentication (mock, on purpose)

Any valid-looking credentials sign in; `admin` is the manager. **No password is stored or
compared, so there is nothing to hash.** Everything around it is real:

- The JWT (24 h) and an opaque session id travel in `HttpOnly`, `SameSite=Strict` cookies.
- A request is accepted only if the JWT is valid, its `sid` claim equals the session cookie, and
  the session exists in Redis. Sign-out deletes the session, which revokes the JWT at once on
  every replica. Signing in again rotates the session.
- CSRF: an HMAC-signed double-submit token bound to the session id.
- Input validation, rate limiting and role checks apply exactly as they would with real passwords.

Two roles only: Employee and Manager. Manager routes are locked at controller level.

## 7. Frontend

- **Hebrew only, right-to-left.** `dir="rtl"` and CSS logical properties throughout. English was
  left out deliberately: it would need a full left-to-right variant, not just translated strings.
- **Palette:** near-black, near-white and one teal accent. Teal on white is about 2:1, which fails
  WCAG for text, so teal is used for fills and on dark surfaces, with near-black text on it; a
  darker teal is used for text on light surfaces. Every pairing and its ratio is listed in
  `frontend/src/shared/styles/tokens.css`. Lighthouse accessibility: 100 on all five screens.
- **Font:** Heebo, bundled and served by our own nginx (Hebrew + Latin, three weights,
  `font-display: swap`). No CDN.
- **Reusable React components** through a container/presentational split. A container
  fetches and holds state; presentational components take props. One `ShiftTable`, one filter bar
  and one pagination control serve History, the Calendar day list and Employees.
- TanStack Query provides request de-duplication, caching, optimistic updates with rollback and
  parallel requests. React Router provides one lazily loaded chunk per screen.

### Screens considered and left out

| Dropped | Reason |
|---|---|
| Forms, New form | A document workflow unrelated to clocking shifts |
| Report sickness | Absence management is a separate product area |
| Support (three frames) | Static help content adds no engineering depth |
| Profile | With mock sign-in there is nothing to edit; sign-out moved to the header menu |
| Company-rules card | Static content |
| Language switcher | Hebrew only (see above) |
| Salary, payslips, vacation and sick-day tiles | Payroll is out of scope; the statistics screen became Shift History |
| Add / edit / delete shift on the Calendar | Employees cannot invent shifts; a manager corrects existing ones with an audit trail |
| Phone, e-mail and photo columns on Employees | No such data is collected; the screen shows shifts instead |

None of them has a sidebar entry, so there are no dead links.

## 8. Operations

- Two stateless API replicas behind nginx. All shared state is in SQL Server and Redis.
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
- Unit tests inside the .NET solution. All tests are black-box pytest, as required; the DST and
  overtime logic is covered through the API.
- Health checks on the API containers. The base image has no HTTP client for one; nginx detects a
  dead replica and uses the other.
- Tier 3: CDN, server-side rendering, real-user monitoring, third-party script loading, list
  virtualisation, slow-device and slow-network testing. File-upload limits and payment checks do
  not apply: there are no uploads or payments.

**Known limits:**

- Depends on free public time APIs with unknown rate limits and no SLA.
- Runs on .NET 11.0.0, the first release of a non-LTS line (section 5).
- No POST fallback for `QUERY` (section 5).
- In the hour that repeats when DST ends, a manager typing a wall-clock time gets the earlier of
  the two instants.
- Auto-close sets clock-out to clock-in + maximum. That value is derived, not read from the time
  API; the shift is flagged `AutoClosed` and audited so it is never mistaken for a recording.
- Failed recordings are not stored for idempotent replay; a retry after a 409 or 503 is evaluated
  again, which gives the same answer unless the situation changed.
- The gateway trusts the single proxy hop in front of the API. That is safe only because the API
  is not published outside the Docker network.
- Rate limiting uses fixed one-minute windows, so a burst across a window boundary can reach
  twice the limit.
- Mock sign-in: anyone can sign in as anyone, including `admin`.
- Keyboard operation was built in (native buttons, a native dialog, a combobox pattern, visible
  focus, a skip link) and spot-checked, not exhaustively tested with a screen reader.
