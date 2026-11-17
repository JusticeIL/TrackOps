"""Builds docs/api/rest-api.drawio (two pages) from the endpoint tree below.

Page 1 draws the API as a folder tree, page 2 the clock-in flow. The tree is the single place
to edit when the API changes; API.md must be updated to match. Usage:

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

# The API as a folder tree: the single place to edit when the API changes.
# A folder is a path segment. An endpoint is a leaf; its full path is every folder above it plus
# its own path. A star marks a recording endpoint (external time + Idempotency-Key).
def folder(path, note, *children):
	return {"path": path, "note": note, "children": list(children)}


def endpoint(method, path, purpose, role, codes):
	return {"method": method, "path": path, "purpose": purpose, "role": role, "codes": codes}


API_TREE = folder(
	"/api/v1", "",
	folder(
		"/auth", "HttpOnly · Secure · SameSite cookies + CSRF token",
		endpoint("GET", "/csrf", "Issue CSRF token (cookie + body)", "Public", "200"),
		endpoint("POST", "/session", "Sign in · rotate session cookies", "Public", "200 · 400"),
		endpoint("GET", "/session", "Current user and role", "Employee", "200 · 401"),
		endpoint("DELETE", "/session", "Sign out · revoke Redis session", "Employee", "204"),
	),
	folder(
		"/time", "display only · recording never trusts the client clock",
		endpoint("GET", "/", "Zurich time · seeds display clock", "Employee", "200 · 503"),
	),
	folder(
		"/shifts", "own shifts only · one open shift per user (DB-enforced)",
		endpoint("POST", "/", "Clock in ★", "Employee", "201 · 409 · 503"),
		endpoint("QUERY", "/", "History: filter · paginate", "Employee", "200 · 400"),
		endpoint("GET", "/activity", "Recent in / out / break events", "Employee", "200"),
		endpoint("GET", "/{shiftId}", "One own shift with breaks", "Employee", "200 · 404"),
		folder(
			"/current", "the signed-in user's open shift",
			endpoint("GET", "/", "Open shift with its breaks", "Employee", "200 · 204"),
			endpoint("POST", "/clock-out", "Clock out · closes open break ★", "Employee", "200 · 409 · 503"),
			folder(
				"/breaks", "one open break at a time",
				endpoint("POST", "/current/start", "Start break ★", "Employee", "201 · 409 · 503"),
				endpoint("POST", "/current/end", "End break ★", "Employee", "200 · 409 · 503"),
			),
		),
	),
	folder(
		"/calendar", "Europe/Zurich local days · DST-aware (23 h / 25 h days)",
		endpoint("GET", "/months/{yyyy-MM}", "Per-day worked + overtime, totals", "Employee", "200 · 400"),
		endpoint("GET", "/days/{yyyy-MM-dd}", "Day summary + that day's shifts", "Employee", "200 · 400"),
	),
	folder(
		"/requests", "own requests and reports · filing time from the external time API",
		endpoint("GET", "/", "Own requests, newest first", "Employee", "200"),
		endpoint("POST", "/", "File a request or a sickness report", "Employee", "201 · 400 · 503"),
	),
	folder(
		"/employees", "Manager only · the role is off unless MANAGER_USERNAME is set",
		endpoint("QUERY", "/", "Autocomplete employees by name", "Manager", "200 · 403"),
		endpoint("QUERY", "/shifts", "All shifts: filter · search · paginate", "Manager", "200 · 403"),
		folder(
			"/{userId}/shifts/{shiftId}", "one shift of one employee",
			endpoint("PATCH", "/", "Correct times · reason · audited", "Manager", "200 · 404 · 409"),
			endpoint("GET", "/audit", "Correction audit trail", "Manager", "200 · 404"),
		),
	),
)

# Served by the API for container health checks; the gateway does not route it.
INTERNAL_TREE = folder(
	"/health", "container health checks",
	endpoint("GET", "/live", "Liveness probe", "Internal", "200"),
	endpoint("GET", "/ready", "Readiness probe", "Internal", "200"),
)

TEXT = "text;html=1;verticalAlign=middle;"
CARD = f"rounded=1;arcSize=2;html=1;fillColor={WHITE};strokeColor={LINE};shadow=1;"
ROLE_STYLE = {
	"Public": f"rounded=1;arcSize=30;html=1;fontSize=11;fillColor=#EFEFEF;strokeColor=#BDBDBD;fontColor={INK};",
	"Employee": f"rounded=1;arcSize=30;html=1;fontSize=11;fillColor={WHITE};strokeColor={INK};fontColor={INK};",
	"Manager": f"rounded=1;arcSize=30;html=1;fontSize=11;fontStyle=1;fillColor={INK};strokeColor={INK};fontColor={TEAL};",
	"Internal": f"rounded=1;arcSize=30;html=1;fontSize=11;fillColor={WHITE};strokeColor=#8A8A8A;dashed=1;fontColor={GRAY};",
}

# Tree geometry. Columns on the right are fixed, so comments, roles and codes line up at every depth.
PAGE_LEFT, PAGE_WIDTH = 30, 1200
ROW, INDENT, GROUP_GAP, CARD_PADDING = 34, 46, 12, 20
TREE_LEFT = PAGE_LEFT + 24
COMMENT_X, ROLE_X, CODES_X = 610, 946, 1040
TREE_RIGHT = PAGE_LEFT + PAGE_WIDTH - 24
TREE_LINE = "#9A9A9A"


def method_style(method):
	if method == "QUERY":
		return f"rounded=1;arcSize=50;html=1;fontSize=11;fontStyle=1;strokeColor=none;fontColor={INK};fillColor={TEAL};"
	return f"rounded=1;arcSize=50;html=1;fontSize=11;fontStyle=1;strokeColor=none;fontColor={WHITE};fillColor={METHOD_FILL[method]};"


def folder_style(is_root):
	fill, text = (TEAL, INK) if is_root else (INK, PAPER)
	return f"rounded=1;arcSize=30;html=1;fontFamily=Courier New;fontSize=13;fontStyle=1;fillColor={fill};strokeColor=none;fontColor={text};"


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


def is_endpoint(node):
	return "method" in node


def count_endpoints(node):
	return 1 if is_endpoint(node) else sum(count_endpoints(child) for child in node["children"])


def tree_height(node, level=0):
	"""Height the node will take, so the card behind the tree can be drawn first."""
	if is_endpoint(node):
		return ROW
	gap = GROUP_GAP if level == 0 else 0
	return ROW + sum(gap + tree_height(child, level + 1) for child in node["children"])


def tree_line(page, x, y, w, h):
	page.box("", f"html=1;fillColor={TREE_LINE};strokeColor=none;", x, y, w, h)


def draw_tree(page, node, y, level=0, is_root=False):
	"""Draws a folder or an endpoint on the row starting at y. Returns (next free y, this row's centre y)."""
	x = TREE_LEFT + INDENT * level
	centre = y + ROW // 2
	if is_endpoint(node):
		page.box(node["method"], method_style(node["method"]), x, y + 6, 60, 22)
		page.box(node["path"], TEXT + f"align=left;fontFamily=Courier New;fontSize=13;fontStyle=1;fontColor={INK};", x + 70, y, COMMENT_X - x - 80, ROW)
		page.box("# " + node["purpose"], TEXT + f"align=left;fontSize=12;fontColor={GRAY};", COMMENT_X, y, ROLE_X - COMMENT_X - 10, ROW)
		page.box(node["role"], ROLE_STYLE[node["role"]], ROLE_X, y + 6, 84, 22)
		page.box(node["codes"], TEXT + f"align=right;fontFamily=Courier New;fontSize=11;fontColor={GRAY};", CODES_X, y, TREE_RIGHT - CODES_X, ROW)
		return y + ROW, centre

	page.box(node["path"], folder_style(is_root), x, y + 4, int(len(node["path"]) * 8.4) + 26, 26)
	if node["note"]:
		page.box("# " + node["note"], TEXT + f"align=left;fontSize=12;fontColor={TEAL_DARK};", COMMENT_X, y, TREE_RIGHT - COMMENT_X, ROW)
	trunk_x, trunk_top, next_y, last_centre = x + 16, y + 30, y + ROW, centre
	for child in node["children"]:
		if level == 0:
			next_y += GROUP_GAP
		next_y, last_centre = draw_tree(page, child, next_y, level + 1)
		tree_line(page, trunk_x, last_centre - 1, INDENT - 20, 2)
	tree_line(page, trunk_x - 1, trunk_top, 2, last_centre - trunk_top + 1)
	return next_y, centre


def draw_tree_card(page, tree, y, is_root):
	"""A white card with one tree inside it. Returns the y below the card."""
	height = tree_height(tree) + 2 * CARD_PADDING
	page.box("", CARD, PAGE_LEFT, y, PAGE_WIDTH, height)
	draw_tree(page, tree, y + CARD_PADDING, is_root=is_root)
	return y + height


def build_api_map():
	page = Page("map", "API map")
	right = PAGE_LEFT + PAGE_WIDTH
	page.box("TrackOps REST API", TEXT + f"align=left;fontSize=30;fontStyle=1;fontColor={INK};", PAGE_LEFT, 20, 330, 44)
	page.box("v1", f"rounded=1;arcSize=50;html=1;fontSize=14;fontStyle=1;fillColor={TEAL};strokeColor=none;fontColor={INK};", 366, 30, 46, 26)
	page.box(f"Base URL&nbsp;&nbsp;<b>{BASE_URL}</b>",
			 f"rounded=1;arcSize=30;html=1;fontFamily=Courier New;fontSize=14;fillColor={WHITE};strokeColor={INK};fontColor={INK};", right - 400, 26, 400, 34)
	page.box("Shift-clock system  ·  JSON  ·  cookie auth (JWT + Redis session)  ·  errors as RFC 9457 problem+json  ·  "
			 "all recorded times are UTC instants from an external time API",
			 TEXT + f"align=left;fontSize=13;fontColor={GRAY};", PAGE_LEFT, 66, PAGE_WIDTH, 24)

	strip = page.box("", f"rounded=1;arcSize=14;html=1;fillColor={INK};strokeColor=none;shadow=1;container=1;pointerEvents=0;collapsible=0;", PAGE_LEFT, 104, PAGE_WIDTH, 92)
	node = f"rounded=1;arcSize=20;html=1;fontSize=13;strokeColor=none;fontColor={INK};"
	browser = page.box(two_lines("Browser", "React SPA · Hebrew RTL"), node + f"fillColor={PAPER};", 24, 22, 160, 48, strip)
	nginx = page.box(two_lines("nginx", "static files · load balancer"), node + f"fillColor={PAPER};", 228, 22, 190, 48, strip)
	api = page.box(two_lines("ASP.NET Core API × 2", "stateless · async"), node + f"fillColor={TEAL};", 462, 22, 200, 48, strip)
	stores = page.box("", f"rounded=1;arcSize=20;html=1;fillColor=none;strokeColor={TEAL};dashed=1;", 706, 14, 478, 64, strip)
	page.box(two_lines("Redis", "sessions · limits · cache"), node + f"fillColor={PAPER};", 716, 22, 150, 48, strip)
	page.box(two_lines("SQL Server", "shifts · breaks · audit"), node + f"fillColor={PAPER};", 874, 22, 150, 48, strip)
	page.box(two_lines("External time API", "Europe/Zurich"), node + f"fillColor={PAPER};", 1032, 22, 142, 48, strip)
	arrow = f"edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;strokeColor={PAPER};strokeWidth=2;endArrow=block;"
	page.edge(browser, nginx, arrow, parent=strip)
	page.edge(nginx, api, arrow, parent=strip)
	page.edge(api, stores, arrow, parent=strip)

	v_IsRoot = True
	bottom = draw_tree_card(page, API_TREE, 220, v_IsRoot)
	page.box("INTERNAL  ·  SERVED FOR CONTAINER HEALTH CHECKS, NOT ROUTED BY THE GATEWAY",
			 TEXT + f"align=left;fontSize=12;fontStyle=1;fontColor={TEAL_DARK};", PAGE_LEFT, bottom + 16, PAGE_WIDTH, 24)
	bottom = draw_tree_card(page, INTERNAL_TREE, bottom + 46, not v_IsRoot)

	page.box("<b>QUERY</b> (RFC 10008) is safe and idempotent and carries the filter as a JSON body: "
			 "no personal data in the URL, no CSRF token needed.<br><b>No POST fallback by design:</b> a proxy or client that "
			 "rejects QUERY cannot search. Known limit, recorded in DECISIONS.md.",
			 f"rounded=1;arcSize=12;html=1;whiteSpace=wrap;align=left;spacingLeft=14;spacingRight=14;fontSize=12;fillColor={TEAL_TINT};"
			 f"strokeColor={TEAL};fontColor={INK};", PAGE_LEFT, bottom + 20, PAGE_WIDTH, 56)

	legend = page.box("", CARD.replace("arcSize=2", "arcSize=8") + "container=1;pointerEvents=0;collapsible=0;", PAGE_LEFT, bottom + 96, PAGE_WIDTH, 146)
	label = TEXT + f"align=left;fontSize=12;fontColor={GRAY};"
	page.box("LEGEND", TEXT + f"align=left;fontSize=12;fontStyle=1;fontColor={TEAL_DARK};", 16, 12, 70, 26, legend)
	page.box("Methods", label, 100, 12, 60, 26, legend)
	for index, method in enumerate(["GET", "POST", "PATCH", "DELETE", "QUERY"]):
		page.box(method, method_style(method), 166 + 70 * index, 14, 60, 22, legend)
	page.box("PUT", method_style("PUT"), 540, 14, 60, 22, legend)
	page.box("not used in v1", label, 606, 12, 100, 26, legend)
	page.box("/folder", folder_style(not v_IsRoot), 740, 12, 84, 26, legend)
	page.box("a path segment; each leaf adds its own path", label, 834, 12, 350, 26, legend)
	page.box("Roles", label, 100, 46, 60, 26, legend)
	for index, role in enumerate(["Public", "Employee", "Manager", "Internal"]):
		page.box(role, ROLE_STYLE[role], 166 + 94 * index, 48, 84, 22, legend)
	page.box("Employee = any signed-in user, managers included  ·  Internal = reachable by containers only", label, 552, 46, 632, 26, legend)
	page.box("<b>★ Recording endpoint:</b> requires an Idempotency-Key header; its timestamp comes only from the external time API, "
			 "and it returns 503 when that source is unavailable (fail closed).&nbsp;&nbsp;·&nbsp;&nbsp;Any Employee or Manager endpoint "
			 "may also return 401 or 429.&nbsp;&nbsp;·&nbsp;&nbsp;POST, PATCH and DELETE require the X-CSRF-Token header, otherwise 403.",
			 TEXT + f"whiteSpace=wrap;align=left;fontSize=12;fontColor={GRAY};", 16, 82, PAGE_WIDTH - 32, 52, legend)
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
	total = count_endpoints(API_TREE)
	print(f"Wrote {OUTPUT} ({total} endpoints, {len(pages)} pages)")


if __name__ == "__main__":
	main()
