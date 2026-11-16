"""Sign-in, session cookies, rotation and revocation."""
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
