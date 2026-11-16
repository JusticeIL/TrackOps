using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261116224949_DropUserCreatedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CreatedAtUtc",
            table: "Users");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAtUtc",
            table: "Users",
            type: "datetime2(3)",
            precision: 3,
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
    }
}
