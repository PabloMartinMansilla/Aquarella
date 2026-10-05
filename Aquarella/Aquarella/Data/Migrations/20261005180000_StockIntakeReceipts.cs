using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Aquarella.Data.Migrations;

public partial class StockIntakeReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "StockIntakeReceipts", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
            OperationKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
            ResultsJson = table.Column<string>(type: "TEXT", nullable: false),
            CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_StockIntakeReceipts", x => x.Id);
            table.ForeignKey("FK_StockIntakeReceipts_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_StockIntakeReceipts_BusinessId_OperationKey", "StockIntakeReceipts", new[] { "BusinessId", "OperationKey" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("StockIntakeReceipts");
}
