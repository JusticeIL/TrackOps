using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261117071048_AddEmployeeRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmployeeRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1001, 1"),
                UserId = table.Column<int>(type: "int", nullable: false),
                Type = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                SickDays = table.Column<int>(type: "int", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmployeeRequests", x => x.Id);
                table.CheckConstraint("CK_EmployeeRequests_SickDays", "([Type] = 3 AND [SickDays] BETWEEN 1 AND 30) OR ([Type] <> 3 AND [SickDays] IS NULL)");
                table.CheckConstraint("CK_EmployeeRequests_Status", "[Status] IN (0, 1, 2)");
                table.CheckConstraint("CK_EmployeeRequests_Type", "[Type] BETWEEN 0 AND 6");
                table.ForeignKey(
                    name: "FK_EmployeeRequests_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmployeeRequests_User",
            table: "EmployeeRequests",
            columns: new[] { "UserId", "Id" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EmployeeRequests");
    }
}
