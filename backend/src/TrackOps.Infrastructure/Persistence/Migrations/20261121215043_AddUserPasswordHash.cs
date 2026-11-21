using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261121215043_AddUserPasswordHash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "Users",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PasswordHash",
            table: "Users");
    }
}
