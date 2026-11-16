"""Static check: no code outside the time modules reads the system clock.

Recording must use the external time API only. To make that hard to break by accident, the wall
clock may be touched in exactly two places, and this test fails if it appears anywhere else:

  backend/src/TrackOps.Infrastructure/Time/   the external time provider, the display clock and
                                              SystemClock (token lifetimes only)
  frontend/src/shared/time/                   the display clock (monotonic ticking only)

It needs no running stack.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

BACKEND_PATTERN = re.compile(
	r"DateTime(Offset)?\.(Now|UtcNow|Today)\b|TimeProvider\.System\b|Environment\.TickCount|Stopwatch\b|GETDATE\(|GETUTCDATE\(|SYSUTCDATETIME\(|SYSDATETIME\(",
	re.IGNORECASE)
FRONTEND_PATTERN = re.compile(r"Date\.now\s*\(|new\s+Date\s*\(\s*\)|performance\.now\s*\(|performance\.timeOrigin")

BACKEND_ALLOWED = ("TrackOps.Infrastructure/Time/",)
FRONTEND_ALLOWED = ("shared/time/",)
SKIPPED_FOLDERS = {"bin", "obj", "node_modules", "dist"}


def find_clock_calls(folder, extensions, pattern, allowed):
	hits = []
	for path in sorted(folder.rglob("*")):
		relative = path.relative_to(ROOT).as_posix()
		if path.suffix not in extensions or SKIPPED_FOLDERS.intersection(path.parts) or any(part in relative for part in allowed):
			continue
		for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
			code = line.split("//")[0]
			if pattern.search(code):
				hits.append(f"{relative}:{number}: {line.strip()}")
	return hits


def test_backend_reads_the_clock_only_in_the_time_module():
	hits = find_clock_calls(ROOT / "backend" / "src", {".cs"}, BACKEND_PATTERN, BACKEND_ALLOWED)

	assert hits == [], "system clock used outside the time module:\n" + "\n".join(hits)


def test_frontend_reads_the_clock_only_in_the_time_module():
	hits = find_clock_calls(ROOT / "frontend" / "src", {".ts", ".tsx"}, FRONTEND_PATTERN, FRONTEND_ALLOWED)

	assert hits == [], "system clock used outside the time module:\n" + "\n".join(hits)


def test_the_allowed_modules_exist_and_the_check_can_find_a_violation():
	assert (ROOT / "backend/src/TrackOps.Infrastructure/Time/ExternalTimeProvider.cs").exists()
	assert (ROOT / "frontend/src/shared/time/serverClock.ts").exists()
	assert BACKEND_PATTERN.search("DateTime now = DateTime.UtcNow;")
	assert FRONTEND_PATTERN.search("const now = Date.now()")
	assert not FRONTEND_PATTERN.search("new Date(instant)")
