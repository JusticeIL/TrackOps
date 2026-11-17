# Security report

What was checked, what was found and fixed, and what proves it. "Test" names a pytest test in
`tests/`; every one of them runs against the real stack through the gateway.

## Summary

| Area | State | Proof |
|---|---|---|
| Input validation (server side) | Done | `test_sign_in_validates_input`, `test_invalid_queries_are_rejected`, `test_invalid_corrections_are_rejected`, `test_calendar_rejects_invalid_dates`, `test_invalid_requests_are_rejected` |
| SQL injection | Done | `test_sql_in_search_is_treated_as_text`, `test_like_wildcards_in_search_match_literally`, `test_sql_in_a_correction_reason_is_stored_as_text`, `test_script_and_sql_in_a_request_are_stored_as_plain_text` |
| XSS | Done | `test_script_in_a_correction_reason_is_stored_and_returned_as_plain_data`, `test_script_in_a_search_term_is_not_reflected_as_html`, CSP test below |
| CSRF | Done | Five tests named `test_*csrf*` in `test_security.py`, `test_filing_needs_the_csrf_token` |
| CORS | Done | `test_foreign_origin_gets_no_cors_permission`, `test_foreign_origin_preflight_is_not_approved`, `test_own_origin_preflight_allows_the_query_method` |
| Security headers | Done | `test_page_has_security_headers`, `test_api_has_security_headers_and_is_never_cached`, `test_https_mode_sets_secure_cookies_and_hsts` |
| Rate limiting | Done | `test_recording_is_rate_limited_per_user`, `test_rate_limit_of_one_user_does_not_affect_another` |
| Role-locked routes | Done | `test_manager_routes_reject_anonymous_callers`, `test_manager_routes_reject_employees` (4 routes each) |
| Session handling | Done | `test_sign_out_revokes_the_token_even_if_the_cookie_is_kept`, `test_signing_in_again_rotates_the_session`, `test_jwt_is_rejected_without_its_matching_session_cookie`, `test_tampered_jwt_is_rejected` |
| Least-privilege database login | Done | Created by the migrator; see below |
| Secrets only through the environment | Done | Audit table below; history check below |
| No exposed files or endpoints | Done | `test_health_endpoints_are_not_reachable_from_outside`; only the gateway publishes a port |
| Debug off in production | Done | `test_errors_do_not_leak_internals`, `test_responses_do_not_reveal_server_software` |
| Dependency vulnerabilities | Clean on 2026-11-17 | Scan output below |
| Security logging | Done | Event list below |

## Found and fixed while building

| # | Finding | Fix |
|---|---|---|
| 1 | The generated migration had no "one open break per shift" unique index. EF Core had merged it with the plain index on the same column, so the invariant would have been enforced only in code | Both indexes were given explicit names; the migration was regenerated and checked. `test_concurrent_break_starts_open_exactly_one_break` covers it |
| 2 | `X-Forwarded-For` could be supplied by the client and would have decided the rate-limit key for sign-in | The gateway overwrites the header with the real peer address instead of appending to it |
| 3 | nginx drops inherited `add_header` lines in any location that declares its own, which silently removes security headers | Page headers are included explicitly in the page location; the API sets its own headers in middleware, so they do not depend on nginx at all |
| 4 | After a container was rebuilt, the gateway kept its old address and requests failed | The gateway now re-resolves container names through Docker's DNS every few seconds |
| 5 | A CSRF token issued before sign-in would have stayed valid after it | The token's signature covers the session id, so it stops verifying when the session changes. `test_csrf_token_from_before_sign_in_stops_working_after_it` |
| 6 | Search text was used in `LIKE`, so `%` and `_` from the user would have acted as wildcards and matched everything | The pattern is escaped and still passed as a parameter. `test_like_wildcards_in_search_match_literally` |
| 7 | The API's default `Server` header named the web server | Disabled in Kestrel; nginx has `server_tokens off` |

## How each defence works

**SQL injection.** All data access goes through EF Core, which sends values as parameters. The
two hand-written queries (row locks) use `FromSql` with an interpolated string, which also parameterises. The only
SQL built by string is in the migrator (`CREATE LOGIN`, which cannot take parameters); its inputs
come from the environment, the name is checked against a strict pattern and the password is
quote-escaped.

**XSS.** The API returns JSON with `X-Content-Type-Options: nosniff`, never HTML. React escapes
everything it renders, and the code never uses `dangerouslySetInnerHTML`. The page's
Content-Security-Policy allows scripts and styles only from its own origin, with no
`unsafe-inline` or `unsafe-eval`, so an injected script would not run even if it reached the page.

**CSRF.** Cookies are `SameSite=Strict`. On top of that, every `POST`, `PATCH` and `DELETE` must
carry a token in the `X-CSRF-Token` header that matches the token cookie and whose HMAC signature
verifies for the current session id. `GET` and `QUERY` change nothing and need no token.

**Sessions.** The JWT and the session id are `HttpOnly` cookies, so page scripts cannot read
them. A valid JWT is not enough: its `sid` claim must equal the session cookie and the session
must exist in Redis. Sign-out deletes it. JWTs are signed with HMAC-SHA256; only that algorithm is
accepted.

**Authorization.** The manager policy is on the controller class, so a new manager action cannot
be added without it. Own-data endpoints filter by the caller's user id in the query itself.
The manager role is off by default: with `MANAGER_USERNAME` empty nobody is a manager and the
manager routes answer 403 to every caller (`test_without_a_configured_manager_everyone_is_an_employee`).

**Rate limiting.** Counters live in Redis and are incremented by one atomic script, so both
replicas share them. Limits per minute: sign-in 10 per IP; recording 30, search 60 and everything
else 240 per user. A rejected request gets `429` and `Retry-After`.

**Transport.** `Secure` cookies and HSTS are on unless `REQUIRE_HTTPS=false`. The local stack sets
that because it runs over plain HTTP; `test_https_mode_sets_secure_cookies_and_hsts` checks the
"on" position.

**Database.** The API's login has `SELECT`, `INSERT`, `UPDATE`, `DELETE` on the `dbo` schema and
nothing else: it cannot create, alter or drop anything. The SA password is given only to the
database container and the migrator.

**Errors.** Unexpected exceptions are logged in full on the server and answered with a generic
`500 internal_error`. No stack trace, type name or SQL text reaches the client. The environment
is `Production`; there is no Swagger page and no developer exception page. Source maps are not
built.

**Request size.** Bodies over 64 KB are refused by nginx and by Kestrel.

## Environment variables

Every setting that differs per environment, and every secret, comes from `.env`. Nothing secret
is in the repository; `.env` is gitignored and `.env.example` holds placeholders only.

| Variable | Secret | Used by | Purpose |
|---|---|---|---|
| `ACCEPT_EULA` | no | sqlserver | Accepts the SQL Server licence |
| `MSSQL_SA_PASSWORD` | **yes** | sqlserver, migrator | Admin login; never given to the API |
| `APP_DB_LOGIN` | no | migrator, backend | Name of the least-privilege login |
| `APP_DB_PASSWORD` | **yes** | migrator, backend | Its password |
| `REDIS_PASSWORD` | **yes** | redis, backend | Redis authentication |
| `JWT_KEY` | **yes** | backend | Signs session JWTs (32+ characters, checked at start-up) |
| `CSRF_KEY` | **yes** | backend | Signs CSRF tokens (32+ characters, checked at start-up) |
| `REQUIRE_HTTPS` | no | backend | `Secure` cookies and HSTS; default on |
| `HSTS_HEADER_VALUE` | no | gateway | HSTS for pages; empty locally |
| `PUBLIC_ORIGIN` | no | backend | The one origin CORS allows |
| `PUBLIC_PORT` | no | gateway | Host port |
| `MANAGER_USERNAME` | no | backend | The one username that signs in as a manager. Empty by default: nobody is a manager |
| `DAILY_TARGET_MINUTES`, `MAX_SHIFT_HOURS` | no | backend | Business rules |

The API refuses to start when a required setting is missing or a key is too short.

History check: `git log --all --name-only` contains no `.env` file, and a search of every commit
for the values in the local `.env` finds nothing.

## Dependency scan (.NET packages 2026-11-17, on .NET 11; npm 2026-11-16)

```
dotnet list backend/TrackOps.sln package --vulnerable --include-transitive
  -> no vulnerable packages in any of the five projects
npm --prefix frontend audit
  -> found 0 vulnerabilities
```

Direct dependencies, each with a reason: EF Core SQL Server (data access), StackExchange.Redis
(sessions, limits, cache), Microsoft.Extensions.Http.Resilience (timeout, retry, circuit breaker
for the time API), JwtBearer (token validation); React, React Router (lazy routes), TanStack Query
(server state), `@fontsource/heebo` (self-hosted font). The JWT package first added to the
infrastructure project turned out to be unused there and was removed.

## Security logging

Logs are structured JSON on standard output. Each security event has a `security_event` field:

| Event | When |
|---|---|
| `sign_in`, `sign_in_rejected` | Successful and rejected sign-ins |
| `csrf_rejected` | A state-changing request without a valid token |
| `forbidden` | An authenticated user called a manager route |
| `rate_limited` | A request over the limit |
| `time_api_failure`, `time_source_unavailable` | One provider failed; all providers failed |

Passwords, tokens and cookies are never logged.

## Not done

- No TLS inside the Docker network (API to SQL Server uses encryption with a self-signed server
  certificate that is not verified; API to Redis is unencrypted). Acceptable on a private network
  on one host; not for a multi-host deployment.
- No container image scanning, no web application firewall, no penetration test.
- No account lockout or multi-factor authentication: sign-in is a mock by design.
- Secrets are static values in `.env`; there is no secret manager and no rotation.
- The nginx master process runs as root inside its container (workers do not); the API and the
  migrator run as a non-root user.
