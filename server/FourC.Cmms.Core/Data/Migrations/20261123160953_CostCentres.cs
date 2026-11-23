using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CostCentres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "WorkRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "StockTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "PmSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostCentres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    LegacyRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCentres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostCentres_CostCentres_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CostCentres",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CostCentres_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCentreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    LegacyRef = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_CostCentres_CostCentreId",
                        column: x => x.CostCentreId,
                        principalTable: "CostCentres",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_CostCentreId",
                table: "WorkRequests",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CostCentreId",
                table: "WorkOrders",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransactions_CostCentreId",
                table: "StockTransactions",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_PmSchedules_CostCentreId",
                table: "PmSchedules",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CostCentreId",
                table: "Locations",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CostCentreId",
                table: "Assets",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CostCentreId",
                table: "Budgets",
                column: "CostCentreId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_TenantId_CostCentreId_Year_Month",
                table: "Budgets",
                columns: new[] { "TenantId", "CostCentreId", "Year", "Month" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_TenantId_LegacyRef",
                table: "Budgets",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_ParentId",
                table: "CostCentres",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_SiteId",
                table: "CostCentres",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_TenantId_Code",
                table: "CostCentres",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_TenantId_LegacyRef",
                table: "CostCentres",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_CostCentres_CostCentreId",
                table: "Assets",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_CostCentres_CostCentreId",
                table: "Locations",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PmSchedules_CostCentres_CostCentreId",
                table: "PmSchedules",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransactions_CostCentres_CostCentreId",
                table: "StockTransactions",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_CostCentres_CostCentreId",
                table: "WorkOrders",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkRequests_CostCentres_CostCentreId",
                table: "WorkRequests",
                column: "CostCentreId",
                principalTable: "CostCentres",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_CostCentres_CostCentreId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_CostCentres_CostCentreId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_PmSchedules_CostCentres_CostCentreId",
                table: "PmSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransactions_CostCentres_CostCentreId",
                table: "StockTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_CostCentres_CostCentreId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkRequests_CostCentres_CostCentreId",
                table: "WorkRequests");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CostCentres");

            migrationBuilder.DropIndex(
                name: "IX_WorkRequests_CostCentreId",
                table: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_CostCentreId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_StockTransactions_CostCentreId",
                table: "StockTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PmSchedules_CostCentreId",
                table: "PmSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Locations_CostCentreId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Assets_CostCentreId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "Assets");
        }
    }
}
