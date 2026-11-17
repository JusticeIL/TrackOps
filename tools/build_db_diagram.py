"""Builds docs/db/schema.drawio: the database tables with their keys, constraints and indexes.

The tables below mirror the EF Core migrations in
backend/src/TrackOps.Infrastructure/Persistence/Migrations. When a migration changes the schema,
update this file in the same commit. Usage:

    python tools/build_db_diagram.py
    draw.io -x -f png -s 2 -b 24 -o docs/db/schema.png docs/db/schema.drawio
"""
from pathlib import Path

from build_api_diagram import GRAY, INK, LINE, PAPER, TEAL, TEAL_DARK, TEAL_TINT, TEXT, WHITE, Page

OUTPUT = Path(__file__).resolve().parent.parent / "docs" / "db" / "schema.drawio"

CARD_WIDTH, HEADER, ROW, RULE_ROW = 420, 40, 28, 22
# The horizontal gap must fit a connector label such as "ON DELETE RESTRICT".
COLUMN_GAP, ROW_GAP = 170, 90
LEFT, TOP = 40, 150
INSTANT = "datetime2(3)"


def column(name, sql_type, key="", nullable=False):
	return {"name": name, "type": sql_type, "key": key, "nullable": nullable}


def rule(kind, text, meaning=""):
	"""A constraint, index or note. A rule with a meaning is an invariant and is drawn highlighted."""
	return {"kind": kind, "text": text, "meaning": meaning}


TABLES = {
	"Users": {
		"note": "everyone who has signed in",
		"columns": [
			column("Id", "int identity", "PK"),
			column("Username", "nvarchar(32)"),
			column("DisplayName", "nvarchar(32)"),
			column("Role", "int"),
		],
		"rules": [
			rule("UNIQUE", "(Username)"),
			rule("CHECK", "Role IN (0, 1)"),
			rule("VALUES", "Role: 0 Employee · 1 Manager"),
		],
	},
	"Shifts": {
		"note": "one row per shift · instants only",
		"columns": [
			column("Id", "bigint identity", "PK"),
			column("UserId", "int", "FK"),
			column("ClockInUtc", INSTANT),
			column("ClockOutUtc", INSTANT, nullable=True),
			column("Status", "int"),
			column("TimeSource", "nvarchar(32)"),
			column("IsCorrected", "bit"),
			column("RowVersion", "rowversion"),
		],
		"rules": [
			rule("UNIQUE", "(UserId) WHERE ClockOutUtc IS NULL", "one open shift per user"),
			rule("INDEX", "(UserId, ClockInUtc DESC)"),
			rule("INDEX", "(ClockInUtc DESC)"),
			rule("CHECK", "ClockOutUtc IS NULL OR ClockOutUtc > ClockInUtc"),
			rule("CHECK", "Status = 0 exactly when ClockOutUtc IS NULL"),
			rule("VALUES", "Status: 0 Open · 1 Closed · 2 AutoClosed"),
		],
	},
	"Breaks": {
		"note": "breaks inside a shift",
		"columns": [
			column("Id", "bigint identity", "PK"),
			column("ShiftId", "bigint", "FK"),
			column("StartUtc", INSTANT),
			column("EndUtc", INSTANT, nullable=True),
		],
		"rules": [
			rule("UNIQUE", "(ShiftId) WHERE EndUtc IS NULL", "one open break per shift"),
			rule("INDEX", "(ShiftId)"),
			rule("CHECK", "EndUtc IS NULL OR EndUtc > StartUtc"),
		],
	},
	"ShiftAudits": {
		"note": "every change that was not a clock action",
		"columns": [
			column("Id", "bigint identity", "PK"),
			column("ShiftId", "bigint", "FK"),
			column("ChangedByUserId", "int", "FK", nullable=True),
			column("ChangedAtUtc", INSTANT),
			column("Kind", "int"),
			column("Reason", "nvarchar(300)"),
			column("OldClockInUtc", INSTANT),
			column("OldClockOutUtc", INSTANT, nullable=True),
			column("NewClockInUtc", INSTANT),
			column("NewClockOutUtc", INSTANT, nullable=True),
		],
		"rules": [
			rule("INDEX", "(ShiftId)"),
			rule("INDEX", "(ChangedByUserId)"),
			rule("VALUES", "Kind: 0 ManagerCorrection · 1 AutoClose"),
			rule("NOTE", "rows are only ever inserted (by the application)"),
		],
	},
	"EmployeeRequests": {
		"note": "requests and reports employees file",
		"columns": [
			column("Id", "bigint identity", "PK"),
			column("UserId", "int", "FK"),
			column("Type", "int"),
			column("Status", "int"),
			column("Title", "nvarchar(100)"),
			column("Content", "nvarchar(2000)"),
			column("SickDays", "int", nullable=True),
			column("CreatedAtUtc", INSTANT),
		],
		"rules": [
			rule("INDEX", "(UserId, Id DESC)"),
			rule("CHECK", "SickDays 1..30 exactly when Type = 3", "only a sickness report has sick days"),
			rule("CHECK", "Type BETWEEN 0 AND 6 · Status IN (0, 1, 2)"),
			rule("VALUES", "Type: 0 Vacation · 1 Expenses · 2 Training"),
			rule("", "3 Sickness · 4 Equipment · 5 ErrorReport · 6 Other"),
			rule("VALUES", "Status: 0 InProgress · 1 Approved · 2 Rejected"),
			rule("NOTE", "Id starts at 1001; it is the request number"),
		],
	},
	"IdempotencyRecords": {
		"note": "stored answers of recording requests",
		"columns": [
			column("UserId", "int", "PK"),
			column("Key", "uniqueidentifier", "PK"),
			column("Endpoint", "nvarchar(32)"),
			column("StatusCode", "int"),
			column("ResponseJson", "nvarchar(max)"),
			column("CreatedAtUtc", INSTANT),
		],
		"rules": [
			rule("KEY", "PRIMARY KEY (UserId, Key)", "a recording happens once per key"),
			rule("INDEX", "(CreatedAtUtc)"),
			rule("NOTE", "UserId is not a declared foreign key"),
		],
	},
}

VIEWS = [
	("v_ShiftReport", "ShiftId · UserId · Username · DisplayName · ClockInUtc · ClockOutUtc · Status · IsCorrected · "
					  "ShiftMinutes · BreakMinutes · WorkedMinutes", "one row per shift; minutes are NULL while the shift is open"),
	("v_BreakReport", "BreakId · ShiftId · UserId · Username · StartUtc · EndUtc · BreakMinutes", "one row per break; minutes are NULL while the break is open"),
]

KEY_STYLE = {
	"PK": f"rounded=1;arcSize=30;html=1;fontSize=10;fontStyle=1;fillColor={TEAL};strokeColor=none;fontColor={INK};",
	"FK": f"rounded=1;arcSize=30;html=1;fontSize=10;fontStyle=1;fillColor={INK};strokeColor=none;fontColor={PAPER};",
}
NULL_STYLE = f"rounded=1;arcSize=30;html=1;fontSize=10;fillColor={WHITE};strokeColor=#8A8A8A;fontColor={GRAY};"
MONO = TEXT + "align=left;fontFamily=Courier New;"


def rule_height(item):
	return RULE_ROW * 2 if item["meaning"] else RULE_ROW


def table_height(table):
	return HEADER + ROW * len(table["columns"]) + 16 + sum(rule_height(item) for item in table["rules"]) + 10


def column_anchor(table, name):
	"""Vertical position of a column's row as a fraction of the card height, for connectors."""
	index = [item["name"] for item in table["columns"]].index(name)
	return round((HEADER + ROW * index + ROW / 2) / table_height(table), 4)


def draw_table(page, name, x, y):
	table = TABLES[name]
	height = table_height(table)
	card = page.box("", f"rounded=1;arcSize=3;html=1;fillColor={WHITE};strokeColor={LINE};shadow=1;container=1;pointerEvents=0;collapsible=0;", x, y, CARD_WIDTH, height)
	page.box("", f"rounded=1;arcSize=14;html=1;fillColor={INK};strokeColor=none;", 0, 0, CARD_WIDTH, HEADER, card)
	page.box(name, MONO + f"fontSize=15;fontStyle=1;fontColor={PAPER};", 14, 0, 200, HEADER, card)
	page.box(table["note"], TEXT + f"align=right;fontSize=11;fontColor={TEAL};", 190, 0, CARD_WIDTH - 204, HEADER, card)

	for index, item in enumerate(table["columns"]):
		top = HEADER + ROW * index
		if item["key"]:
			page.box(item["key"], KEY_STYLE[item["key"]], 12, top + 5, 30, 18, card)
		page.box(item["name"], MONO + f"fontSize=13;fontStyle=1;fontColor={INK};", 52, top, 176, ROW, card)
		page.box(item["type"], MONO + f"fontSize=12;fontColor={GRAY};", 230, top, 130, ROW, card)
		if item["nullable"]:
			page.box("NULL", NULL_STYLE, 366, top + 5, 42, 18, card)

	top = HEADER + ROW * len(table["columns"]) + 8
	page.box("", f"html=1;fillColor={LINE};strokeColor=none;", 12, top, CARD_WIDTH - 24, 1, card)
	top += 8
	for item in table["rules"]:
		if item["meaning"]:
			page.box("", f"rounded=1;arcSize=14;html=1;fillColor={TEAL_TINT};strokeColor={TEAL};", 8, top + 1, CARD_WIDTH - 16, RULE_ROW * 2 - 2, card)
			page.box("→ " + item["meaning"], TEXT + f"align=left;fontSize=11;fontStyle=1;fontColor={TEAL_DARK};", 74, top + RULE_ROW - 2, CARD_WIDTH - 90, RULE_ROW, card)
		page.box(item["kind"], TEXT + f"align=left;fontSize=10;fontStyle=1;fontColor={GRAY};", 14, top, 58, RULE_ROW, card)
		page.box(item["text"], MONO + f"fontSize=11;fontColor={INK};", 74, top, CARD_WIDTH - 86, RULE_ROW, card)
		top += rule_height(item)
	return card


def relation(page, parent, child, label, exit_point, entry_point, optional=False):
	"""A foreign key: the single bar is the referenced row, the crow's foot the rows that reference it."""
	style = (f"edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;strokeColor={INK};strokeWidth=1.5;"
			 f"startArrow={'ERzeroToOne' if optional else 'ERmandOne'};startFill=0;startSize=10;endArrow=ERzeroToMany;endFill=0;endSize=10;"
			 f"exitX={exit_point[0]};exitY={exit_point[1]};entryX={entry_point[0]};entryY={entry_point[1]};"
			 f"fontSize=11;fontColor={INK};labelBackgroundColor={PAPER};" + ("dashed=1;" if optional else ""))
	page.edge(parent, child, style, label)


def build():
	page = Page("schema", "Database schema")
	column_x = [LEFT, LEFT + CARD_WIDTH + COLUMN_GAP, LEFT + 2 * (CARD_WIDTH + COLUMN_GAP)]
	width = column_x[2] + CARD_WIDTH - LEFT
	lower_top = TOP + table_height(TABLES["Shifts"]) + ROW_GAP

	page.box("TrackOps database", TEXT + f"align=left;fontSize=30;fontStyle=1;fontColor={INK};", LEFT, 20, 340, 44)
	page.box("SQL Server 2022", f"rounded=1;arcSize=50;html=1;fontSize=13;fontStyle=1;fillColor={TEAL};strokeColor=none;fontColor={INK};", 384, 30, 140, 26)
	page.box("Six tables and two reporting views. Only UTC instants are stored; every duration is derived. "
			 "The highlighted rules are invariants the database itself enforces, for any number of API replicas.",
			 TEXT + f"align=left;fontSize=13;fontColor={GRAY};", LEFT, 66, width, 24)

	users = draw_table(page, "Users", column_x[0], TOP)
	shifts = draw_table(page, "Shifts", column_x[1], TOP)
	breaks = draw_table(page, "Breaks", column_x[2], TOP)
	audits = draw_table(page, "ShiftAudits", column_x[1], lower_top)
	draw_table(page, "IdempotencyRecords", column_x[2], lower_top)
	requests = draw_table(page, "EmployeeRequests", column_x[0], lower_top)

	relation(page, users, shifts, "ON DELETE RESTRICT", (1, column_anchor(TABLES["Users"], "Id")), (0, column_anchor(TABLES["Shifts"], "UserId")))
	relation(page, shifts, breaks, "ON DELETE CASCADE", (1, column_anchor(TABLES["Shifts"], "Id")), (0, column_anchor(TABLES["Breaks"], "ShiftId")))
	relation(page, shifts, audits, "ShiftId · ON DELETE CASCADE", (0.5, 1), (0.5, 0))
	v_Optional = True
	# Leaves Users on its right side, so the line runs between the columns and not through EmployeeRequests below.
	relation(page, users, audits, "ChangedByUserId · optional", (1, 0.9),
			 (0, column_anchor(TABLES["ShiftAudits"], "ChangedByUserId")), v_Optional)
	relation(page, users, requests, "UserId · ON DELETE RESTRICT", (0.5, 1), (0.5, 0))

	bottom = lower_top + max(table_height(TABLES[name]) for name in ("ShiftAudits", "IdempotencyRecords", "EmployeeRequests"))
	page.box("REPORTING VIEWS  ·  DERIVED FROM THE TABLES ABOVE, NOTHING IS STORED", TEXT + f"align=left;fontSize=12;fontStyle=1;fontColor={TEAL_DARK};", LEFT, bottom + 36, width, 24)
	view_width = (width - 20) // 2
	for index, (name, columns, note) in enumerate(VIEWS):
		x = LEFT + index * (view_width + 20)
		view = page.box("", f"rounded=1;arcSize=6;html=1;fillColor={WHITE};strokeColor=#8A8A8A;dashed=1;container=1;pointerEvents=0;collapsible=0;", x, bottom + 66, view_width, 104)
		page.box(name, MONO + f"fontSize=14;fontStyle=1;fontColor={INK};", 14, 6, 300, 28, view)
		page.box(columns, MONO + f"whiteSpace=wrap;fontSize=11;fontColor={INK};", 14, 34, view_width - 28, 40, view)
		page.box(note, TEXT + f"align=left;fontSize=11;fontColor={GRAY};", 14, 74, view_width - 28, 24, view)

	legend = page.box("", f"rounded=1;arcSize=10;html=1;fillColor={WHITE};strokeColor={LINE};shadow=1;container=1;pointerEvents=0;collapsible=0;", LEFT, bottom + 194, width, 78)
	label = TEXT + f"align=left;fontSize=12;fontColor={GRAY};"
	page.box("LEGEND", TEXT + f"align=left;fontSize=12;fontStyle=1;fontColor={TEAL_DARK};", 16, 10, 70, 26, legend)
	page.box("PK", KEY_STYLE["PK"], 100, 14, 30, 18, legend)
	page.box("primary key", label, 138, 10, 90, 26, legend)
	page.box("FK", KEY_STYLE["FK"], 236, 14, 30, 18, legend)
	page.box("foreign key", label, 274, 10, 90, 26, legend)
	page.box("NULL", NULL_STYLE, 372, 14, 42, 18, legend)
	page.box("may be empty; every other column is NOT NULL", label, 422, 10, 300, 26, legend)
	page.box("", f"rounded=1;arcSize=30;html=1;fillColor={TEAL_TINT};strokeColor={TEAL};", 740, 14, 30, 18, legend)
	page.box("invariant enforced by the database", label, 778, 10, 230, 26, legend)
	page.box("Lines are foreign keys: the bar is the referenced row, the crow's foot the rows that point to it; a dashed line is optional.  ·  "
			 f"Every …Utc column is a UTC instant stored as {INSTANT}.", label, 16, 40, width - 32, 28, legend)
	return page


def main():
	page = build()
	OUTPUT.parent.mkdir(parents=True, exist_ok=True)
	OUTPUT.write_text('<mxfile host="TrackOps tools/build_db_diagram.py">' + page.xml() + "</mxfile>", encoding="utf-8")
	print(f"Wrote {OUTPUT} ({len(TABLES)} tables, {len(VIEWS)} views)")


if __name__ == "__main__":
	main()
