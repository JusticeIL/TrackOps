"""Security behaviour seen from outside: CSRF, rate limits, headers, CORS, XSS and SQL injection."""
import os

import pytest
import requests

from conftest import BASE, GATEWAY, PASSWORD, PUBLIC_ORIGIN, TIMEOUT, Client, work_shift

FOREIGN_ORIGIN = "https://evil.example"


# ---------- CSRF ----------

def test_state_changing_request_without_csrf_token_is_rejected(user, stub):
	del user.http.headers["X-CSRF-Token"]

	response = user.clock_in()

	assert response.status_code == 403
	assert response.json()["code"] == "csrf_invalid"
	assert user.get("/shifts/current").status_code == 204


def test_made_up_csrf_token_is_rejected(user, stub):
	user.http.headers["X-CSRF-Token"] = "0123456789ABCDEF0123456789ABCDEF.DEADBEEF"

	assert user.clock_in().status_code == 403


def test_csrf_token_of_another_session_is_rejected(user, stub):
	other = Client()
	# The attacker plants their own valid token pair; it is signed for their session, not the victim's.
	user.http.headers["X-CSRF-Token"] = other.http.headers["X-CSRF-Token"]
	user.http.cookies.set("trackops_csrf", other.http.cookies["trackops_csrf"], path="/api")

	assert user.clock_in().status_code == 403


def test_csrf_token_from_before_sign_in_stops_working_after_it(stub):
	client = Client(sign_in=False)
	anonymous_token = client.refresh_csrf()
	response = client.http.post(f"{BASE}/auth/session", json={"username": client.username, "password": PASSWORD}, timeout=TIMEOUT)
	assert response.status_code == 200
	client.http.headers["X-CSRF-Token"] = anonymous_token

	assert client.clock_in().status_code == 403


def test_sign_in_itself_needs_a_csrf_token(stub):
	response = requests.post(f"{BASE}/auth/session", json={"username": "someone", "password": PASSWORD}, timeout=TIMEOUT)

	assert response.status_code == 403


# ---------- Rate limiting ----------

def test_recording_is_rate_limited_per_user(user, stub):
	# Limit: 30 recordings per minute per user. Rejected attempts count too.
	statuses = [user.clock_out().status_code for _ in range(32)]
	limited = user.clock_out()

	assert statuses[:30] == [409] * 30
	assert 429 in statuses[30:]
	assert limited.status_code == 429
	assert limited.json()["code"] == "rate_limited"
	assert int(limited.headers["Retry-After"]) >= 1


def test_rate_limit_of_one_user_does_not_affect_another(user, stub):
	for _ in range(32):
		user.clock_out()
	other = Client()
	stub.set("2026-11-17T07:00:00Z")

	assert other.clock_in().status_code == 201


# ---------- Headers ----------

def test_page_has_security_headers(stub):
	headers = requests.get(GATEWAY + "/", timeout=TIMEOUT).headers
	policy = headers["Content-Security-Policy"]

	assert "default-src 'self'" in policy
	assert "script-src 'self'" in policy and "unsafe-inline" not in policy and "unsafe-eval" not in policy
	assert "frame-ancestors 'none'" in policy
	assert "object-src 'none'" in policy
	assert headers["X-Content-Type-Options"] == "nosniff"
	assert headers["X-Frame-Options"] == "DENY"
	assert headers["Referrer-Policy"] == "no-referrer"
	assert "Permissions-Policy" in headers


def test_api_has_security_headers_and_is_never_cached(user):
	headers = user.get("/auth/session").headers

	assert headers["X-Content-Type-Options"] == "nosniff"
	assert headers["X-Frame-Options"] == "DENY"
	assert headers["Cache-Control"] == "no-store"
	assert "frame-ancestors 'none'" in headers["Content-Security-Policy"]


def test_responses_do_not_reveal_server_software(user):
	for response in (requests.get(GATEWAY + "/", timeout=TIMEOUT), user.get("/auth/session")):
		assert "kestrel" not in response.headers.get("Server", "").lower()
		assert "/" not in response.headers.get("Server", ""), "no version number in the Server header"
		assert "X-Powered-By" not in response.headers


def test_errors_do_not_leak_internals(user):
	response = user.request("PATCH", "/employees/1/shifts/1", data="{not json", headers={"Content-Type": "application/json"})
	text = response.text.lower()

	assert response.headers["Content-Type"].startswith("application/problem+json")
	for marker in ("exception", "stack", "   at ", "microsoft.", "sqlclient"):
		assert marker not in text


def test_static_assets_are_cacheable_and_the_shell_is_revalidated(stub):
	html = requests.get(GATEWAY + "/", timeout=TIMEOUT)
	asset = html.text.split('src="')[1].split('"')[0]
	headers = requests.get(GATEWAY + asset, headers={"Accept-Encoding": "gzip"}, timeout=TIMEOUT).headers

	assert html.headers["Cache-Control"] == "no-cache"
	assert "immutable" in headers["Cache-Control"]
	assert headers.get("Content-Encoding") == "gzip"


@pytest.mark.skipif(os.environ.get("EXPECT_HTTPS") != "1", reason="run against a stack started with REQUIRE_HTTPS=true (see README)")
def test_https_mode_sets_secure_cookies_and_hsts(stub):
	client = Client(sign_in=False)
	token = client.http.get(f"{BASE}/auth/csrf", timeout=TIMEOUT)
	csrf_cookie = token.raw.headers.getlist("Set-Cookie")[0]
	response = requests.post(
		f"{BASE}/auth/session",
		json={"username": client.username, "password": PASSWORD},
		headers={"X-CSRF-Token": token.json()["token"], "Cookie": csrf_cookie.split(";")[0]},
		timeout=TIMEOUT,
	)

	assert response.status_code == 200
	for header in response.raw.headers.getlist("Set-Cookie"):
		if header.startswith(("trackops_jwt", "trackops_sid")):
			assert "secure" in header.lower()
	assert "max-age=" in response.headers["Strict-Transport-Security"]
	assert "max-age=" in requests.get(GATEWAY + "/", timeout=TIMEOUT).headers["Strict-Transport-Security"]


# ---------- CORS ----------

def test_foreign_origin_gets_no_cors_permission(user):
	response = user.request("GET", "/auth/session", headers={"Origin": FOREIGN_ORIGIN})

	assert "Access-Control-Allow-Origin" not in response.headers
	assert "Access-Control-Allow-Credentials" not in response.headers


def test_foreign_origin_preflight_is_not_approved(stub):
	response = requests.options(
		f"{BASE}/shifts",
		headers={"Origin": FOREIGN_ORIGIN, "Access-Control-Request-Method": "QUERY", "Access-Control-Request-Headers": "content-type"},
		timeout=TIMEOUT,
	)

	assert "Access-Control-Allow-Origin" not in response.headers


def test_own_origin_preflight_allows_the_query_method(stub):
	response = requests.options(
		f"{BASE}/shifts",
		headers={"Origin": PUBLIC_ORIGIN, "Access-Control-Request-Method": "QUERY", "Access-Control-Request-Headers": "content-type"},
		timeout=TIMEOUT,
	)

	assert response.headers.get("Access-Control-Allow-Origin") == PUBLIC_ORIGIN
	assert "QUERY" in response.headers.get("Access-Control-Allow-Methods", "")


# ---------- XSS ----------

XSS_PAYLOAD = '<img src=x onerror=alert(1)><script>alert("xss")</script>'


def correct_with_reason(manager, stub, reason):
	"""The correction reason is the only free text the system stores. Returns the audit trail response."""
	employee = Client()
	shift = work_shift(employee, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")
	path = f"/employees/{employee.user['id']}/shifts/{shift['id']}"
	corrected = manager.request("PATCH", path, json={
		"clockOutUtc": "2026-11-17T14:00:00Z", "reason": reason, "rowVersion": shift["rowVersion"]})

	assert corrected.status_code == 200, corrected.text
	return manager.get(path + "/audit")


def test_script_in_a_correction_reason_is_stored_and_returned_as_plain_data(manager, stub):
	audit = correct_with_reason(manager, stub, XSS_PAYLOAD)

	# The API returns JSON with nosniff, so a browser never treats it as HTML; React then
	# renders the reason as text. The value itself is kept exactly as typed.
	assert audit.headers["Content-Type"].startswith("application/json")
	assert audit.headers["X-Content-Type-Options"] == "nosniff"
	assert audit.json()["items"][0]["reason"] == XSS_PAYLOAD


def test_script_in_a_search_term_is_not_reflected_as_html(manager):
	response = manager.query("/employees/shifts", {"search": XSS_PAYLOAD})

	assert response.status_code == 200
	assert response.headers["Content-Type"].startswith("application/json")
	assert response.json()["totalItems"] == 0


# ---------- SQL injection ----------

SQL_PAYLOADS = [
	"' OR '1'='1",
	"'; DROP TABLE Shifts; --",
	"%' UNION SELECT Id, Username, DisplayName, Role FROM Users --",
	"\\' OR 1=1 --",
]


@pytest.mark.parametrize("payload", SQL_PAYLOADS)
def test_sql_in_search_is_treated_as_text(manager, user, stub, payload):
	work_shift(user, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")

	employees = manager.query("/employees", {"search": payload})
	shifts = manager.query("/employees/shifts", {"search": payload})
	after = manager.query("/employees/shifts", {"userId": user.user["id"]})

	# The search matches employee names, and a name may not contain these characters.
	assert employees.status_code == 200 and employees.json()["items"] == []
	assert shifts.status_code == 200
	assert shifts.json()["totalItems"] == 0, "a tautology must not match every row"
	assert after.json()["totalItems"] == 1, "the table still exists and still has the row"


def test_like_wildcards_in_search_match_literally(manager, user, stub):
	work_shift(user, stub, "2026-11-17T07:00:00Z", "2026-11-17T15:00:00Z")

	assert manager.query("/employees/shifts", {"search": user.username}).json()["totalItems"] == 1
	assert manager.query("/employees/shifts", {"search": "%"}).json()["totalItems"] == 0
	assert manager.query("/employees/shifts", {"search": "_" * len(user.username)}).json()["totalItems"] == 0


def test_sql_in_a_correction_reason_is_stored_as_text(manager, stub):
	audit = correct_with_reason(manager, stub, SQL_PAYLOADS[1])

	assert audit.json()["items"][0]["reason"] == SQL_PAYLOADS[1]
	assert manager.query("/employees/shifts", {"pageSize": 1}).json()["totalItems"] > 0


def test_sql_in_a_username_is_rejected_by_validation(stub):
	client = Client(sign_in=False)
	client.refresh_csrf()

	response = client.http.post(f"{BASE}/auth/session", json={"username": "admin'--", "password": PASSWORD}, timeout=TIMEOUT)

	assert response.status_code == 400


def test_oversized_request_body_is_refused(user):
	response = user.request("QUERY", "/shifts", data="x" * 200_000, headers={"Content-Type": "application/json"})

	assert response.status_code in (400, 413)
