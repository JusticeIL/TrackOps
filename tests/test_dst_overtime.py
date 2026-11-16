"""Overtime and DST. Europe/Zurich in 2027: clocks go forward on 28 March (a 23 h day)
and back on 31 October (a 25 h day). Daily target in the test stack: 480 minutes.

Minute counts are compared with a one-minute tolerance, because the stub clock keeps ticking
for the few milliseconds each request takes.
"""
import pytest

from conftest import work_shift

TARGET = 480


def approx(minutes):
	return pytest.approx(minutes, abs=1)


def day(client, date):
	return client.get(f"/calendar/days/{date}").json()


def test_normal_day_overtime_is_worked_minus_target(user, stub):
	stub.set("2026-11-17T06:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T11:00:00Z")
	user.break_start()
	stub.set("2026-11-17T11:30:00Z")
	user.break_end()
	stub.set("2026-11-17T16:00:00Z")
	user.clock_out()

	summary = day(user, "2026-11-17")

	assert summary["dailyTargetMinutes"] == TARGET
	assert summary["workedMinutes"] == approx(570)      # 10 h minus a 30 min break
	assert summary["breakMinutes"] == approx(30)
	assert summary["overtimeMinutes"] == approx(90)


def test_a_short_day_has_zero_overtime_not_negative(user, stub):
	work_shift(user, stub, "2026-11-17T08:00:00Z", "2026-11-17T12:00:00Z")

	summary = day(user, "2026-11-17")

	assert summary["workedMinutes"] == approx(240)
	assert summary["overtimeMinutes"] == 0


def test_spring_forward_day_is_23_hours_long(user, stub):
	# Local midnight to local midnight on 28 March 2027: 23:00Z the day before to 22:00Z.
	work_shift(user, stub, "2027-03-27T23:00:00Z", "2027-03-28T22:00:00Z")

	summary = day(user, "2027-03-28")

	assert summary["workedMinutes"] == approx(23 * 60)
	assert summary["overtimeMinutes"] == approx(23 * 60 - TARGET)
	assert day(user, "2027-03-27")["workedMinutes"] <= 1
	assert day(user, "2027-03-29")["workedMinutes"] <= 1


def test_fall_back_day_is_25_hours_long(user, stub):
	# Local midnight to local midnight on 31 October 2027: 22:00Z the day before to 23:00Z.
	work_shift(user, stub, "2027-10-30T22:00:00Z", "2027-10-31T23:00:00Z")

	summary = day(user, "2027-10-31")

	assert summary["workedMinutes"] == approx(25 * 60)
	assert summary["overtimeMinutes"] == approx(25 * 60 - TARGET)
	assert day(user, "2027-10-30")["workedMinutes"] <= 1
	assert day(user, "2027-11-01")["workedMinutes"] <= 1


def test_night_shift_over_spring_forward_counts_real_elapsed_time(user, stub):
	# 22:00 to 06:00 on the wall clock, but only 7 real hours: the hour 02:00-03:00 does not exist.
	shift = work_shift(user, stub, "2027-03-27T21:00:00Z", "2027-03-28T04:00:00Z")

	assert shift["shiftMinutes"] == approx(7 * 60)
	assert day(user, "2027-03-27")["workedMinutes"] == approx(120)
	assert day(user, "2027-03-28")["workedMinutes"] == approx(300)


def test_night_shift_over_fall_back_counts_the_repeated_hour(user, stub):
	# 22:00 to 06:00 on the wall clock, but 9 real hours: the hour 02:00-03:00 happens twice.
	shift = work_shift(user, stub, "2027-10-30T20:00:00Z", "2027-10-31T05:00:00Z")

	assert shift["shiftMinutes"] == approx(9 * 60)
	assert day(user, "2027-10-30")["workedMinutes"] == approx(120)
	assert day(user, "2027-10-31")["workedMinutes"] == approx(420)
	assert day(user, "2027-10-31")["overtimeMinutes"] == 0


def test_month_totals_sum_the_days_and_average_over_days_worked(user, stub):
	work_shift(user, stub, "2026-11-16T07:00:00Z", "2026-11-16T17:00:00Z")   # 10 h -> 120 overtime
	work_shift(user, stub, "2026-11-17T07:00:00Z", "2026-11-17T16:00:00Z")   # 9 h  ->  60 overtime
	work_shift(user, stub, "2026-11-18T07:00:00Z", "2026-11-18T13:00:00Z")   # 6 h  ->   0 overtime

	month = user.get("/calendar/months/2026-11").json()
	by_date = {item["date"]: item for item in month["days"]}

	assert len(month["days"]) == 30
	assert by_date["2026-11-16"]["overtimeMinutes"] == approx(120)
	assert by_date["2026-11-17"]["overtimeMinutes"] == approx(60)
	assert by_date["2026-11-18"]["overtimeMinutes"] == 0
	assert month["totals"]["daysWorked"] == 3
	assert month["totals"]["workedMinutes"] == pytest.approx(25 * 60, abs=3)   # three shifts, up to a minute each
	assert month["totals"]["overtimeMinutes"] == pytest.approx(180, abs=2)
	assert month["totals"]["averageDailyOvertimeMinutes"] == pytest.approx(60, abs=1)


def test_month_cache_is_invalidated_by_a_new_shift(user, stub):
	work_shift(user, stub, "2026-11-16T07:00:00Z", "2026-11-16T15:00:00Z")
	before = user.get("/calendar/months/2026-11").json()["totals"]["workedMinutes"]

	work_shift(user, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")
	after = user.get("/calendar/months/2026-11").json()["totals"]["workedMinutes"]

	assert before == approx(480)
	assert after == approx(960)


def test_open_shift_counts_up_to_the_current_time(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T09:30:00Z")

	assert day(user, "today")["workedMinutes"] == approx(150)


@pytest.mark.parametrize("path", ["/calendar/months/2026-13", "/calendar/months/nope", "/calendar/days/2026-02-30", "/calendar/days/1999-01-01"])
def test_calendar_rejects_invalid_dates(user, stub, path):
	response = user.get(path)

	assert response.status_code == 400
	assert response.json()["code"] == "validation_failed"
