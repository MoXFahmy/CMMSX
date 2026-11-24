using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SerialUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSerialTracked",
                table: "Parts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UnitLifeMonths",
                table: "Parts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndOfLifeOn",
                table: "Assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnitMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitMovements_Assets_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_UnitId",
                table: "PurchaseOrderLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_PartId",
                table: "Assets",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_StoreId",
                table: "Assets",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_PartId_SerialNumber",
                table: "Assets",
                columns: new[] { "TenantId", "PartId", "SerialNumber" },
                unique: true,
                filter: "[PartId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UnitMovements_TenantId",
                table: "UnitMovements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitMovements_UnitId_At",
                table: "UnitMovements",
                columns: new[] { "UnitId", "At" });

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Parts_PartId",
                table: "Assets",
                column: "PartId",
                principalTable: "Parts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Stores_StoreId",
                table: "Assets",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_Assets_UnitId",
                table: "PurchaseOrderLines",
                column: "UnitId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Parts_PartId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Stores_StoreId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_Assets_UnitId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "UnitMovements");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_UnitId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_Assets_PartId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_StoreId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TenantId_PartId_SerialNumber",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "IsSerialTracked",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "UnitLifeMonths",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "EndOfLifeOn",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "PartId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Assets");
        }
    }
}
