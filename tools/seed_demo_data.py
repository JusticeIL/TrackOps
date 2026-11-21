"""Gives a demo employee six weeks of shift history, for trying the History and Calendar screens.

A new database is empty, and the application cannot create a shift in the past: the time of every
recording comes from the external time API. So this developer tool writes past shifts straight
into SQL Server. Its rows are marked TimeSource = 'demo-seed'; they are demo data, not recordings.

Nothing runs this automatically. Run it yourself while the stack is up:

    python tools/seed_demo_data.py

It needs only Python's standard library and Docker. Running it again replaces the rows it wrote
before and touches nothing else. "Today" is the Europe/Zurich date from the application's
GET /time, which comes from the external time source; this computer's clock is never read.
Local times are converted to UTC by SQL Server, so daylight-saving changes come out right.
"""
import argparse
import datetime
import http.cookiejar
import json
import pathlib
import re
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
MARK = "demo-seed"
MANAGER = "admin"
# A username's first sign-in sets its password. On a new database this becomes the password of
# "demo" and "admin"; it is a demo value, not a secret. Use --password if they already have another.
DEFAULT_PASSWORD = "demo-password"
# SQL Server's name for the time zone that Europe/Zurich belongs to.
SQL_ZONE = "W. Europe Standard Time"
DAYS_BACK = 42
MAX_SHIFT_HOURS = 16
CLOSED, AUTO_CLOSED = 1, 2
MANAGER_CORRECTION, AUTO_CLOSE = 0, 1
USERNAME_RULE = re.compile(r"[A-Za-z0-9._-]{3,32}")
# -I turns QUOTED_IDENTIFIER on, which SQL Server requires for writes to tables with filtered indexes.
SQLCMD = '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d TrackOps -b -I'


# ---------- the application: create the users and ask for today's date ----------

def call(opener, method, url, body=None, csrf=None, unauthorized_message=None):
	headers = {"Content-Type": "application/json"}
	if csrf:
		headers["X-CSRF-Token"] = csrf
	data = json.dumps(body).encode() if body is not None else None
	request = urllib.request.Request(url, data=data, headers=headers, method=method)
	try:
		with opener.open(request, timeout=30) as response:
			return json.loads(response.read())
	except urllib.error.HTTPError as error:
		if error.code == 401 and unauthorized_message:
			sys.exit(unauthorized_message)
		sys.exit(f"{method} {url} answered {error.code}: {error.read().decode(errors='replace')}")
	except urllib.error.URLError as error:
		sys.exit(f"Cannot reach {url} ({error.reason}). Is the stack running?")


def sign_in(api, username, password):
	"""Signs in through the real API, which creates the user on first sign-in. Returns the session."""
	opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
	csrf = call(opener, "GET", f"{api}/auth/csrf")["token"]
	refused = f"'{username}' already has a different password. Run again with --password and that password."

	call(opener, "POST", f"{api}/auth/session", {"username": username, "password": password}, csrf, refused)
	return opener


def zurich_today(api, opener):
	"""The current local date in Zurich, from the application's external time reading."""
	reading = call(opener, "GET", f"{api}/time")
	utc = datetime.datetime.fromisoformat(reading["utc"].replace("Z", "+00:00"))
	local = utc + datetime.timedelta(seconds=reading["offsetSeconds"])

	return local.date()


# ---------- the plan: which shifts the demo employee worked ----------

def at(day, clock):
	return datetime.datetime.combine(day, datetime.time.fromisoformat(clock))


def shift(start, end, breaks=(), status=CLOSED, corrected=False, audit=None):
	return {"start": start, "end": end, "breaks": breaks, "status": status, "corrected": corrected, "audit": audit}


def plan_shifts(today):
	"""Local Zurich times. Monday to Friday are work days; every second Saturday has a night shift."""
	shifts = []
	for days_back in range(DAYS_BACK, 0, -1):
		day = today - datetime.timedelta(days=days_back)
		next_day = day + datetime.timedelta(days=1)
		week = days_back // 7
		weekday = day.weekday()  # Monday is 0
		# A few minutes' difference from day to day, always the same for the same day.
		late = datetime.timedelta(minutes=days_back * 7 % 13, seconds=days_back * 17 % 60)

		if weekday == 0:
			# A plain day: 8 hours of work and a lunch break.
			shifts.append(shift(at(day, "08:00") + late, at(day, "16:30") + late, [(at(day, "12:00"), at(day, "12:30"))]))
		elif weekday == 1 and week == 1:
			# Forgot to clock out; the manager corrected the clock-out the next morning.
			start, recorded_end, corrected_end = at(day, "08:30") + late, at(day, "23:40"), at(day, "17:30")
			shifts.append(shift(start, corrected_end, [(at(day, "12:30"), at(day, "13:00"))], corrected=True, audit={
				"kind": MANAGER_CORRECTION, "by_manager": True, "at": at(next_day, "09:10"),
				"reason": "Forgot to clock out; time confirmed with the employee",
				"old": (start, recorded_end), "new": (start, corrected_end)}))
		elif weekday == 1:
			# A long day: overtime.
			shifts.append(shift(at(day, "08:30") + late, at(day, "18:15") + late, [(at(day, "12:30"), at(day, "13:00"))]))
		elif weekday == 2:
			# Two shifts on one day.
			shifts.append(shift(at(day, "07:30") + late, at(day, "11:30")))
			shifts.append(shift(at(day, "12:30"), at(day, "17:00") + late))
		elif weekday == 3 and week == 2:
			# Never clocked out: the system closed the shift at the maximum length.
			start = at(day, "08:00") + late
			end = start + datetime.timedelta(hours=MAX_SHIFT_HOURS)
			shifts.append(shift(start, end, status=AUTO_CLOSED, audit={
				"kind": AUTO_CLOSE, "by_manager": False, "at": end + datetime.timedelta(minutes=5),
				"reason": f"Open longer than {MAX_SHIFT_HOURS} hours; closed automatically",
				"old": (start, None), "new": (start, end)}))
		elif weekday == 3:
			# A short day.
			shifts.append(shift(at(day, "09:00") + late, at(day, "15:00") + late, [(at(day, "12:00"), at(day, "12:20"))]))
		elif weekday == 4:
			# Two breaks.
			shifts.append(shift(at(day, "08:00") + late, at(day, "17:30") + late,
				[(at(day, "10:15"), at(day, "10:30")), (at(day, "12:30"), at(day, "13:15"))]))
		elif weekday == 5 and week % 2 == 1:
			# A night shift across midnight. On a daylight-saving night it is 7 or 9 hours long.
			shifts.append(shift(at(day, "22:00") + late, at(next_day, "06:00") + late, [(at(next_day, "00:30"), at(next_day, "01:00"))]))

	return shifts


# ---------- the database ----------

def utc(local):
	"""SQL that turns a Zurich local time into the UTC instant the tables store."""
	if local is None:
		return "NULL"

	return f"CAST(CAST('{local.isoformat(timespec='seconds')}' AS datetime2(3)) AT TIME ZONE @zone AT TIME ZONE 'UTC' AS datetime2(3))"


def build_sql(username, shifts):
	lines = [
		"SET NOCOUNT ON;",
		"SET XACT_ABORT ON;",
		f"DECLARE @zone sysname = N'{SQL_ZONE}';",
		f"DECLARE @user int = (SELECT Id FROM Users WHERE Username = N'{username}');",
		f"DECLARE @manager int = (SELECT Id FROM Users WHERE Username = N'{MANAGER}');",
		"DECLARE @shift bigint;",
		"IF @user IS NULL OR @manager IS NULL BEGIN; THROW 50000, 'The demo user or the manager does not exist.', 1; END;",
		"BEGIN TRANSACTION;",
		# Breaks and audit rows of these shifts go with them (ON DELETE CASCADE).
		f"DELETE FROM Shifts WHERE UserId = @user AND TimeSource = N'{MARK}';",
	]
	for item in shifts:
		lines.append(
			"INSERT INTO Shifts (UserId, ClockInUtc, ClockOutUtc, Status, TimeSource, IsCorrected) "
			f"VALUES (@user, {utc(item['start'])}, {utc(item['end'])}, {item['status']}, N'{MARK}', {int(item['corrected'])});")
		lines.append("SET @shift = SCOPE_IDENTITY();")
		for start, end in item["breaks"]:
			lines.append(f"INSERT INTO Breaks (ShiftId, StartUtc, EndUtc) VALUES (@shift, {utc(start)}, {utc(end)});")
		audit = item["audit"]
		if audit:
			lines.append(
				"INSERT INTO ShiftAudits (ShiftId, ChangedByUserId, ChangedAtUtc, Kind, Reason, "
				"OldClockInUtc, OldClockOutUtc, NewClockInUtc, NewClockOutUtc) "
				f"VALUES (@shift, {'@manager' if audit['by_manager'] else 'NULL'}, {utc(audit['at'])}, {audit['kind']}, N'{audit['reason']}', "
				f"{utc(audit['old'][0])}, {utc(audit['old'][1])}, {utc(audit['new'][0])}, {utc(audit['new'][1])});")
	lines += ["COMMIT;", "GO", ""]

	return "\n".join(lines)


def run_sql(sql):
	"""Runs the script with sqlcmd inside the SQL Server container, which already holds the password."""
	command = ["docker", "compose", "exec", "-T", "sqlserver", "sh", "-c", SQLCMD]
	result = subprocess.run(command, input=sql, capture_output=True, text=True, encoding="utf-8", cwd=ROOT, check=False)

	if result.returncode != 0:
		sys.exit(f"SQL Server refused the script:\n{result.stdout}{result.stderr}")


def main():
	parser = argparse.ArgumentParser(description="Write demo shift history for one employee.")
	parser.add_argument("--url", default="http://127.0.0.1:8080", help="where the stack is published")
	parser.add_argument("--username", default="demo", help="the employee who gets the history")
	parser.add_argument("--password", default=DEFAULT_PASSWORD, help="password for the demo user and admin (set on their first sign-in)")
	arguments = parser.parse_args()
	api = arguments.url.rstrip("/") + "/api/v1"

	if not USERNAME_RULE.fullmatch(arguments.username):
		sys.exit("The username must be 3-32 letters, digits, dots, hyphens or underscores.")

	sign_in(api, MANAGER, arguments.password)
	today = zurich_today(api, sign_in(api, arguments.username, arguments.password))
	shifts = plan_shifts(today)

	run_sql(build_sql(arguments.username, shifts))
	print(f"Wrote {len(shifts)} demo shifts for '{arguments.username}', "
		f"{today - datetime.timedelta(days=DAYS_BACK)} to {today - datetime.timedelta(days=1)} (Zurich dates).")
	print(f"Sign in as '{arguments.username}' with the password '{arguments.password}'.")


if __name__ == "__main__":
	main()
