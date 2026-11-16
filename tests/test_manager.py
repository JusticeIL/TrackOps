"""Manager-only routes: role lock, corrections with an audit trail, and the forgotten clock-out."""
import time

import pytest
import requests

from conftest import BASE, TIMEOUT, Client, work_shift

MANAGER_ROUTES = [
	("QUERY", "/employees"),
	("QUERY", "/employees/shifts"),
	("PATCH", "/employees/1/shifts/1"),
	("GET", "/employees/1/shifts/1/audit"),
]


@pytest.mark.parametrize("method, path", MANAGER_ROUTES)
def test_manager_routes_reject_anonymous_callers(stub, method, path):
	anonymous = Client(sign_in=False)
	anonymous.refresh_csrf()

	response = anonymous.request(method, path, json={})

	assert response.status_code == 401
	assert response.json()["code"] == "unauthenticated"


@pytest.mark.parametrize("method, path", MANAGER_ROUTES)
def test_manager_routes_reject_employees(user, method, path):
	response = user.request(method, path, json={})

	assert response.status_code == 403
	assert response.json()["code"] == "forbidden"


def test_employee_autocomplete_matches_by_prefix(manager, stub):
	employee = Client("zz" + Client(sign_in=False).username)

	found = manager.query("/employees", {"search": employee.username[:10], "limit": 5}).json()["items"]

	assert employee.username in [item["username"] for item in found]
	assert all(item["username"].startswith(employee.username[:10]) for item in found)


def test_manager_sees_all_shifts_and_can_filter_by_employee(manager, stub):
	first, second = Client(), Client()
	work_shift(first, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")
	work_shift(second, stub, "2026-11-17T08:00:00Z", "2026-11-17T16:00:00Z")

	only_first = manager.query("/employees/shifts", {"userId": first.user["id"]}).json()
	by_name = manager.query("/employees/shifts", {"search": second.username}).json()

	assert only_first["totalItems"] == 1
	assert only_first["items"][0]["username"] == first.username
	assert by_name["totalItems"] == 1
	assert by_name["items"][0]["username"] == second.username


def test_correction_changes_the_times_and_writes_an_audit_row(manager, stub):
	employee = Client()
	shift = work_shift(employee, stub, "2026-11-17T07:00:00Z", "2026-11-17T18:00:00Z")
	path = f"/employees/{shift['userId']}/shifts/{shift['id']}"

	corrected = manager.request("PATCH", path, json={
		"clockOutUtc": "2026-11-17T16:00:00Z", "reason": "forgot to clock out on time", "rowVersion": shift["rowVersion"]})
	audit = manager.get(path + "/audit").json()["items"]

	assert corrected.status_code == 200
	assert corrected.json()["clockOutUtc"].startswith("2026-11-17T16:00:00")
	assert corrected.json()["isCorrected"] is True
	assert corrected.json()["rowVersion"] != shift["rowVersion"]
	assert len(audit) == 1
	assert audit[0]["kind"] == "ManagerCorrection"
	assert audit[0]["changedBy"]["username"] == "admin"
	assert audit[0]["reason"] == "forgot to clock out on time"
	assert audit[0]["oldClockOutUtc"].startswith("2026-11-17T18:00:0")
	assert audit[0]["newClockOutUtc"].startswith("2026-11-17T16:00:00")


def test_correction_with_a_stale_row_version_is_rejected(manager, stub):
	employee = Client()
	shift = work_shift(employee, stub, "2026-11-17T07:00:00Z", "2026-11-17T18:00:00Z")
	path = f"/employees/{shift['userId']}/shifts/{shift['id']}"
	body = {"clockOutUtc": "2026-11-17T16:00:00Z", "reason": "first edit", "rowVersion": shift["rowVersion"]}
	assert manager.request("PATCH", path, json=body).status_code == 200

	second = manager.request("PATCH", path, json={**body, "clockOutUtc": "2026-11-17T15:00:00Z", "reason": "second edit"})

	assert second.status_code == 409
	assert second.json()["code"] == "stale_version"


@pytest.mark.parametrize("change, field", [
	({"clockOutUtc": "2026-11-17T06:00:00Z"}, "clockOutUtc"),      # before clock-in
	({"clockOutUtc": "2030-01-01T00:00:00Z"}, "clockOutUtc"),      # in the future
	({"clockInUtc": "2026-11-17T12:00:00Z"}, "breaks"),            # would cut off the break
	({"clockOutUtc": "2026-11-17T16:00:00Z", "reason": "x"}, "reason"),
	({"reason": "no times given"}, "clockInUtc"),
])
def test_invalid_corrections_are_rejected(manager, stub, change, field):
	employee = Client()
	stub.set("2026-11-17T07:00:00Z")
	employee.clock_in()
	stub.set("2026-11-17T10:00:00Z")
	employee.break_start()
	stub.set("2026-11-17T10:30:00Z")
	employee.break_end()
	stub.set("2026-11-17T18:00:00Z")
	shift = employee.clock_out().json()

	response = manager.request("PATCH", f"/employees/{shift['userId']}/shifts/{shift['id']}", json={
		"reason": "valid reason", "rowVersion": shift["rowVersion"], **change})

	assert response.status_code == 400
	assert field in response.json()["errors"]


def test_correction_may_not_overlap_another_shift(manager, stub):
	employee = Client()
	work_shift(employee, stub, "2026-11-17T07:00:00Z", "2026-11-17T12:00:00Z")
	later = work_shift(employee, stub, "2026-11-17T13:00:00Z", "2026-11-17T18:00:00Z")

	response = manager.request("PATCH", f"/employees/{later['userId']}/shifts/{later['id']}", json={
		"clockInUtc": "2026-11-17T11:00:00Z", "reason": "moving it earlier", "rowVersion": later["rowVersion"]})

	assert response.status_code == 409
	assert response.json()["code"] == "shift_overlap"


def test_correcting_a_missing_shift_is_not_found(manager, stub):
	response = manager.request("PATCH", "/employees/999999/shifts/999999", json={
		"clockOutUtc": "2026-11-17T16:00:00Z", "reason": "valid reason", "rowVersion": "AAAAAAAAB9E="})

	assert response.status_code == 404


def test_forgotten_clock_out_is_closed_automatically_and_audited(manager, stub):
	# The test stack allows 30 h per shift and runs the job every 3 s.
	employee = Client()
	stub.set("2026-11-17T07:00:00Z")
	shift = employee.clock_in().json()
	stub.set("2026-11-18T14:00:00Z")   # 31 hours later

	deadline = time.monotonic() + 30
	while employee.get("/shifts/current").status_code != 204 and time.monotonic() < deadline:
		time.sleep(1)
	closed = employee.get(f"/shifts/{shift['id']}").json()
	audit = manager.get(f"/employees/{shift['userId']}/shifts/{shift['id']}/audit").json()["items"]

	assert closed["status"] == "AutoClosed"
	assert closed["shiftMinutes"] == 30 * 60, "closed at clock-in + maximum, not at the time the job happened to run"
	assert audit[0]["kind"] == "AutoClose"
	assert audit[0]["changedBy"] is None


def test_health_endpoints_are_not_reachable_from_outside(stub):
	assert requests.get(BASE.replace("/api/v1", "") + "/health/live", timeout=TIMEOUT).status_code == 404
	assert requests.get(BASE.replace("/api/v1", "") + "/health/ready", timeout=TIMEOUT).status_code == 404
