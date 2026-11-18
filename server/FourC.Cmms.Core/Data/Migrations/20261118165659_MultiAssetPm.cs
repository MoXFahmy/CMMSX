using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiAssetPm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyRef",
                table: "WorkOrderTasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reading",
                table: "WorkOrderTasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "WorkOrderTasks",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PmScheduleAssets",
                columns: table => new
                {
                    PmScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmScheduleAssets", x => new { x.PmScheduleId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_PmScheduleAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PmScheduleAssets_PmSchedules_PmScheduleId",
                        column: x => x.PmScheduleId,
                        principalTable: "PmSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAssets",
                columns: table => new
                {
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAssets", x => new { x.WorkOrderId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_WorkOrderAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderAssets_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTasks_AssetId",
                table: "WorkOrderTasks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderTasks_TenantId_LegacyRef",
                table: "WorkOrderTasks",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PmScheduleAssets_AssetId",
                table: "PmScheduleAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAssets_AssetId_WorkOrderId",
                table: "WorkOrderAssets",
                columns: new[] { "AssetId", "WorkOrderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderTasks_Assets_AssetId",
                table: "WorkOrderTasks",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderTasks_Assets_AssetId",
                table: "WorkOrderTasks");

            migrationBuilder.DropTable(
                name: "PmScheduleAssets");

            migrationBuilder.DropTable(
                name: "WorkOrderAssets");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderTasks_AssetId",
                table: "WorkOrderTasks");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderTasks_TenantId_LegacyRef",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "LegacyRef",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Reading",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "WorkOrderTasks");
        }
    }
}
