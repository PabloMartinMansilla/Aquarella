using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aquarella.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Businesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Profile_Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Profile_LogoDataUrl = table.Column<string>(type: "TEXT", maxLength: 3000000, nullable: true),
                    Profile_Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Profile_PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Profile_SecondaryColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Profile_TertiaryColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Profile_Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Email = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Website = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Instagram = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Facebook = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_TikTok = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_X = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_LinkedIn = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_YouTube = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Industry = table.Column<string>(type: "TEXT", nullable: true),
                    Profile_Hours = table.Column<string>(type: "TEXT", nullable: true),
                    LegacyImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Businesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CalendarEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarEntries_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DesiredProfitPercent = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    SalePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ManualSalePrice = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Product_Quantity", "Quantity >= 0");
                    table.ForeignKey(
                        name: "FK_Products_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_UserId",
                table: "Businesses",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntries_BusinessId_Date",
                table: "CalendarEntries",
                columns: new[] { "BusinessId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_BusinessId",
                table: "Products",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEntries");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Businesses");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
