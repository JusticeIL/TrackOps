"""Builds docs/api/rest-api.drawio (two pages) from the endpoint table below.

The table is the single place to edit when the API changes; API.md must be
updated to match. Usage:

    python tools/build_api_diagram.py

Then export the PNGs with draw.io Desktop (see README, "API diagram").
"""
from html import escape
from pathlib import Path

BASE_URL = "http://localhost:8080/api/v1"
OUTPUT = Path(__file__).resolve().parent.parent / "docs" / "api" / "rest-api.drawio"

INK, PAPER, WHITE = "#0A0A0A", "#FAFAFA", "#FFFFFF"
TEAL, TEAL_DARK, TEAL_TINT = "#5AC2C5", "#1F6F72", "#E6F5F5"
GRAY, LINE, RED, RED_TINT = "#444444", "#E2E2E2", "#C2362B", "#FDECEC"
METHOD_FILL = {"GET": "#1D6FB8", "POST": "#1E7F3E", "PATCH": "#7A4FBF", "DELETE": RED, "PUT": "#9A5B00"}

# group -> (header note, [(method, path, purpose, role, main status codes)])
# A star marks a recording endpoint (external time + Idempotency-Key).
GROUPS = {
	"Auth": ("HttpOnly · Secure · SameSite cookies + CSRF token", [
		("GET", "/auth/csrf", "Issue CSRF token (cookie + body)", "Public", "200"),
		("POST", "/auth/session", "Sign in · rotate session cookies", "Public", "200 · 400"),
		("GET", "/auth/session", "Current user and role", "Employee", "200 · 401"),
		("DELETE", "/auth/session", "Sign out · revoke Redis session", "Employee", "204"),
	]),
	"Time": ("display only · recording never trusts the client clock", [
		("GET", "/time", "Zurich time · seeds display clock", "Employee", "200 · 503"),
	]),
	"Shifts": ("own shifts only · one open shift per user (DB-enforced)", [
		("POST", "/shifts", "Clock in ★", "Employee", "201 · 409 · 503"),
		("POST", "/shifts/current/clock-out", "Clock out · closes open break ★", "Employee", "200 · 409 · 503"),
		("GET", "/shifts/current", "Open shift with its breaks", "Employee", "200 · 204"),
		("GET", "/shifts/{shiftId}", "One own shift with breaks", "Employee", "200 · 404"),
		("GET", "/shifts/activity", "Recent in / out / break events", "Employee", "200"),
		("QUERY", "/shifts", "History: filter · search · paginate", "Employee", "200 · 400"),
	]),
	"Breaks": ("inside the open shift · one open break at a time", [
		("POST", "/shifts/current/breaks", "Start break ★", "Employee", "201 · 409 · 503"),
		("POST", "/shifts/current/breaks/current/end", "End break ★", "Employee", "200 · 409 · 503"),
	]),
	"Calendar": ("Europe/Zurich local days · DST-aware (23 h / 25 h days)", [
		("GET", "/calendar/months/{yyyy-MM}", "Per-day worked + overtime, totals", "Employee", "200 · 400"),
		("GET", "/calendar/days/{yyyy-MM-dd}", "Day summary + that day's shifts", "Employee", "200 · 400"),
	]),
	"Employees": ("Manager role only · corrections leave an audit trail", [
		("QUERY", "/employees", "Autocomplete employees by name", "Manager", "200 · 403"),
		("QUERY", "/employees/shifts", "All shifts: filter · search · paginate", "Manager", "200 · 403"),
		("PATCH", "/employees/{userId}/shifts/{shiftId}", "Correct times · reason · audited", "Manager", "200 · 404 · 409"),
		("GET", "/employees/{userId}/shifts/{shiftId}/audit", "Correction audit trail", "Manager", "200 · 404"),
	]),
}
LEFT_COLUMN, RIGHT_COLUMN = ["Auth", "Time", "Shifts"], ["Breaks", "Calendar", "Employees"]

TEXT = "text;html=1;verticalAlign=middle;"
CARD = f"rounded=1;arcSize=4;html=1;fillColor={WHITE};strokeColor={LINE};shadow=1;container=1;pointerEvents=0;collapsible=0;"
ROLE_STYLE = {
	"Public": f"rounded=1;arcSize=30;html=1;fontSize=11;fillColor=#EFEFEF;strokeColor=#BDBDBD;fontColor={INK};",
	"Employee": f"rounded=1;arcSize=30;html=1;fontSize=11;fillColor={WHITE};strokeColor={INK};fontColor={INK};",
	"Manager": f"rounded=1;arcSize=30;html=1;fontSize=11;fontStyle=1;fillColor={INK};strokeColor={INK};fontColor={TEAL};",
}


def method_style(method):
	if method == "QUERY":
		return f"rounded=1;arcSize=50;html=1;fontSize=11;fontStyle=1;strokeColor={INK};fontColor={INK};fillColor={TEAL};"
	return f"rounded=1;arcSize=50;html=1;fontSize=11;fontStyle=1;strokeColor=none;fontColor={WHITE};fillColor={METHOD_FILL[method]};"


class Page:
	def __init__(self, page_id, name):
		self.page_id, self.name, self.cells, self.count = page_id, name, [], 0

	def _next_id(self):
		self.count += 1
		return f"{self.page_id}-{self.count}"

	def box(self, value, style, x, y, w, h, parent="1"):
		cell_id = self._next_id()
		self.cells.append(
			f'<mxCell id="{cell_id}" value="{escape(value, quote=True)}" style="{style}" vertex="1" parent="{parent}">'
			f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry"/></mxCell>')
		return cell_id

	def edge(self, source, target, style, value="", parent="1"):
		self.cells.append(
			f'<mxCell id="{self._next_id()}" value="{escape(value, quote=True)}" style="{style}" edge="1" parent="{parent}" '
			f'source="{source}" target="{target}"><mxGeometry relative="1" as="geometry"/></mxCell>')

	def xml(self):
		return (f'<diagram id="{self.page_id}" name="{self.name}"><mxGraphModel adaptiveColors="none" background="{PAPER}" '
				f'page="0" grid="0"><root><mxCell id="0"/><mxCell id="1" parent="0"/>{"".join(self.cells)}</root></mxGraphModel></diagram>')


def two_lines(title, detail):
	return f'<b>{title}</b><br><font style="font-size:11px">{detail}</font>'


def card_height(group):
	return 50 + 34 * len(GROUPS[group][1]) + 12


def draw_card(page, group, x, y):
	note, endpoints = GROUPS[group]
	card = page.box("", CARD, x, y, 840, card_height(group))
	page.box("", f"rounded=1;arcSize=50;html=1;fillColor={TEAL};strokeColor=none;", 14, 15, 6, 20, card)
	page.box(group, TEXT + f"align=left;fontSize=17;fontStyle=1;fontColor={INK};", 30, 10, 200, 30, card)
	page.box(note, TEXT + f"align=right;fontSize=12;fontColor={TEAL_DARK};", 420, 10, 408, 30, card)
	for row, (method, path, purpose, role, codes) in enumerate(endpoints):
		top = 50 + 34 * row
		page.box(method, method_style(method), 14, top + 6, 60, 22, card)
		page.box(path, TEXT + f"align=left;fontFamily=Courier New;fontSize=12;fontStyle=1;fontColor={INK};", 84, top, 314, 34, card)
		page.box(purpose, TEXT + f"align=left;fontSize=12;fontColor={GRAY};", 412, top, 214, 34, card)
		page.box(role, ROLE_STYLE[role], 632, top + 6, 84, 22, card)
		page.box(codes, TEXT + f"align=right;fontFamily=Courier New;fontSize=11;fontColor={GRAY};", 722, top, 106, 34, card)


def draw_column(page, groups, x):
	y = 220
	for group in groups:
		draw_card(page, group, x, y)
		y += card_height(group) + 20
	return y - 20


def build_api_map():
	page = Page("map", "API map")
	page.box("TrackOps REST API", TEXT + f"align=left;fontSize=30;fontStyle=1;fontColor={INK};", 30, 20, 330, 44)
	page.box("v1", f"rounded=1;arcSize=50;html=1;fontSize=14;fontStyle=1;fillColor={TEAL};strokeColor=none;fontColor={INK};", 366, 30, 46, 26)
	page.box(f"Base URL&nbsp;&nbsp;<b>{BASE_URL}</b>",
			 f"rounded=1;arcSize=30;html=1;fontFamily=Courier New;fontSize=14;fillColor={WHITE};strokeColor={INK};fontColor={INK};", 1340, 26, 400, 34)
	page.box("Shift-clock system  ·  JSON  ·  cookie auth (JWT + Redis session)  ·  errors as RFC 9457 problem+json  ·  "
			 "all recorded times are UTC instants from an external time API",
			 TEXT + f"align=left;fontSize=13;fontColor={GRAY};", 30, 66, 1300, 24)

	strip = page.box("", f"rounded=1;arcSize=14;html=1;fillColor={INK};strokeColor=none;shadow=1;container=1;pointerEvents=0;collapsible=0;", 30, 104, 1710, 92)
	node = f"rounded=1;arcSize=20;html=1;fontSize=13;strokeColor=none;fontColor={INK};"
	browser = page.box(two_lines("Browser", "React SPA · Hebrew RTL"), node + f"fillColor={PAPER};", 24, 22, 210, 48, strip)
	nginx = page.box(two_lines("nginx", "static files · load balancer"), node + f"fillColor={PAPER};", 320, 22, 290, 48, strip)
	api = page.box(two_lines("ASP.NET Core API × 2 replicas", "stateless · async"), node + f"fillColor={TEAL};", 696, 22, 290, 48, strip)
	stores = page.box("", f"rounded=1;arcSize=20;html=1;fillColor=none;strokeColor={TEAL};dashed=1;", 1072, 14, 620, 64, strip)
	page.box(two_lines("Redis", "sessions · limits · cache"), node + f"fillColor={PAPER};", 1082, 22, 190, 48, strip)
	page.box(two_lines("SQL Server", "shifts · breaks · audit"), node + f"fillColor={PAPER};", 1284, 22, 190, 48, strip)
	page.box(two_lines("External time API", "Europe/Zurich"), node + f"fillColor={PAPER};", 1486, 22, 196, 48, strip)
	arrow = f"edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;strokeColor={PAPER};strokeWidth=2;endArrow=block;"
	page.edge(browser, nginx, arrow, parent=strip)
	page.edge(nginx, api, arrow, parent=strip)
	page.edge(api, stores, arrow, parent=strip)

	left_bottom = draw_column(page, LEFT_COLUMN, 30)
	right_bottom = draw_column(page, RIGHT_COLUMN, 900)
	bottom = max(left_bottom, right_bottom)
	note_top = right_bottom + 20
	page.box("<b>QUERY</b> (RFC 10008) is safe and idempotent and carries the filter as a JSON body: "
			 "no personal data in the URL, no CSRF token needed.<br><b>No POST fallback by design:</b> a proxy or client that "
			 "rejects QUERY cannot search. Known limit, recorded in DECISIONS.md.",
			 f"rounded=1;arcSize=12;html=1;whiteSpace=wrap;align=left;spacingLeft=14;spacingRight=14;fontSize=12;fillColor={TEAL_TINT};"
			 f"strokeColor={TEAL};fontColor={INK};", 900, note_top, 840, bottom - note_top)

	legend = page.box("", CARD.replace("arcSize=4", "arcSize=8"), 30, bottom + 24, 1710, 104)
	label = TEXT + f"align=left;fontSize=12;fontColor={GRAY};"
	page.box("LEGEND", TEXT + f"align=left;fontSize=12;fontStyle=1;fontColor={TEAL_DARK};", 16, 12, 70, 26, legend)
	page.box("Methods", label, 100, 12, 60, 26, legend)
	for index, method in enumerate(["GET", "POST", "PATCH", "DELETE", "QUERY"]):
		page.box(method, method_style(method), 166 + 70 * index, 14, 60, 22, legend)
	page.box("PUT", method_style("PUT"), 540, 14, 60, 22, legend)
	page.box("not used in v1", label, 606, 12, 100, 26, legend)
	page.box("Roles", label, 976, 12, 44, 26, legend)
	for index, role in enumerate(["Public", "Employee", "Manager"]):
		page.box(role, ROLE_STYLE[role], 1022 + 94 * index, 14, 84, 22, legend)
	page.box("Employee = any signed-in user, managers included", label, 1306, 12, 390, 26, legend)
	page.box("<b>★ Recording endpoint:</b> requires an Idempotency-Key header; its timestamp comes only from the external time API, "
			 "and it returns 503 when that source is unavailable (fail closed).&nbsp;&nbsp;·&nbsp;&nbsp;Any Employee or Manager endpoint "
			 "may also return 401 or 429.&nbsp;&nbsp;·&nbsp;&nbsp;POST, PATCH and DELETE require the X-CSRF-Token header, otherwise 403."
			 "&nbsp;&nbsp;·&nbsp;&nbsp;Internal only, not routed by nginx: GET /health/live and GET /health/ready.",
			 TEXT + f"whiteSpace=wrap;align=left;fontSize=12;fontColor={GRAY};", 16, 46, 1678, 48, legend)
	return page


# (title, detail, dependency chip or None, (edge label, outcome title, outcome detail, is_rejection) or None, label on the arrow to the next step)
FLOW_STEPS = [
	("1 · Browser sends the request", "cookies (JWT + session id) · X-CSRF-Token · Idempotency-Key", None, None, ""),
	("2 · nginx", "forwards to replica 1 or 2", None, None, ""),
	("3 · CSRF check", "header token must match the CSRF cookie", None, ("mismatch", "403", "csrf_invalid", True), ""),
	("4 · JWT + session check", "signature · expiry · sid claim = cookie = live Redis session", "Redis", ("invalid", "401", "unauthenticated", True), ""),
	("5 · Rate limit", "per-user counter shared by both replicas", "Redis", ("over limit", "429", "rate_limited + Retry-After", True), ""),
	("6 · Idempotency-Key lookup", "has this user already sent this key?", "SQL Server", ("seen before", "Replay stored response", "same 201 · no second shift", False), "new key"),
	("7 · Fetch Zurich time", "short timeout · one retry · second provider · circuit breaker", "External time API", ("down or slow", "503", "time_source_unavailable — FAIL CLOSED", True), "UTC instant"),
	("8 · INSERT shift + idempotency record", "one transaction · unique index: one open shift per user", "SQL Server", ("index violated", "409", "shift_already_open", True), "committed"),
	("9 · 201 Created", "shift JSON · Location: /shifts/{shiftId}", None, None, ""),
]


def build_clock_in_flow():
	page = Page("flow", "Clock-in flow")
	page.box("Clock-in flow", TEXT + f"align=left;fontSize=28;fontStyle=1;fontColor={INK};", 30, 20, 300, 40)
	page.box("POST /api/v1/shifts", f"rounded=1;arcSize=30;html=1;fontFamily=Courier New;fontSize=14;fontStyle=1;fillColor={WHITE};"
			 f"strokeColor={INK};fontColor={INK};", 800, 24, 220, 32)
	page.box("Every check runs on any replica; shared state lives only in Redis and SQL Server. "
			 "The recorded timestamp never comes from the browser or the server clock.",
			 TEXT + f"align=left;fontSize=13;fontColor={GRAY};", 30, 62, 990, 24)

	step_style = f"rounded=1;arcSize=14;whiteSpace=wrap;html=1;fontSize=13;shadow=1;fontColor={INK};"
	main_arrow = f"edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;strokeColor={INK};strokeWidth=2;endArrow=block;fontSize=11;fontColor={INK};labelBackgroundColor={PAPER};"
	previous, previous_label, last = None, "", len(FLOW_STEPS) - 1
	for index, (title, detail, dependency, outcome, next_label) in enumerate(FLOW_STEPS):
		y = 110 + 92 * index
		if index == 0:
			style = step_style.replace(f"fontColor={INK}", f"fontColor={PAPER}") + f"fillColor={INK};strokeColor=none;"
		elif index == last:
			style = step_style + f"fillColor={TEAL};strokeColor=none;"
		else:
			style = step_style + f"fillColor={WHITE};strokeColor={INK};"
		step = page.box(two_lines(title, detail), style, 260, y, 380, 60)
		if previous:
			page.edge(previous, step, main_arrow, previous_label)
		previous, previous_label = step, next_label
		if dependency:
			chip_color = TEAL if dependency == "External time API" else PAPER
			chip = page.box(dependency, f"rounded=1;arcSize=40;html=1;fontSize=12;fontStyle=1;fillColor={INK};strokeColor=none;fontColor={chip_color};", 30, y + 12, 150, 36)
			page.edge(chip, step, "endArrow=none;dashed=1;html=1;strokeColor=#8A8A8A;")
		if outcome:
			edge_label, outcome_title, outcome_detail, is_rejection = outcome
			color, fill = (RED, RED_TINT) if is_rejection else (TEAL_DARK, TEAL_TINT)
			result = page.box(two_lines(outcome_title, outcome_detail),
							  f"rounded=1;arcSize=14;whiteSpace=wrap;html=1;fontSize=12;fillColor={fill};strokeColor={color};fontColor={INK};", 770, y + 6, 250, 48)
			page.edge(step, result, f"edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;strokeColor={color};strokeWidth=1.5;endArrow=block;"
					  f"fontSize=11;fontColor={color};verticalAlign=bottom;", edge_label)

	page.box("<b>Legend</b>&nbsp;&nbsp;&nbsp;Black arrow: happy path&nbsp;&nbsp;·&nbsp;&nbsp;Red arrow: request rejected, nothing written"
			 "&nbsp;&nbsp;·&nbsp;&nbsp;Teal arrow: safe retry&nbsp;&nbsp;·&nbsp;&nbsp;Dashed line: dependency used by the step",
			 f"rounded=1;arcSize=20;html=1;whiteSpace=wrap;align=left;spacingLeft=14;fontSize=12;fillColor={WHITE};strokeColor={LINE};"
			 f"fontColor={GRAY};shadow=1;", 30, 110 + 92 * len(FLOW_STEPS) + 6, 990, 40)
	return page


def main():
	pages = [build_api_map(), build_clock_in_flow()]
	OUTPUT.parent.mkdir(parents=True, exist_ok=True)
	OUTPUT.write_text('<mxfile host="TrackOps tools/build_api_diagram.py">' + "".join(page.xml() for page in pages) + "</mxfile>", encoding="utf-8")
	total = sum(len(endpoints) for _, endpoints in GROUPS.values())
	print(f"Wrote {OUTPUT} ({total} endpoints, {len(pages)} pages)")


if __name__ == "__main__":
	main()
