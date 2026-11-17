"""Requests and reports: a filed request is stored with the external time, and only its owner sees it."""
import pytest
import requests

from conftest import BASE, TIMEOUT, Client

VACATION = {"type": "Vacation", "title": "Annual leave, 5 days", "content": "Family trip in August."}
SICKNESS = {"type": "Sickness", "title": "Sick leave", "sickDays": 2}


def test_filing_a_request_stores_it_with_the_external_time(user, stub):
	stub.set("2026-11-17T09:15:00Z")
	created = user.request("POST", "/requests", json=VACATION)
	listed = user.get("/requests")

	assert created.status_code == 201
	assert created.json()["id"] >= 1001, "request numbers start at 1001"
	assert created.json()["type"] == "Vacation"
	assert created.json()["status"] == "InProgress"
	assert created.json()["title"] == VACATION["title"]
	assert created.json()["createdAtUtc"].startswith("2026-11-17T09:15:0")
	assert listed.status_code == 200
	assert listed.json()["items"] == [created.json()]


def test_client_cannot_supply_the_filing_time_or_the_status(user, stub):
	stub.set("2026-11-17T09:15:00Z")

	created = user.request("POST", "/requests", json={**VACATION, "createdAtUtc": "2020-01-01T00:00:00Z", "status": "Approved"})

	assert created.status_code == 201
	assert created.json()["createdAtUtc"].startswith("2026-11-17T09:15:0")
	assert created.json()["status"] == "InProgress"


def test_sickness_report_keeps_its_days_and_needs_no_explanation(user, stub):
	created = user.request("POST", "/requests", json=SICKNESS)

	assert created.status_code == 201
	assert created.json()["type"] == "Sickness"
	assert created.json()["sickDays"] == 2
	assert created.json()["content"] == ""


def test_requests_are_listed_newest_first(user, stub):
	first = user.request("POST", "/requests", json=VACATION).json()
	second = user.request("POST", "/requests", json=SICKNESS).json()

	assert [item["id"] for item in user.get("/requests").json()["items"]] == [second["id"], first["id"]]


def test_a_user_sees_only_their_own_requests(user, stub):
	other = Client()
	user.request("POST", "/requests", json=VACATION)

	assert len(user.get("/requests").json()["items"]) == 1
	assert other.get("/requests").json()["items"] == []


@pytest.mark.parametrize("body, field", [
	({}, "type"),
	({**VACATION, "type": "Holiday"}, "$.type"),  # an unknown name is refused when the JSON is read
	({**VACATION, "type": 99}, "type"),
	({**VACATION, "title": "ab"}, "title"),
	({**VACATION, "title": "x" * 101}, "title"),
	({**VACATION, "content": ""}, "content"),
	({**VACATION, "content": "x" * 2001}, "content"),
	({**VACATION, "sickDays": 2}, "sickDays"),
	({"type": "Sickness", "title": "Sick leave"}, "sickDays"),
	({**SICKNESS, "sickDays": 0}, "sickDays"),
	({**SICKNESS, "sickDays": 31}, "sickDays"),
])
def test_invalid_requests_are_rejected(user, stub, body, field):
	response = user.request("POST", "/requests", json=body)

	assert response.status_code == 400, response.text
	assert response.json()["code"] == "validation_failed"
	assert field in response.json()["errors"]
	assert user.get("/requests").json()["items"] == []


def test_filing_fails_closed_when_the_time_source_is_down(user, stub):
	stub.mode("down", "down")

	response = user.request("POST", "/requests", json=VACATION)

	assert response.status_code == 503
	assert response.json()["code"] == "time_source_unavailable"
	assert user.get("/requests").json()["items"] == [], "nothing is stored without an external time"


def test_requests_need_a_session(stub):
	response = requests.get(f"{BASE}/requests", timeout=TIMEOUT)

	assert response.status_code == 401


def test_filing_needs_the_csrf_token(user, stub):
	del user.http.headers["X-CSRF-Token"]

	response = user.request("POST", "/requests", json=VACATION)

	assert response.status_code == 403
	assert response.json()["code"] == "csrf_invalid"


@pytest.mark.parametrize("payload", [
	'<img src=x onerror=alert(1)><script>alert("xss")</script>',
	"'; DROP TABLE EmployeeRequests; --",
])
def test_script_and_sql_in_a_request_are_stored_as_plain_text(user, stub, payload):
	created = user.request("POST", "/requests", json={"type": "Other", "title": payload, "content": payload})
	listed = user.get("/requests")

	# The API answers JSON with nosniff and React renders text, so the value is kept exactly as typed.
	assert created.status_code == 201
	assert listed.headers["Content-Type"].startswith("application/json")
	assert listed.headers["X-Content-Type-Options"] == "nosniff"
	assert listed.json()["items"][0]["title"] == payload
	assert listed.json()["items"][0]["content"] == payload
