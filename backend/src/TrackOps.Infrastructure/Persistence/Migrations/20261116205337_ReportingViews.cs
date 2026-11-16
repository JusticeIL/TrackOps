using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Reporting views. The tables store only UTC instants; every duration is derived here,
    /// so a stored minute count can never disagree with its clock-in and clock-out.
    /// Open shifts and open breaks report NULL minutes.
    /// </summary>
    public partial class ReportingViews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE VIEW dbo.v_BreakReport AS
SELECT b.Id AS BreakId,
       b.ShiftId,
       s.UserId,
       u.Username,
       b.StartUtc,
       b.EndUtc,
       DATEDIFF_BIG(MILLISECOND, b.StartUtc, b.EndUtc) / 60000 AS BreakMinutes
FROM dbo.Breaks AS b
JOIN dbo.Shifts AS s ON s.Id = b.ShiftId
JOIN dbo.Users AS u ON u.Id = s.UserId;");

            migrationBuilder.Sql(@"
CREATE VIEW dbo.v_ShiftReport AS
SELECT s.Id AS ShiftId,
       s.UserId,
       u.Username,
       u.DisplayName,
       s.ClockInUtc,
       s.ClockOutUtc,
       s.Status,
       s.IsCorrected,
       DATEDIFF_BIG(MILLISECOND, s.ClockInUtc, s.ClockOutUtc) / 60000 AS ShiftMinutes,
       CASE WHEN s.ClockOutUtc IS NULL THEN NULL ELSE ISNULL(br.BreakMilliseconds, 0) / 60000 END AS BreakMinutes,
       (DATEDIFF_BIG(MILLISECOND, s.ClockInUtc, s.ClockOutUtc) - ISNULL(br.BreakMilliseconds, 0)) / 60000 AS WorkedMinutes
FROM dbo.Shifts AS s
JOIN dbo.Users AS u ON u.Id = s.UserId
OUTER APPLY (
    SELECT SUM(DATEDIFF_BIG(MILLISECOND, b.StartUtc, b.EndUtc)) AS BreakMilliseconds
    FROM dbo.Breaks AS b
    WHERE b.ShiftId = s.Id
) AS br;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.v_ShiftReport;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.v_BreakReport;");
        }
    }
}
