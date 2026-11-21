"""Static check: no code reads the computer's clock or the computer's own time zone, except where allowed.

Recording must use the external time API only, and every "today" must be a Europe/Zurich day of an
external reading. To make that hard to break by accident, each way of reading the machine's time
is allowed in at most one named file, and this test fails if it appears anywhere else:

  backend/src/TrackOps.Infrastructure/Time/DisplayClock.cs
      Stopwatch for the display clock's monotonic ticking, and SystemClock (token lifetimes only)
  frontend/src/shared/time/serverClock.ts
      performance.now for the display clock's monotonic ticking

Nothing may use the machine's local time zone, anywhere. It needs no running stack.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DISPLAY_CLOCK_CS = "backend/src/TrackOps.Infrastructure/Time/DisplayClock.cs"
SERVER_CLOCK_TS = "frontend/src/shared/time/serverClock.ts"
SKIPPED_FOLDERS = {"bin", "obj", "node_modules", "dist", ".venv", "__pycache__"}

# (pattern, the only files where it is allowed)
BACKEND_RULES = [
	(re.compile(r"DateTime(Offset)?\.(Now|UtcNow|Today)\b"), {DISPLAY_CLOCK_CS}),
	(re.compile(r"\bStopwatch\b"), {DISPLAY_CLOCK_CS}),
	(re.compile(r"TimeProvider\.System\b|Environment\.TickCount"), set()),
	(re.compile(r"TimeZoneInfo\.Local\b|DateTimeKind\.Local\b|\.ToLocalTime\(|\.ToUniversalTime\("), set()),
	(re.compile(r"GETDATE\(|GETUTCDATE\(|SYSDATETIME\(|SYSUTCDATETIME\(|SYSDATETIMEOFFSET\(|CURRENT_TIMESTAMP|defaultValueSql",
				re.IGNORECASE), set()),
]
FRONTEND_RULES = [
	(re.compile(r"\bDate\.now\s*\("), set()),
	(re.compile(r"\bnew\s+Date\s*\(\s*\)"), set()),
	(re.compile(r"\bperformance\.(now\s*\(|timeOrigin)"), {SERVER_CLOCK_TS}),
	# Getters and setters in the browser's own zone; the getUTC* and setUTC* forms are fine.
	(re.compile(r"\.(get|set)(FullYear|Month|Date|Day|Hours|Minutes|Seconds|Milliseconds|TimezoneOffset)\s*\("), set()),
	(re.compile(r"\.toLocale(Date|Time)?String\s*\("), set()),
]
PYTHON_RULES = [
	(re.compile(r"\bdatetime\.(now|utcnow|today)\s*\(|\bdate\.today\s*\(|\btime\.(time|time_ns|localtime)\s*\("), set()),
]
# The stub stands in for the external API, so it has a clock; this file names the patterns it looks for.
PYTHON_SKIPPED = ("tests/time-stub/", "tests/test_static_clock.py")
# "//" starts a comment, except inside a URL ("https://").
LINE_COMMENT = re.compile(r"(?<![:'\"])//")


def source_files(folder, extensions, skipped=()):
	for path in sorted((ROOT / folder).rglob("*")):
		relative = path.relative_to(ROOT).as_posix()
		if path.suffix in extensions and not SKIPPED_FOLDERS.intersection(path.parts) and not relative.startswith(skipped):
			yield relative, path.read_text(encoding="utf-8")


def code_of(line, comment):
	return LINE_COMMENT.split(line)[0] if comment == "//" else ("" if line.lstrip().startswith("#") else line)


def find_violations(folder, extensions, rules, comment, skipped=()):
	hits = []
	for relative, text in source_files(folder, extensions, skipped):
		for number, line in enumerate(text.splitlines(), start=1):
			code = code_of(line, comment)
			for pattern, allowed in rules:
				if pattern.search(code) and relative not in allowed:
					hits.append(f"{relative}:{number}: {line.strip()}")
	return hits


def formatters_without_zone(text):
	"""Every Intl.DateTimeFormat(...) must name a timeZone; otherwise it formats in the browser's zone."""
	missing = []
	for match in re.finditer(r"Intl\.DateTimeFormat\s*\(", text):
		depth, end = 1, match.end()
		while depth and end < len(text):
			depth += {"(": 1, ")": -1}.get(text[end], 0)
			end += 1
		if "timeZone" not in text[match.end():end]:
			missing.append(text.count("\n", 0, match.start()) + 1)
	return missing


def test_backend_reads_the_clock_only_where_allowed():
	hits = find_violations("backend/src", {".cs"}, BACKEND_RULES, "//")

	assert hits == [], "machine clock or local time zone used in the backend:\n" + "\n".join(hits)


def test_frontend_reads_the_clock_only_where_allowed():
	hits = find_violations("frontend/src", {".ts", ".tsx"}, FRONTEND_RULES, "//")

	assert hits == [], "machine clock or local time zone used in the frontend:\n" + "\n".join(hits)


def test_every_date_formatter_names_its_time_zone():
	missing = [f"{relative}:{line}" for relative, text in source_files("frontend/src", {".ts", ".tsx"})
			   for line in formatters_without_zone(text)]

	assert missing == [], "Intl.DateTimeFormat without a timeZone:\n" + "\n".join(missing)


def test_tools_and_tests_do_not_read_the_clock():
	hits = find_violations("tools", {".py"}, PYTHON_RULES, "#") + find_violations("tests", {".py"}, PYTHON_RULES, "#", PYTHON_SKIPPED)

	assert hits == [], "machine clock used in a tool or test:\n" + "\n".join(hits)


def test_the_allowed_files_exist_and_the_check_can_find_violations():
	assert (ROOT / DISPLAY_CLOCK_CS).exists()
	assert (ROOT / SERVER_CLOCK_TS).exists()

	def flags(rules, line):
		return any(pattern.search(code_of(line, "//")) for pattern, _ in rules)

	assert flags(BACKEND_RULES, "DateTime now = DateTime.UtcNow;")
	assert flags(BACKEND_RULES, "DateTime local = value.ToLocalTime();")
	assert flags(BACKEND_RULES, "TimeZoneInfo zone = TimeZoneInfo.Local;")
	assert flags(FRONTEND_RULES, "const now = Date.now()")
	assert flags(FRONTEND_RULES, "const hour = new Date(value).getHours()")
	assert flags(FRONTEND_RULES, "label = new Date(value).toLocaleDateString()")
	assert not flags(FRONTEND_RULES, "const day = new Date(instant).getUTCDay()")
	assert not flags(FRONTEND_RULES, "const url = 'https://example.org/' + Date.parse(text)")
	assert flags(FRONTEND_RULES, "const url = 'https://example.org/'; const now = Date.now()")
	assert formatters_without_zone("new Intl.DateTimeFormat('he-IL', { hour: '2-digit' })") == [1]
	assert formatters_without_zone("new Intl.DateTimeFormat('he-IL', { timeZone: ZONE })") == []
	assert any(pattern.search("stamp = datetime.now()") for pattern, _ in PYTHON_RULES)
