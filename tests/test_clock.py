"""Clock in/out and breaks: recorded times come from the (stub) external time API, and the rules hold."""
import uuid

import pytest

from conftest import work_shift


def approx(minutes):
	"""The stub clock keeps ticking during each request, so minute counts may be one minute short."""
	return pytest.approx(minutes, abs=1)


def test_clock_in_and_out_record_the_external_time(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	opened = user.clock_in()
	stub.set("2026-11-17T15:30:00Z")
	closed = user.clock_out()

	assert opened.status_code == 201
	assert opened.headers["Location"].endswith(f"/shifts/{opened.json()['id']}")
	assert opened.json()["clockInUtc"].startswith("2026-11-17T07:00:0")
	assert opened.json()["status"] == "Open"
	assert closed.status_code == 200
	assert closed.json()["clockOutUtc"].startswith("2026-11-17T15:30:0")
	assert closed.json()["status"] == "Closed"
	assert closed.json()["shiftMinutes"] == approx(510)
	assert closed.json()["workedMinutes"] == approx(510)


def test_client_cannot_supply_the_clock_in_time(user, stub):
	stub.set("2026-11-17T07:00:00Z")

	response = user.record("/shifts", json={"clockInUtc": "2020-01-01T00:00:00Z"})

	assert response.status_code == 201
	assert response.json()["clockInUtc"].startswith("2026-11-17T07:00:0")


def test_second_clock_in_is_rejected(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	assert user.clock_in().status_code == 201

	again = user.clock_in()

	assert again.status_code == 409
	assert again.json()["code"] == "shift_already_open"


def test_clock_out_without_an_open_shift_is_rejected(user, stub):
	response = user.clock_out()

	assert response.status_code == 409
	assert response.json()["code"] == "no_open_shift"


def test_current_shift_is_204_when_none_is_open(user, stub):
	assert user.get("/shifts/current").status_code == 204

	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()

	current = user.get("/shifts/current")
	assert current.status_code == 200
	assert current.json()["status"] == "Open"


def test_breaks_are_recorded_and_subtracted(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T10:00:00Z")
	started = user.break_start()
	stub.set("2026-11-17T10:45:00Z")
	ended = user.break_end()
	stub.set("2026-11-17T16:00:00Z")
	closed = user.clock_out().json()

	assert started.status_code == 201
	assert ended.status_code == 200
	assert len(closed["breaks"]) == 1
	assert closed["breaks"][0]["minutes"] == approx(45)
	assert closed["shiftMinutes"] == approx(540)
	assert closed["workedMinutes"] == approx(495)


def test_only_one_break_can_be_open(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T09:00:00Z")
	assert user.break_start().status_code == 201

	second = user.break_start()

	assert second.status_code == 409
	assert second.json()["code"] == "break_already_open"


def test_break_actions_need_an_open_shift_and_an_open_break(user, stub):
	assert user.break_start().json()["code"] == "no_open_shift"
	assert user.break_end().json()["code"] == "no_open_shift"

	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T08:00:00Z")

	assert user.break_end().json()["code"] == "no_open_break"


def test_clock_out_closes_an_open_break_at_the_same_instant(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T12:00:00Z")
	user.break_start()
	stub.set("2026-11-17T12:30:00Z")

	closed = user.clock_out().json()

	assert closed["breaks"][0]["endUtc"] == closed["clockOutUtc"]


def test_shift_crossing_midnight_is_one_shift_split_over_two_days(user, stub):
	# 22:00 to 02:00 Zurich winter time (UTC+1).
	shift = work_shift(user, stub, "2026-11-17T21:00:00Z", "2026-11-18T01:00:00Z")

	first_day = user.get("/calendar/days/2026-11-17").json()
	second_day = user.get("/calendar/days/2026-11-18").json()

	assert shift["shiftMinutes"] == approx(240)
	assert first_day["workedMinutes"] == approx(120)
	assert second_day["workedMinutes"] == approx(120)
	assert [item["id"] for item in first_day["shifts"]] == [shift["id"]]
	assert [item["id"] for item in second_day["shifts"]] == [shift["id"]]


def test_a_time_reading_that_goes_backwards_is_treated_as_a_fault(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T06:00:00Z")

	response = user.clock_out()

	assert response.status_code == 503
	assert response.json()["code"] == "time_source_unavailable"
	assert user.get("/shifts/current").json()["status"] == "Open"


def test_another_users_shift_is_not_found(user, stub):
	from conftest import Client
	shift = work_shift(user, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")
	stranger = Client()

	assert user.get(f"/shifts/{shift['id']}").status_code == 200
	assert stranger.get(f"/shifts/{shift['id']}").status_code == 404


def test_recent_activity_lists_events_newest_first(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T10:00:00Z")
	user.break_start()
	stub.set("2026-11-17T10:30:00Z")
	user.break_end()
	stub.set("2026-11-17T15:00:00Z")
	user.clock_out()

	items = user.get("/shifts/activity?limit=10").json()["items"]

	assert [item["type"] for item in items] == ["ClockOut", "BreakEnd", "BreakStart", "ClockIn"]


def test_recording_requires_an_idempotency_key(user, stub):
	response = user.request("POST", "/shifts")

	assert response.status_code == 400
	assert response.json()["code"] == "idempotency_key_required"


def test_retry_with_the_same_key_replays_and_records_once(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	key = str(uuid.uuid4())

	first = user.clock_in(key)
	stub.set("2026-11-17T09:00:00Z")
	retry = user.clock_in(key)

	assert first.status_code == 201
	assert retry.status_code == 201
	assert retry.headers.get("Idempotency-Replayed") == "true"
	assert retry.json() == first.json()
	assert user.query("/shifts").json()["totalItems"] == 1


def test_a_key_cannot_be_reused_for_a_different_action(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	key = str(uuid.uuid4())
	user.clock_in(key)
	stub.set("2026-11-17T09:00:00Z")

	response = user.clock_out(key)

	assert response.status_code == 409
	assert response.json()["code"] == "idempotency_key_reused"
