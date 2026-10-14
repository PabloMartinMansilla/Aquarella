using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Aquarella.Data.Migrations;
public partial class AccountEmailDelivery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing tokens remain usable, but do not impose a resend cooldown on pre-migration accounts.
        migrationBuilder.AddColumn<DateTime>("CreatedAt", "AccountTokens", type: "TEXT", nullable: false, defaultValue: DateTime.MinValue);
        migrationBuilder.DropIndex("IX_AccountTokens_UserId", "AccountTokens");
        migrationBuilder.CreateIndex("IX_AccountTokens_UserId_Purpose_CreatedAt", "AccountTokens", new[] { "UserId", "Purpose", "CreatedAt" });
        migrationBuilder.CreateTable(name: "AccountEmailNotifications", columns: table => new {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            UserId = table.Column<Guid>(type: "TEXT", nullable: false),
            Purpose = table.Column<string>(type: "TEXT", nullable: false),
            TokenId = table.Column<Guid>(type: "TEXT", nullable: true),
            PublicOrigin = table.Column<string>(type: "TEXT", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
            LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
            LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
            SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
            CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
            Attempts = table.Column<int>(type: "INTEGER", nullable: false)
        }, constraints: table => {
            table.PrimaryKey("PK_AccountEmailNotifications", x => x.Id);
            table.ForeignKey("FK_AccountEmailNotifications_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_AccountEmailNotifications_UserId", "AccountEmailNotifications", "UserId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AccountEmailNotifications");
        migrationBuilder.DropIndex("IX_AccountTokens_UserId_Purpose_CreatedAt", "AccountTokens");
        migrationBuilder.CreateIndex("IX_AccountTokens_UserId", "AccountTokens", "UserId");
        migrationBuilder.DropColumn("CreatedAt", "AccountTokens");
    }
}
