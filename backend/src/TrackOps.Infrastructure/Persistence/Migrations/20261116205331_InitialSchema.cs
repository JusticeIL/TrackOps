using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.UserId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Role", "[Role] IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClockInUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    ClockOutUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TimeSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsCorrected = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                    table.CheckConstraint("CK_Shifts_ClockOutAfterClockIn", "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]");
                    table.CheckConstraint("CK_Shifts_StatusMatchesClockOut", "([Status] = 0 AND [ClockOutUtc] IS NULL) OR ([Status] IN (1, 2) AND [ClockOutUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Shifts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Breaks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<long>(type: "bigint", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Breaks", x => x.Id);
                    table.CheckConstraint("CK_Breaks_EndAfterStart", "[EndUtc] IS NULL OR [EndUtc] > [StartUtc]");
                    table.ForeignKey(
                        name: "FK_Breaks_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OldClockInUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    OldClockOutUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    NewClockInUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    NewClockOutUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftAudits_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftAudits_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Breaks_Shift",
                table: "Breaks",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "UX_Breaks_OneOpenPerShift",
                table: "Breaks",
                column: "ShiftId",
                unique: true,
                filter: "[EndUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CreatedAt",
                table: "IdempotencyRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAudits_ChangedByUserId",
                table: "ShiftAudits",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAudits_Shift",
                table: "ShiftAudits",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_ClockIn",
                table: "Shifts",
                column: "ClockInUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_User_ClockIn",
                table: "Shifts",
                columns: new[] { "UserId", "ClockInUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Shifts_OneOpenPerUser",
                table: "Shifts",
                column: "UserId",
                unique: true,
                filter: "[ClockOutUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Breaks");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "ShiftAudits");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
