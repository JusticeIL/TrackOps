"""The day switch at Zurich midnight: no minute lost or double-counted, and "today" turns exactly at 00:00.

Zurich is UTC+1 in winter and UTC+2 in summer, so local midnight is 23:00Z or 22:00Z the day before.
"""
import pytest

from conftest import work_shift


def day(client, date):
	return client.get(f"/calendar/days/{date}").json()


def month_day(client, date):
	month = client.get(f"/calendar/months/{date[:7]}").json()
	return next(item for item in month["days"] if item["date"] == date)


def test_a_shift_across_midnight_keeps_every_minute(user, stub):
	# 23:59:30 to 00:01:00 local: 90 seconds, one whole minute. Floored per day it would be 0 + 0.
	shift = work_shift(user, stub, "2026-11-17T22:59:30Z", "2026-11-17T23:01:00Z")
	first, second = day(user, "2026-11-17"), day(user, "2026-11-18")

	assert shift["workedMinutes"] == 1
	assert first["workedMinutes"] == 0
	assert second["workedMinutes"] == 1
	# The shift is listed on both days it touches.
	assert [item["id"] for item in first["shifts"]] == [shift["id"]]
	assert [item["id"] for item in second["shifts"]] == [shift["id"]]


def test_a_night_shift_with_a_break_over_midnight_adds_up_to_its_own_minutes(user, stub):
	stub.set("2026-11-17T21:30:30Z")   # 22:30:30 local
	user.clock_in()
	stub.set("2026-11-17T22:50:00Z")   # break from 23:50 ...
	user.break_start()
	stub.set("2026-11-17T23:20:00Z")   # ... to 00:20 the next day
	user.break_end()
	stub.set("2026-11-18T01:30:30Z")   # 02:30:30 local
	shift = user.clock_out().json()
	first, second = day(user, "2026-11-17"), day(user, "2026-11-18")

	assert first["workedMinutes"] + second["workedMinutes"] == shift["workedMinutes"]
	assert first["breakMinutes"] + second["breakMinutes"] == shift["breakMinutes"]
	# The sums above are exact. These values are approximate only because the stub clock keeps
	# ticking, so each recording lands a few milliseconds after the time it was set to.
	assert first["workedMinutes"] == pytest.approx(79, abs=1)   # 89.5 minutes before midnight, 10 on break
	assert first["breakMinutes"] == pytest.approx(10, abs=1)
	assert shift["workedMinutes"] == pytest.approx(210, abs=1)


def test_a_shift_across_the_month_boundary_is_split_between_the_months(user, stub):
	# 30 November 23:30:30 to 1 December 00:31:00 local: 60.5 minutes, so 60 whole minutes even though
	# the ticking stub records each instant a few milliseconds late.
	shift = work_shift(user, stub, "2026-11-30T22:30:30Z", "2026-11-30T23:31:00Z")
	november, december = month_day(user, "2026-11-30"), month_day(user, "2026-12-01")

	assert shift["workedMinutes"] == 60
	assert november["workedMinutes"] + december["workedMinutes"] == 60
	assert november["workedMinutes"] == 29


def test_today_turns_at_zurich_midnight_in_winter_and_summer(user, stub):
	for before, after, old_date, new_date in [
		("2026-11-17T22:59:58Z", "2026-11-17T23:00:01Z", "2026-11-17", "2026-11-18"),   # UTC+1
		("2026-07-14T21:59:58Z", "2026-07-14T22:00:01Z", "2026-07-14", "2026-07-15"),   # UTC+2
	]:
		stub.set(before)
		assert day(user, "today")["date"] == old_date
		stub.set(after)
		assert day(user, "today")["date"] == new_date


def test_history_dates_end_at_midnight(user, stub):
	# One shift starts at 23:59:59 local, the next one at 00:00:00 local on the following day.
	late = work_shift(user, stub, "2026-11-17T22:59:59Z", "2026-11-17T22:59:59.800Z")
	early = work_shift(user, stub, "2026-11-17T23:00:00Z", "2026-11-17T23:30:00Z")

	until_the_17th = user.query("/shifts", {"to": "2026-11-17"}).json()["items"]
	from_the_18th = user.query("/shifts", {"from": "2026-11-18"}).json()["items"]

	assert [item["id"] for item in until_the_17th] == [late["id"]]
	assert [item["id"] for item in from_the_18th] == [early["id"]]
