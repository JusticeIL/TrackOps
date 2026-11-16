"""Runs the Lighthouse accessibility audit on all five screens of the running app.

Usage (stack running, Chrome or Edge installed, Node available):

    python tools/lighthouse_a11y.py

It signs in as an employee and as the manager through the API, passes the session cookies to
Lighthouse, and prints one score per screen plus any failed audit. Lighthouse is fetched by npx
into frontend/node_modules/.cache, so nothing is installed outside the project.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parent.parent
BASE_URL = os.environ.get("TRACKOPS_URL", "http://localhost:8080")
PAGES = [("Sign in", "/sign-in", None), ("Dashboard", "/", "employee"), ("History", "/history", "employee"),
		 ("Calendar", "/calendar", "employee"), ("Employees", "/employees", "manager")]
BROWSERS = [r"C:\Program Files\Google\Chrome\Application\chrome.exe", r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
			r"C:\Program Files\Microsoft\Edge\Application\msedge.exe", "/usr/bin/google-chrome", "/usr/bin/chromium"]


def session_cookie(username):
	http = requests.Session()
	token = http.get(f"{BASE_URL}/api/v1/auth/csrf", timeout=15).json()["token"]
	http.post(f"{BASE_URL}/api/v1/auth/session", json={"username": username, "password": "not-a-real-password"},
			  headers={"X-CSRF-Token": token}, timeout=15).raise_for_status()
	return "; ".join(f"{cookie.name}={cookie.value}" for cookie in http.cookies)


def audit(path, cookie, browser, folder):
	report = Path(folder) / f"{uuid.uuid4().hex}.json"
	command = ["npx", "--yes", "--cache", str(ROOT / "frontend" / "node_modules" / ".cache" / "npx"), "lighthouse@12", BASE_URL + path,
			   "--only-categories=accessibility", "--output=json", f"--output-path={report}", "--quiet",
			   "--chrome-flags=--headless=new --no-first-run"]
	if cookie:
		command.append("--extra-headers=" + json.dumps({"Cookie": cookie}))
	subprocess.run(command, check=True, shell=(os.name == "nt"), env={**os.environ, "CHROME_PATH": browser}, capture_output=True)
	result = json.loads(report.read_text(encoding="utf-8"))
	failed = [f"{item['id']}: {item['title']}" for item in result["audits"].values() if item.get("score") == 0]
	return round(result["categories"]["accessibility"]["score"] * 100), result.get("finalDisplayedUrl", ""), failed


def main():
	browser = next((candidate for candidate in BROWSERS if Path(candidate).exists()), shutil.which("chrome"))
	if not browser:
		sys.exit("No Chrome or Edge found; set CHROME_PATH and add it to BROWSERS.")
	cookies = {None: None, "employee": session_cookie("lighthouse.user"), "manager": session_cookie("admin")}
	worst = 100
	with tempfile.TemporaryDirectory() as folder:
		for name, path, role in PAGES:
			score, final_url, failed = audit(path, cookies[role], browser, folder)
			worst = min(worst, score)
			print(f"{name:<10} {score:>3}  {final_url}")
			for line in failed:
				print(f"             failed: {line}")
	print(f"lowest score: {worst}")
	sys.exit(0 if worst >= 90 else 1)


if __name__ == "__main__":
	main()
