"""The rule of the system: clock-in, break start, break end and clock-out are recorded with the
external time API's reading and nothing else. No browser clock, no server clock, no client value.

These tests set the stub time API to 2031. No real clock involved in the test run (this machine,
the containers, the database server) shows that year, so a recorded instant in 2031 can only have
come from the external source.
"""
import time

import pytest

from conftest import Client, work_shift

ACTIONS = ["clock_in", "break_start", "break_end", "clock_out"]
PATHS = {
	"clock_in": "/shifts",
	"break_start": "/shifts/current/breaks/current/start",
	"break_end": "/shifts/current/breaks/current/end",
	"clock_out": "/shifts/current/clock-out",
}
# Every name a client might try, in the body and in headers.
CLIENT_TIME = "2020-01-01T00:00:00Z"
FAKE_BODY = {name: CLIENT_TIME for name in ("clockInUtc", "clockOutUtc", "startUtc", "endUtc", "utc", "time", "timestamp", "now")}
FAKE_HEADERS = {"Date": "Wed, 01 Jan 2020 00:00:00 GMT", "X-Timestamp": CLIENT_TIME, "X-Client-Time": CLIENT_TIME}


def approx(minutes):
	return pytest.approx(minutes, abs=1)


def prepare(client, stub, action):
	"""Brings the user to the state in which `action` is the valid next step."""
	if action != "clock_in":
		stub.set("2031-05-05T05:00:00Z")
		assert client.clock_in().status_code == 201
	if action == "break_end":
		stub.set("2031-05-05T09:00:00Z")
		assert client.break_start().status_code == 201


def current_state(client):
	response = client.get("/shifts/current")
	return response.json() if response.status_code == 200 else None


def test_all_four_recorded_instants_are_the_external_readings(user, stub):
	stub.set("2031-05-05T05:00:00Z")
	opened = user.clock_in().json()
	stub.set("2031-05-05T09:00:00Z")
	started = user.break_start().json()
	stub.set("2031-05-05T09:30:00Z")
	ended = user.break_end().json()
	stub.set("2031-05-05T13:00:00Z")
	closed = user.clock_out().json()
	stored = user.query("/shifts").json()["items"][0]

	assert opened["clockInUtc"].startswith("2031-05-05T05:00:0")
	assert started["break"]["startUtc"].startswith("2031-05-05T09:00:0")
	assert ended["break"]["endUtc"].startswith("2031-05-05T09:30:0")
	assert closed["clockOutUtc"].startswith("2031-05-05T13:00:0")
	# What was answered is what was stored.
	assert stored["clockInUtc"] == opened["clockInUtc"]
	assert stored["clockOutUtc"] == closed["clockOutUtc"]
	assert stored["break"]["startUtc"] == started["break"]["startUtc"]
	assert stored["break"]["endUtc"] == ended["break"]["endUtc"]
	# Eight hours on the external clock, though the test itself took well under a minute.
	assert stored["shiftMinutes"] == approx(480)
	assert stored["breakMinutes"] == approx(30)


def test_break_closed_by_clock_out_gets_the_external_reading(user, stub):
	stub.set("2031-05-05T05:00:00Z")
	user.clock_in()
	stub.set("2031-05-05T09:00:00Z")
	user.break_start()
	stub.set("2031-05-05T12:00:00Z")

	closed = user.clock_out().json()

	assert closed["break"]["endUtc"].startswith("2031-05-05T12:00:0")
	assert closed["break"]["endUtc"] == closed["clockOutUtc"]


@pytest.mark.parametrize("action", ACTIONS)
def test_a_time_sent_by_the_client_is_ignored(user, stub, action):
	prepare(user, stub, action)
	stub.set("2031-05-05T11:00:00Z")

	response = user.record(PATHS[action], json=FAKE_BODY, headers=FAKE_HEADERS)

	assert response.status_code in (200, 201), response.text
	assert "2020-01-01" not in response.text
	shift = response.json()
	recorded = {
		"clock_in": shift["clockInUtc"],
		"break_start": shift["break"]["startUtc"] if shift["break"] else None,
		"break_end": shift["break"]["endUtc"] if shift["break"] else None,
		"clock_out": shift["clockOutUtc"],
	}[action]
	assert recorded.startswith("2031-05-05T11:00:0")


@pytest.mark.parametrize("action", ACTIONS)
def test_each_recording_fails_closed_and_writes_nothing(user, stub, action):
	prepare(user, stub, action)
	before = current_state(user)
	stub.mode("down", "down")

	response = getattr(user, action)()

	assert response.status_code == 503
	assert response.json()["code"] == "time_source_unavailable"
	stub.mode("ok", "ok")
	time.sleep(1.2)
	assert current_state(user) == before, "without an external reading nothing may be recorded"


@pytest.mark.parametrize("action", ACTIONS)
def test_each_recording_fails_closed_when_the_source_answers_nonsense(user, stub, action):
	prepare(user, stub, action)
	before = current_state(user)
	stub.mode("garbage", "garbage")

	response = getattr(user, action)()

	assert response.status_code == 503
	stub.mode("ok", "ok")
	assert current_state(user) == before


def test_there_is_no_break_endpoint_that_takes_a_time(user, stub):
	prepare(user, stub, "break_start")

	# The collection itself is not writable: a break cannot be created with chosen times.
	response = user.record("/shifts/current/breaks", json={"startUtc": CLIENT_TIME, "endUtc": CLIENT_TIME})

	assert response.status_code in (404, 405)
	assert current_state(user)["break"] is None


def test_a_correction_is_stamped_and_judged_by_the_external_clock(manager, stub):
	employee = Client()
	shift = work_shift(employee, stub, "2031-05-05T05:00:00Z", "2031-05-05T15:00:00Z")
	path = f"/employees/{shift['userId']}/shifts/{shift['id']}"
	stub.set("2031-05-06T08:00:00Z")

	# 2031 is the future for every real clock here; it is accepted because it is the past for the external one.
	accepted = manager.request("PATCH", path, json={"clockOutUtc": "2031-05-05T14:00:00Z", "reason": "left earlier", "rowVersion": shift["rowVersion"]})
	too_late = manager.request("PATCH", path, json={
		"clockOutUtc": "2031-05-06T09:00:00Z", "reason": "after the external now", "rowVersion": accepted.json()["rowVersion"]})
	audit = manager.get(path + "/audit").json()["items"]

	assert accepted.status_code == 200
	assert too_late.status_code == 400
	assert len(audit) == 1
	assert audit[0]["changedAtUtc"].startswith("2031-05-06T08:00:0")


def test_a_correction_is_refused_when_the_time_source_is_down(manager, stub):
	employee = Client()
	shift = work_shift(employee, stub, "2031-05-05T05:00:00Z", "2031-05-05T15:00:00Z")
	path = f"/employees/{shift['userId']}/shifts/{shift['id']}"
	stub.mode("down", "down")

	response = manager.request("PATCH", path, json={"clockOutUtc": "2031-05-05T14:00:00Z", "reason": "left earlier", "rowVersion": shift["rowVersion"]})

	assert response.status_code == 503
	stub.mode("ok", "ok")
	time.sleep(1.2)
	assert manager.get(path + "/audit").json()["items"] == []
	assert employee.query("/shifts").json()["items"][0]["clockOutUtc"] == shift["clockOutUtc"]
