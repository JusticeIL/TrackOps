# TrackOps — hours alignment, card colours, trustworthy requests, passwords that stick (Sat 21/11)

## Context

Four requests the night before submission:

1. Forms and data: in Hebrew (RTL) the hour values sit on the left; they belong on the right.
2. Forms and data: the "average weekly hours" card should match the month card (teal card, black
   text), not be inverted (black card).
3. Requests and reports: parse the user's input strictly on the server, trust nothing the client
   sends, and present the stored request well in the UI.
4. Sign-in: today any password works for any username, every time. Change it: the password typed
   at a username's **first** sign-in is saved (hashed) for that user, and later sign-ins must use
   the same password. The user's data (shifts, requests) stays with the username. Document how
   signing in works wherever it is explained.

One dedicated branch, `feature/requests-hardening-and-passwords`, small commits in the required
format, merged into `main` with `--no-ff`. Trailer:
`Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

**Push (user's explicit instruction, 21/11):** only after everything passes and the branch is
merged into `main`, run `git push origin --all`. That publishes `main` and every `feature/*`
branch, unchanged (no rebase, no force, no date changes). Before pushing: `git status` clean,
`.env` and other ignored files not tracked, and `git fetch` + compare `origin/main` with local
`main`; if GitHub has commits that local `main` lacks, stop and report instead of forcing. If git
asks for credentials, the user signs in in Git's own window; Claude never handles the credentials.
CLAUDE.md rule 4 is reworded to match: no `gh`, no `.github/`, no CI files, and push only when
the owner asks.

## 1. Hours on the right (cause found)

`frontend/src/features/formsData/cards.tsx:27` renders `<p className="stat-value latin">`. The
`.latin` class (`shared/styles/base.css:84`) sets `direction: ltr`, so the whole paragraph is
left-to-right and its text starts on the left. Fix the same way as everywhere else in the app:
the paragraph stays RTL and only the number is LTR —
`<p className="stat-value"><span className="latin">{formatMinutes(minutes)}</span></p>`.
No other block element carries `latin` (checked with grep).

## 2. Weekly-average card colours

Both stat cards become the teal card with near-black text (9.4 : 1). The `tone` prop of
`StatCard` then has one value, so remove it with `.stat-card-dark` and `.stat-card-dark
.stat-value` from `formsData.css` (no dead code); `.stat-card-accent` rules move onto `.stat-card`.

## 3. Requests: strict parsing on the server, careful display in the UI

Today the server trims and length-checks the text, but it also accepts enum numbers
(`"type": 3`), silently ignores unknown fields such as `status` or `createdAtUtc`, and lets
invisible control and bidi-override characters through (they can make Hebrew/English text display
in a misleading order). The UI shows one generic error and never shows a request's content or
sick days.

**Backend** (`EmployeeRequestService.cs`, `Contracts.cs`, `JsonDefaults`):
- Reject unknown fields in the body of `POST /requests`
  (`[JsonUnmappedMemberHandling(Disallow)]` on `NewEmployeeRequest`): the status and the filing
  time are the server's, and sending them is a 400, not silently dropped.
- Enums are accepted as names only (`new JsonStringEnumConverter(allowIntegerValues: false)` in
  `JsonDefaults.Apply`; the frontend and all tests already send names). Responses are unchanged.
- Text normalisation, in this order, then validation on the result: Unicode NFC; `\r\n`/`\r` →
  `\n` (content only); trim. Rejected with a field error (400, nothing stored, nothing silently
  altered beyond that):
  - title: any line break or control character (single line);
  - content: control characters other than line break and tab;
  - both: bidi embedding/override/isolate controls (U+202A–U+202E, U+2066–U+2069) and zero-width
    characters used for spoofing (U+200B–U+200D, U+FEFF). The ordinary marks U+200E/U+200F
    (LRM/RLM), which Hebrew typing produces, stay allowed.
  - Existing length and sick-day rules stay; lengths are checked after normalisation.
- The text is stored and returned as plain data: never interpreted, linkified or rendered as
  HTML anywhere (React escapes it; JSON with `nosniff`).

**Frontend** (`features/requests/RequestDialog.tsx`, `RequestsPage.tsx`, `requestTexts.ts`):
- Field-level errors: the server's `fieldErrors` (keys `title`, `type`, `content`, `sickDays`,
  and `$.type`/`$`) shown in Hebrew under the matching field with `aria-invalid` and
  `aria-describedby`; a general error only for the rest.
- The table: user text cells get `dir="auto"` so a title that starts with English or a number
  lines up correctly in the RTL table; a sickness report shows its days ("דיווח מחלה · 2 ימים").
- The title becomes a button that opens a **request details** dialog (native `<dialog>`, like the
  existing ones): number, type, status badge, filing date and time (Zurich), sick days, title and
  content. Content keeps its line breaks (`white-space: pre-wrap`, `overflow-wrap: anywhere`,
  `dir="auto"`); empty content says "אין פירוט נוסף". Works for the sample rows too.

**Tests** (`tests/test_requests.py`): unknown field (`status`, `createdAtUtc`) → 400 (replaces
the current "ignored" test); `"type": 3` → 400; title with a line break → 400; content with a
bidi override (U+202E) or a zero-width space → 400; content keeps its line breaks and `\r\n`
becomes `\n`; NFC: a decomposed Hebrew/Latin string is stored composed; RLM inside Hebrew text is
accepted. The existing XSS/SQL tests stay.

## 4. Passwords bound to the first sign-in

**Behaviour:** sign-in and sign-up stay one step. The first sign-in with a username creates the
user and stores a hash of the password typed then. Every later sign-in must give the same
password, or it is refused with 401 `invalid_credentials` ("username or password is incorrect");
nothing about the user changes. Usernames stay case-insensitive. Users created before this change
have no password yet; their next sign-in sets it (same rule: first sign-in wins).

**Storage and code:**
- `Users.PasswordHash nvarchar(200) NULL` via migration `AddUserPasswordHash` (read it; update
  `tools/build_db_diagram.py`, regenerate `docs/db/schema.drawio` + PNG in the same commit).
- Hash: PBKDF2-HMAC-SHA256 from the framework (`Rfc2898DeriveBytes.Pbkdf2`, no new package),
  16-byte random salt, 32-byte key, stored self-describing as
  `pbkdf2-sha256$<iterations>$<salt>$<key>` (base64); compared with
  `CryptographicOperations.FixedTimeEquals`. Interface `IPasswordHasher` in
  `Application/Abstractions`, implementation in `Infrastructure` (crypto stays out of Domain and
  Application). Iterations: `AuthRules.PasswordIterations` (`Auth__PasswordIterations`), default
  600,000 (OWASP's figure for PBKDF2-SHA256); the test overlay sets 1,000, because the suite signs
  in a few hundred new users (verification always uses the count stored in the hash).
- `AuthService.SignInAsync`: validate → find user → (a) new user: insert with role and hash; on
  the unique-username race, re-read and verify against the winner's hash, so of two simultaneous
  first sign-ins with different passwords exactly one gets in; (b) user without a hash: set it with
  a conditional `UPDATE … WHERE Id = @id AND PasswordHash IS NULL` (`ExecuteUpdateAsync`); if
  another request won, verify against that hash; (c) otherwise verify. Only then rotate the session
  (unchanged). The invariant lives in the database (rule 3), not in memory.
- `Problems.InvalidCredentials()` (401, `invalid_credentials`). The existing
  `security_event=sign_in_rejected` log line covers it; the password is never logged or returned.
- Frontend: `describeError` gets the Hebrew message; the sign-in page hint explains the rule
  ("בכניסה הראשונה עם שם משתמש, הסיסמה שתזינו נשמרת לו; בכניסות הבאות צריך להזין את אותה סיסמה").
- `tools/seed_demo_data.py`: signs in `demo` and `admin` with `demo-password` (which now becomes
  their password on a fresh database); add `--password`, and a clear message if the username
  already has another password. `tools/lighthouse_a11y.py` already uses the tests' password.

**Tests** (`tests/test_auth.py`): the first password sticks (same password again → 200, another →
401 `invalid_credentials`); data stays with the user across sign-ins (a filed request is still
listed after signing in again); ten simultaneous first sign-ins of one new username with ten
different passwords → exactly one 200; neither the sign-in nor the session response contains the
password or a hash.

## Documentation (the sign-in rule explained everywhere it appears)

- README: "Check that it runs" (how sign-in works now), demo data (`demo` / `demo-password`),
  troubleshooting row "שם המשתמש או הסיסמה שגויים" → use the first password, or a new username,
  or reset the database.
- `docs/api/API.md`: `POST /auth/session` (first sign-in registers; 401 `invalid_credentials`),
  the error-code table, `POST /requests` rules (names only, unknown fields rejected, characters
  refused).
- DECISIONS: section 6 rewritten ("trust on first use" sign-in, why PBKDF2 from the framework,
  why no password reset); known limits (anyone can claim an unused username; no change/reset; a
  forgotten password means a new username or a database reset); requests input rules.
- `docs/SECURITY-REPORT.md`: password storage (algorithm, salt, iterations, fixed-time compare),
  new tests in the summary table, input-handling rows for requests.
- CLAUDE.md gotchas: password rule and the test overlay's iteration count; request text rules.
- `.claude/WORKLOG.md`: this change of decision (passwords now stored); plan saved as
  `.claude/plans/03-requests-hardening-and-passwords.md`.

## Verification

- `dotnet build` 0 warnings (also with VS 2026's MSBuild); `npm --prefix frontend run build`;
  `test_static_clock.py`.
- Test stack: full `pytest` (all pass, the 2 stack-mode tests skipped); new request and auth
  tests run several times in a row.
- Browser: Forms and data shows hours on the right and two teal cards; Requests: file a request
  with a multi-line, mixed Hebrew/English description, open its details, see a field error for an
  invalid input; sign-in with a wrong password shows the Hebrew message.
- Lighthouse 100 on every screen; NuGet/npm scans clean.
- End on the normal stack; re-run the demo seed only if the user wants `demo`'s password set to
  `demo-password` (their local `demo` user gets whatever password they type first otherwise).
- Then merge, run the pre-push checks above, `git push origin --all`, and confirm with
  `git ls-remote --heads origin` that `main` and the feature branches are on GitHub at the local
  commits.
