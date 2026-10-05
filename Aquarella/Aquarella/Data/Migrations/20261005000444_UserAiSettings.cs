using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aquarella.Data.Migrations;

public partial class UserAiSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "AiSettingsJson", table: "Users", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "AiSettingsJson", table: "Users");
}
