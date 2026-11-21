"""Stub of the two external time APIs, for tests only.

It answers in the same JSON shapes as timeapi.io (/primary) and time.now (/secondary), so the
backend's real client code is exercised. Tests control it through /control:

    POST /control {"utc": "2027-03-28T00:30:00Z"}          set the clock (it keeps ticking)
    POST /control {"primary": "down", "secondary": "slow"} set each provider's behaviour
    GET  /control                                           read the current state

Behaviours: "ok", "down" (HTTP 500), "slow" (answers after `slowSeconds`), "garbage" (invalid JSON),
"empty" (valid JSON with the time set to null).
The stub's own clock is monotonic: a set time plus elapsed time since it was set.
"""
import json
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from zoneinfo import ZoneInfo

ZURICH = ZoneInfo("Europe/Zurich")
state = {
	"base_utc": datetime.now(timezone.utc),
	"base_monotonic": time.monotonic(),
	"primary": "ok",
	"secondary": "ok",
	"slowSeconds": 5.0,
}


def now_utc():
	return state["base_utc"] + timedelta(seconds=time.monotonic() - state["base_monotonic"])


def primary_body():
	local = now_utc().astimezone(ZURICH)
	offset = int(local.utcoffset().total_seconds())
	return {
		"timeZone": "Europe/Zurich",
		"currentLocalTime": local.replace(tzinfo=None).isoformat(timespec="microseconds"),
		"currentUtcOffset": {"seconds": offset},
		"isDayLightSavingActive": bool(local.dst()),
	}


def secondary_body():
	utc = now_utc()
	return {
		"timezone": "Europe/Zurich",
		"utc_datetime": utc.isoformat(timespec="microseconds").replace("+00:00", "Z"),
		"unixtime": int(utc.timestamp()),
	}


class Handler(BaseHTTPRequestHandler):
	def log_message(self, *_):
		pass

	def _send(self, status, body):
		data = body if isinstance(body, bytes) else json.dumps(body).encode()
		self.send_response(status)
		self.send_header("Content-Type", "application/json")
		self.send_header("Content-Length", str(len(data)))
		self.end_headers()
		self.wfile.write(data)

	def _provider(self, name, body_factory):
		mode = state[name]
		if mode == "slow":
			time.sleep(state["slowSeconds"])
		if mode == "down":
			self._send(500, {"error": "stub is down"})
		elif mode == "garbage":
			self._send(200, b"<html>not json</html>")
		elif mode == "empty":
			# Valid JSON for the right zone, but the time itself is missing.
			body = body_factory()
			self._send(200, {key: (None if key in ("currentLocalTime", "utc_datetime") else value) for key, value in body.items()})
		else:
			self._send(200, body_factory())

	def do_GET(self):
		path = self.path.split("?")[0]
		if path == "/primary":
			self._provider("primary", primary_body)
		elif path == "/secondary":
			self._provider("secondary", secondary_body)
		elif path == "/control":
			self._send(200, {"utc": now_utc().isoformat(), "primary": state["primary"], "secondary": state["secondary"]})
		else:
			self._send(404, {"error": "not found"})

	def do_POST(self):
		if self.path != "/control":
			self._send(404, {"error": "not found"})
			return
		change = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0")) or 0) or b"{}")
		if "utc" in change:
			state["base_utc"] = datetime.fromisoformat(change["utc"].replace("Z", "+00:00")).astimezone(timezone.utc)
			state["base_monotonic"] = time.monotonic()
		for key in ("primary", "secondary", "slowSeconds"):
			if key in change:
				state[key] = change[key]
		self._send(200, {"utc": now_utc().isoformat(), "primary": state["primary"], "secondary": state["secondary"]})


if __name__ == "__main__":
	ThreadingHTTPServer(("0.0.0.0", 8090), Handler).serve_forever()
