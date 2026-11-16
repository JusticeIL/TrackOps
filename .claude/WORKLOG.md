# Working with Claude Code — log

A short, honest record of how this project was built with Claude Code: what the tool did well,
where it had to be corrected, and where it corrected me. The plan that was approved before any
code is in `plans/01-api-contract-and-execution-plan.md`.

## How the work was organised

1. **One detailed prompt** with role, context, scope, constraints and success metrics, plus four
   inputs: the assignment e-mail, the Figma wireframes, the course coding standard (PDF) and the
   ReSharper settings.
2. **Plan mode with two approval gates.** Gate 1: the REST contract and its diagram. Gate 2: the
   execution plan (schema, time source, overtime rule, risks, cut order). Nothing was scaffolded
   or committed before both were approved.
3. **Build in phases**, each on a feature branch merged into `main` without squashing.
4. **Verification against the running stack**, not just by reading code.

## Where Claude Code helped

- **Reading before building.** It read all four inputs and checked the environment first, and
  reported what was missing (draw.io Desktop, a supported .NET SDK, Docker not running) instead of
  assuming.
- **Verifying claims.** It confirmed that HTTP `QUERY` is a published standard (RFC 10008), probed
  three public time APIs and found one of them down, and looked up exact image tags rather than
  guessing them.
- **A design detail I had not specified.** It rejected the time API endpoint that returns local
  time without an offset, because that is ambiguous in the hour that repeats when DST ends, and
  chose the endpoint that returns the offset.
- **Risk first.** `QUERY` passing through nginx was the top risk in the plan; it was tested with
  the first running stack, before the UI was built on it.
- **Database-enforced invariants** and a test that fires 20 concurrent clock-ins across both
  replicas and expects exactly one open shift.
- **The diagram** is generated from one endpoint table, so the diagram and the contract change
  together.
- **Tests** that control time through a stub: DST days, a slow or dead time source, recovery.

## Where I corrected Claude Code

| What it proposed | What I decided | Why |
|---|---|---|
| A POST fallback for every `QUERY` search (my own brief had asked for it) | No fallbacks: `QUERY` only | I asked what the extra POST routes were for; once explained, I preferred one way to search and fewer routes. The limit is documented |
| Self-signed TLS on localhost so `Secure` cookies and HSTS are real | Plain HTTP locally, with the reason stated in the README | Browsers reject self-signed certificates; production would use a real one |
| Logo "ring and check" (its recommendation) | Logo "Tick T" | My preference |
| 22 endpoints | 19 after removing the fallbacks | Follows from the first row |

## Where Claude Code corrected me

- I asked for the docs to say that .NET 10 was used "for the QUERY support". It pointed out that
  this is not accurate: .NET 10 has no built-in `QUERY` support; the method is mapped as a custom
  verb, which works on older versions too, and first-class support starts in .NET 11. The README
  says it the accurate way.

## Where Claude Code got something wrong and fixed it

| Mistake | How it was caught | Fix |
|---|---|---|
| The first generated migration silently lacked the "one open break per shift" unique index (EF Core merged two indexes on the same column) | It read the generated migration instead of trusting it | Named both indexes explicitly and regenerated |
| Added NuGet packages without versions and got .NET 11 packages that do not fit a .NET 10 target | Restore errors | Pinned 10.x versions centrally |
| Named a class `SignInResult`, which clashes with an ASP.NET type | Compile error | Renamed |
| Deleted the scaffolded frontend files while the Docker build was running, which broke that build | Build log | Added a minimal entry point, then built the real UI |
| Several long shell commands with embedded here-documents failed to parse | Shell error | Wrote the files directly instead |
| The first full test run was extremely slow | It read the gateway log and saw two seconds per request | `localhost` was being tried over IPv6 first; tests now use `127.0.0.1`. About 3 minutes instead of more than 10 |
| A few assertions demanded exact minute counts while the stub clock keeps ticking | Test failures off by one minute | A one-minute tolerance, with the reason in a comment |
| After rebuilding the API containers the gateway could keep stale addresses | Noticed while restarting containers | nginx now re-resolves names through Docker's DNS |
| The plan said "built-in antiforgery" | While implementing: it needs data-protection keys shared between replicas | A small HMAC-signed token; listed as a deviation in DECISIONS.md |

## What was cut or left unverified

- OpenTelemetry tracing was cut (it was first in the agreed cut order).
- Keyboard operation was built in and spot-checked, not exhaustively tested.
- The real time APIs were probed by hand; the automated tests use the stub so they do not depend
  on the internet.

## Practices that worked

- Gates before code: both design changes above were made on a diagram, not on written code.
- Asking "what is this for?" about anything in the plan I could not explain.
- Having it look at what it produced: the exported diagram, the generated migration, the running
  screens.
- Small commits in a fixed message format, so the history reads in order.
