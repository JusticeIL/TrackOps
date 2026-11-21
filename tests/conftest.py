"""Shared fixtures. The tests are black-box: they talk only to the gateway and to the time stub.

Start the stack with the test overlay first:

    docker compose -f compose.yaml -f infra/compose.test.yaml up -d --build
"""
import os
import time
import uuid

import pytest
import requests

# 127.0.0.1 rather than "localhost": on Windows "localhost" is tried over IPv6 first, and every
# request then waits about two seconds before falling back to IPv4.
GATEWAY = os.environ.get("TRACKOPS_URL", "http://127.0.0.1:8080")
BASE = GATEWAY + "/api/v1"
STUB = os.environ.get("TRACKOPS_STUB_URL", "http://127.0.0.1:8090") + "/control"
# The origin the browser uses, as configured in PUBLIC_ORIGIN; only CORS tests need it.
PUBLIC_ORIGIN = os.environ.get("TRACKOPS_PUBLIC_ORIGIN", "http://localhost:8080")
PASSWORD = "not-a-real-password"
TIMEOUT = 30


class Stub:
	"""Remote control for the stub time API."""

	def set(self, utc):
		requests.post(STUB, json={"utc": utc}, timeout=TIMEOUT).raise_for_status()

	def mode(self, primary="ok", secondary="ok", slow_seconds=5.0):
		requests.post(STUB, json={"primary": primary, "secondary": secondary, "slowSeconds": slow_seconds}, timeout=TIMEOUT).raise_for_status()


class Client:
	"""One signed-in user: a cookie jar plus the CSRF token for it."""

	def __init__(self, username=None, sign_in=True):
		self.username = username or "t" + uuid.uuid4().hex[:14]
		self.http = requests.Session()
		self.user = None
		if sign_in:
			self.sign_in()

	def refresh_csrf(self):
		token = self.http.get(f"{BASE}/auth/csrf", timeout=TIMEOUT).json()["token"]
		self.http.headers["X-CSRF-Token"] = token
		return token

	def sign_in(self):
		self.refresh_csrf()
		response = self.http.post(f"{BASE}/auth/session", json={"username": self.username, "password": PASSWORD}, timeout=TIMEOUT)
		assert response.status_code == 200, response.text
		self.user = response.json()
		self.refresh_csrf()
		return response

	def request(self, method, path, key=None, **kwargs):
		headers = kwargs.pop("headers", {})
		if key is not None:
			headers["Idempotency-Key"] = key
		return self.http.request(method, BASE + path, headers=headers, timeout=TIMEOUT, **kwargs)

	def record(self, path, key=None, **kwargs):
		return self.request("POST", path, key=key or str(uuid.uuid4()), **kwargs)

	def clock_in(self, key=None):
		return self.record("/shifts", key)

	def clock_out(self, key=None):
		return self.record("/shifts/current/clock-out", key)

	def break_start(self, key=None):
		return self.record("/shifts/current/breaks/current/start", key)

	def break_end(self, key=None):
		return self.record("/shifts/current/breaks/current/end", key)

	def get(self, path):
		return self.request("GET", path)

	def query(self, path, body=None):
		return self.request("QUERY", path, json=body)

	def clone(self):
		"""A second HTTP session with the same cookies, for sending requests in parallel."""
		twin = Client(self.username, sign_in=False)
		twin.http.cookies.update(self.http.cookies)
		twin.http.headers["X-CSRF-Token"] = self.http.headers["X-CSRF-Token"]
		twin.user = self.user
		return twin


# Tests that read only the source code; a run of nothing but these needs no stack.
STATIC_TEST_FILES = {"test_static_clock.py"}


@pytest.fixture(scope="session", autouse=True)
def stack_is_up(request):
	if all(item.path.name in STATIC_TEST_FILES for item in request.session.items):
		return
	try:
		requests.get(STUB, timeout=5).raise_for_status()
		assert requests.get(GATEWAY, timeout=5).status_code == 200
	except Exception as error:  # noqa: BLE001
		pytest.exit(f"The test stack is not running ({error}). See the top of tests/conftest.py.", returncode=2)


@pytest.fixture
def stub():
	control = Stub()
	control.mode("ok", "ok")
	yield control
	control.mode("ok", "ok")
	# Let a circuit breaker that opened during the test close again (the test overlay sets 1 s).
	time.sleep(1.2)


@pytest.fixture
def user(stub):
	return Client()


@pytest.fixture
def manager(stub):
	return Client("admin")


def work_shift(client, stub, start_utc, end_utc):
	"""Clocks a user in at one instant and out at another, and returns the closed shift."""
	stub.set(start_utc)
	assert client.clock_in().status_code == 201
	stub.set(end_utc)
	response = client.clock_out()
	assert response.status_code == 200, response.text
	return response.json()
