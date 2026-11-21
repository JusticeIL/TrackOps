"""Sign-in, session cookies, rotation and revocation, and the password set at a username's first sign-in."""
import os
import time
import uuid
from concurrent.futures import ThreadPoolExecutor

import pytest
import requests

from conftest import BASE, PASSWORD, TIMEOUT, Client


def test_sign_in_sets_http_only_same_site_cookies(stub):
	client = Client(sign_in=False)
	response = client.sign_in()
	set_cookie = response.headers.get("Set-Cookie", "").lower()

	assert response.json()["role"] == "Employee"
	for name in ("trackops_jwt", "trackops_sid"):
		assert name in set_cookie
	# requests folds several Set-Cookie headers into one; read them one by one from the raw response.
	for header in response.raw.headers.getlist("Set-Cookie"):
		if header.startswith(("trackops_jwt", "trackops_sid")):
			assert "httponly" in header.lower()
			assert "samesite=strict" in header.lower()
			assert "path=/api" in header.lower()


@pytest.mark.skipif(os.environ.get("EXPECT_NO_MANAGER") != "1", reason="run against a stack started with TEST_MANAGER_USERNAME= (see README)")
def test_without_a_configured_manager_everyone_is_an_employee(stub):
	admin = Client("admin")

	assert admin.user["role"] == "Employee"
	assert admin.get("/auth/session").json()["role"] == "Employee"
	assert admin.query("/employees/shifts").status_code == 403


@pytest.mark.skipif(os.environ.get("EXPECT_NO_MANAGER") == "1", reason="this stack has no manager")
def test_admin_username_is_manager_and_others_are_employees(stub):
	assert Client("admin").user["role"] == "Manager"
	assert Client().user["role"] == "Employee"


@pytest.mark.parametrize("username, password", [
	("ab", PASSWORD),                      # too short
	("has space", PASSWORD),               # illegal character
	("x" * 33, PASSWORD),                  # too long
	("<script>alert(1)</script>", PASSWORD),
	("valid.name", "short"),               # password too short
	("", ""),
])
def test_sign_in_validates_input(stub, username, password):
	client = Client(sign_in=False)
	client.refresh_csrf()
	response = client.http.post(f"{BASE}/auth/session", json={"username": username, "password": password}, timeout=TIMEOUT)

	assert response.status_code == 400
	assert response.headers["Content-Type"].startswith("application/problem+json")
	assert response.json()["code"] == "validation_failed"
	assert "trackops_jwt" not in response.headers.get("Set-Cookie", "")


def test_session_endpoint_requires_sign_in(stub):
	response = requests.get(f"{BASE}/auth/session", timeout=TIMEOUT)

	assert response.status_code == 401
	assert response.json()["code"] == "unauthenticated"


def test_sign_out_revokes_the_token_even_if_the_cookie_is_kept(user):
	stolen_cookies = dict(user.http.cookies)
	assert user.get("/auth/session").status_code == 200

	assert user.request("DELETE", "/auth/session").status_code == 204

	# The JWT is still inside its 24 h lifetime, but its session no longer exists in Redis.
	replay = requests.get(f"{BASE}/auth/session", cookies=stolen_cookies, timeout=TIMEOUT)
	assert replay.status_code == 401


def test_signing_in_again_rotates_the_session(user):
	old_cookies = dict(user.http.cookies)

	user.sign_in()

	assert user.http.cookies["trackops_sid"] != old_cookies["trackops_sid"]
	assert requests.get(f"{BASE}/auth/session", cookies=old_cookies, timeout=TIMEOUT).status_code == 401
	assert user.get("/auth/session").status_code == 200


def test_jwt_is_rejected_without_its_matching_session_cookie(user):
	only_jwt = {"trackops_jwt": user.http.cookies["trackops_jwt"]}

	assert requests.get(f"{BASE}/auth/session", cookies=only_jwt, timeout=TIMEOUT).status_code == 401


def test_tampered_jwt_is_rejected(user):
	cookies = dict(user.http.cookies)
	cookies["trackops_jwt"] = cookies["trackops_jwt"][:-4] + "AAAA"

	assert requests.get(f"{BASE}/auth/session", cookies=cookies, timeout=TIMEOUT).status_code == 401


# ---------- The first sign-in sets the password ----------

def new_username():
	return "p" + uuid.uuid4().hex[:14]


def sign_in_as(username, password):
	"""One sign-in attempt in a fresh browser session; returns the client and the response."""
	client = Client(username, sign_in=False)
	client.refresh_csrf()
	response = client.http.post(f"{BASE}/auth/session", json={"username": username, "password": password}, timeout=TIMEOUT)
	return client, response


def test_the_password_of_the_first_sign_in_is_required_afterwards(stub):
	username = new_username()

	_, first = sign_in_as(username, "first-password")
	_, again = sign_in_as(username, "first-password")
	_, wrong = sign_in_as(username, "another-password")
	_, other_case = sign_in_as(username.upper(), "first-password")   # usernames ignore case

	assert first.status_code == 200
	assert again.status_code == 200
	assert again.json()["id"] == first.json()["id"]
	assert wrong.status_code == 401
	assert wrong.json()["code"] == "invalid_credentials"
	assert "trackops_sid" not in wrong.headers.get("Set-Cookie", ""), "a refused sign-in opens no session"
	assert other_case.status_code == 200


def test_a_users_data_stays_with_the_username(stub):
	first_visit = Client()
	first_visit.request("POST", "/requests", json={"type": "Other", "title": "Kept across sign-ins", "content": "Still here."})

	later_visit = Client(first_visit.username)   # another browser, the same username and password

	assert [item["title"] for item in later_visit.get("/requests").json()["items"]] == ["Kept across sign-ins"]


def test_simultaneous_first_sign_ins_let_exactly_one_password_in(stub):
	# Ten browsers claim one new username at once, each with its own password. The database keeps
	# one user; the other nine are checked against the winner's password and refused.
	username = new_username()
	passwords = [f"password-{index:02d}" for index in range(10)]

	with ThreadPoolExecutor(max_workers=len(passwords)) as pool:
		codes = list(pool.map(lambda password: sign_in_as(username, password)[1].status_code, passwords))

	# The others are refused: 401 for a wrong password, or 429 once five wrong ones locked the name.
	assert codes.count(200) == 1, codes
	assert all(code in (401, 429) for code in codes if code != 200), codes


def test_the_password_is_never_sent_back(stub):
	client, response = sign_in_as(new_username(), "secret-password-123")
	session = client.get("/auth/session")

	assert response.status_code == 200
	for body in (response.text, session.text):
		assert "secret-password-123" not in body
		assert "pbkdf2" not in body.lower()
		assert "passwordhash" not in body.lower()


# ---------- Too many wrong passwords lock the username for a while ----------
# Production: 5 wrong passwords within 15 minutes. The test stack keeps the count of 5 and
# shortens the window to 6 seconds.

MAX_FAILED_SIGN_INS = 5
TEST_WINDOW_SECONDS = 6


def fail(username, times):
	return [sign_in_as(username, f"wrong-password-{index}")[1].status_code for index in range(times)]


def test_a_username_is_locked_after_too_many_wrong_passwords(stub):
	username = new_username()
	sign_in_as(username, "right-password")

	refused = fail(username, MAX_FAILED_SIGN_INS)
	_, locked = sign_in_as(username, "right-password")   # even the right password waits

	assert refused == [401] * MAX_FAILED_SIGN_INS
	assert locked.status_code == 429
	assert locked.json()["code"] == "sign_in_locked"
	assert 1 <= int(locked.headers["Retry-After"]) <= TEST_WINDOW_SECONDS


def test_the_lock_affects_only_that_username_and_ends_by_itself(stub):
	username, other = new_username(), new_username()
	sign_in_as(username, "right-password")
	sign_in_as(other, "other-password")
	fail(username, MAX_FAILED_SIGN_INS)

	assert sign_in_as(other, "other-password")[1].status_code == 200
	assert sign_in_as(username, "right-password")[1].status_code == 429
	time.sleep(TEST_WINDOW_SECONDS + 1)
	assert sign_in_as(username, "right-password")[1].status_code == 200


def test_a_correct_password_clears_earlier_mistakes(stub):
	username = new_username()
	sign_in_as(username, "right-password")

	fail(username, MAX_FAILED_SIGN_INS - 1)
	assert sign_in_as(username, "right-password")[1].status_code == 200
	fail(username, MAX_FAILED_SIGN_INS - 1)

	# Eight wrong passwords in all, but never five in a row, so the username is not locked.
	assert sign_in_as(username, "right-password")[1].status_code == 200

