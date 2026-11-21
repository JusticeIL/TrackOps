"""The external time source: fail closed, never fall back to a local clock."""
import time


def test_clock_in_fails_closed_when_every_provider_is_down(user, stub):
	stub.mode("down", "down")

	response = user.clock_in()

	assert response.status_code == 503
	assert response.json()["code"] == "time_source_unavailable"
	assert "Retry-After" in response.headers
	stub.mode("ok", "ok")
	time.sleep(1.2)
	assert user.get("/shifts/current").status_code == 204, "nothing may be written when the time is unknown"


def test_clock_out_fails_closed_and_leaves_the_shift_open(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	user.clock_in()
	stub.mode("down", "down")

	response = user.clock_out()

	assert response.status_code == 503
	stub.mode("ok", "ok")
	time.sleep(1.2)
	assert user.get("/shifts/current").json()["clockOutUtc"] is None


def test_slow_providers_time_out_and_fail_closed(user, stub):
	stub.mode("slow", "slow", slow_seconds=6)
	started = time.monotonic()

	response = user.clock_in()

	elapsed = time.monotonic() - started
	assert response.status_code == 503
	assert elapsed < 12, "timeouts and the retry are bounded"


def test_invalid_answers_are_not_trusted(user, stub):
	stub.mode("garbage", "garbage")

	assert user.clock_in().status_code == 503


def test_an_answer_without_a_time_fails_closed_with_503_not_500(user, stub):
	stub.mode("empty", "empty")

	response = user.clock_in()

	assert response.status_code == 503
	assert response.json()["code"] == "time_source_unavailable"
	assert "Retry-After" in response.headers


def test_an_answer_without_a_time_falls_back_to_the_second_provider(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	stub.mode("empty", "ok")

	response = user.clock_in()

	assert response.status_code == 201
	assert response.json()["clockInUtc"].startswith("2026-11-17T07:00:0")


def test_secondary_provider_is_used_when_the_primary_is_down(user, stub):
	stub.set("2026-11-17T07:00:00Z")
	stub.mode("down", "ok")

	response = user.clock_in()

	assert response.status_code == 201
	assert response.json()["clockInUtc"].startswith("2026-11-17T07:00:0")


def test_recording_works_again_after_the_provider_recovers(user, stub):
	stub.mode("down", "down")
	assert user.clock_in().status_code == 503

	stub.mode("ok", "ok")
	time.sleep(1.5)
	stub.set("2026-11-17T07:00:00Z")

	assert user.clock_in().status_code == 201


def test_display_time_is_zurich_time_from_the_external_source(user, stub):
	stub.set("2026-07-01T10:00:00Z")

	summer = user.get("/time").json()
	stub.set("2026-12-01T10:00:00Z")
	winter = user.get("/time").json()

	assert summer["utc"].startswith("2026-07-01T10:00:0")
	assert summer["zone"] == "Europe/Zurich"
	assert summer["offsetSeconds"] == 7200
	assert winter["offsetSeconds"] == 3600
