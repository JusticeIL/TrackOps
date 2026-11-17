"""History search with the HTTP QUERY method: filters and pagination."""
import pytest
import requests

from conftest import BASE, TIMEOUT, work_shift


@pytest.fixture
def history(user, stub):
	"""Twelve closed shifts on twelve consecutive days."""
	for index in range(12):
		work_shift(user, stub, f"2026-11-{index + 1:02d}T07:00:00Z", f"2026-11-{index + 1:02d}T15:00:00Z")
	return user


def test_query_method_returns_a_page(history):
	response = history.query("/shifts", {"page": 1, "pageSize": 5})
	body = response.json()

	assert response.status_code == 200
	assert body["totalItems"] == 12
	assert body["totalPages"] == 3
	assert body["page"] == 1
	assert len(body["items"]) == 5


def test_query_works_without_a_body(history):
	body = history.query("/shifts").json()

	assert body["pageSize"] == 20
	assert body["totalItems"] == 12


def test_pages_are_newest_first_and_do_not_overlap(history):
	pages = [history.query("/shifts", {"page": page, "pageSize": 5}).json()["items"] for page in (1, 2, 3)]
	ids = [item["id"] for page in pages for item in page]
	clock_ins = [item["clockInUtc"] for page in pages for item in page]

	assert [len(page) for page in pages] == [5, 5, 2]
	assert len(set(ids)) == 12
	assert clock_ins == sorted(clock_ins, reverse=True)


def test_page_past_the_end_is_empty(history):
	body = history.query("/shifts", {"page": 9, "pageSize": 5}).json()

	assert body["items"] == []
	assert body["totalItems"] == 12


def test_date_range_filter_uses_zurich_local_dates(history):
	body = history.query("/shifts", {"from": "2026-11-03", "to": "2026-11-05"}).json()

	assert body["totalItems"] == 3
	assert {item["clockInUtc"][:10] for item in body["items"]} == {"2026-11-03", "2026-11-04", "2026-11-05"}


def test_own_history_has_no_text_search(history):
	# A shift holds no free text, so "search" applies to the manager's list only (it matches
	# employee names). On a user's own history the field is ignored, like any unknown field.
	body = history.query("/shifts", {"search": "no shift contains this"}).json()

	assert body["totalItems"] == 12


def test_status_filter(history, stub):
	stub.set("2026-11-20T07:00:00Z")
	history.clock_in()

	assert history.query("/shifts", {"status": ["Open"]}).json()["totalItems"] == 1
	assert history.query("/shifts", {"status": ["Closed"]}).json()["totalItems"] == 12
	assert history.query("/shifts", {"status": ["Open", "Closed"]}).json()["totalItems"] == 13


def test_history_shows_only_the_users_own_shifts(history, stub):
	from conftest import Client
	stranger = Client()

	assert stranger.query("/shifts").json()["totalItems"] == 0
	assert stranger.query("/shifts", {"userId": history.user["id"]}).json()["totalItems"] == 0


@pytest.mark.parametrize("body", [
	{"page": 0},
	{"pageSize": 0},
	{"pageSize": 101},
	{"from": "2026-11-10", "to": "2026-11-01"},
	{"from": "not-a-date"},
	{"status": ["Sleeping"]},
	{"search": "x" * 101},
])
def test_invalid_queries_are_rejected(user, stub, body):
	response = user.query("/shifts", body)

	assert response.status_code == 400
	assert response.json()["code"] == "validation_failed"


def test_query_requires_sign_in(stub):
	assert requests.request("QUERY", f"{BASE}/shifts", json={}, timeout=TIMEOUT).status_code == 401


def test_query_needs_no_csrf_token_because_it_is_a_safe_method(history):
	del history.http.headers["X-CSRF-Token"]

	assert history.query("/shifts").status_code == 200


def test_there_is_no_post_fallback_for_search(user, stub):
	# 404 or 405: either way there is no POST route that searches.
	assert user.request("POST", "/shifts/search", json={}).status_code in (404, 405)
	assert user.request("POST", "/employees/search", json={}).status_code in (403, 404, 405)
