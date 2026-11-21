using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261121233040_OneBreakPerShift : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Breaks_Shift",
            table: "Breaks");

        migrationBuilder.DropIndex(
            name: "UX_Breaks_OneOpenPerShift",
            table: "Breaks");

        // A shift now has one break at most. A database from before this change may hold several
        // per shift; each shift keeps its first break, or the unique index cannot be created.
        migrationBuilder.Sql(
            """
            WITH ranked AS (
                SELECT Id, ROW_NUMBER() OVER (PARTITION BY ShiftId ORDER BY StartUtc, Id) AS position
                FROM Breaks)
            DELETE FROM ranked WHERE position > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "UX_Breaks_OnePerShift",
            table: "Breaks",
            column: "ShiftId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Breaks_OnePerShift",
            table: "Breaks");

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
    }
}
