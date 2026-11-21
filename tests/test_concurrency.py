"""Concurrency across the two replicas: the database decides, not the application's memory."""
import uuid
from concurrent.futures import ThreadPoolExecutor

import pytest

PARALLEL = 20


def fire(clients, send):
	with ThreadPoolExecutor(max_workers=len(clients)) as pool:
		return list(pool.map(send, clients))


def test_twenty_concurrent_clock_ins_open_exactly_one_shift(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	clients = [user.clone() for _ in range(PARALLEL)]

	responses = fire(clients, lambda client: client.clock_in())

	statuses = sorted(response.status_code for response in responses)
	replicas = {response.headers.get("X-Replica") for response in responses}
	assert statuses == [201] + [409] * (PARALLEL - 1)
	assert replicas == {"1", "2"}, "the requests must have been spread over both replicas"
	assert all(response.json()["code"] == "shift_already_open" for response in responses if response.status_code == 409)
	history = user.query("/shifts").json()
	assert history["totalItems"] == 1
	assert history["items"][0]["status"] == "Open"


def test_concurrent_retries_with_one_key_all_get_the_same_shift(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	key = str(uuid.uuid4())
	clients = [user.clone() for _ in range(10)]

	responses = fire(clients, lambda client: client.clock_in(key))

	assert all(response.status_code == 201 for response in responses), [response.status_code for response in responses]
	assert len({response.json()["id"] for response in responses}) == 1
	assert user.query("/shifts").json()["totalItems"] == 1


def test_concurrent_clock_out_retries_with_one_key_all_get_the_same_answer(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T15:00:00Z")
	key = str(uuid.uuid4())
	clients = [user.clone() for _ in range(10)]

	responses = fire(clients, lambda client: client.clock_out(key))

	# Every retry gets the stored 200, never "no open shift" because another retry got there first.
	assert all(response.status_code == 200 for response in responses), [response.status_code for response in responses]
	assert len({response.json()["clockOutUtc"] for response in responses}) == 1
	assert user.get("/shifts/current").status_code == 204


@pytest.mark.parametrize("attempt", range(5))
def test_same_key_race_is_stable_over_repeated_runs(stub, attempt):
	from conftest import Client
	user = Client()
	stub.set("2026-11-17T07:00:00Z")
	key = str(uuid.uuid4())
	clients = [user.clone() for _ in range(12)]

	responses = fire(clients, lambda client: client.clock_in(key))

	assert [response.status_code for response in responses] == [201] * 12
	assert len({response.json()["id"] for response in responses}) == 1


def test_concurrent_break_starts_open_exactly_one_break(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T09:00:00Z")
	clients = [user.clone() for _ in range(10)]

	responses = fire(clients, lambda client: client.break_start())

	assert sorted(response.status_code for response in responses) == [201] + [409] * 9
	assert user.get("/shifts/current").json()["break"]["endUtc"] is None


def test_concurrent_clock_outs_close_the_shift_once(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.set("2026-11-17T15:00:00Z")
	clients = [user.clone() for _ in range(10)]

	responses = fire(clients, lambda client: client.clock_out())

	assert sorted(response.status_code for response in responses) == [200] + [409] * 9
	assert user.get("/shifts/current").status_code == 204
