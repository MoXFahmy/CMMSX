using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartsAndDowntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DowntimeEventId",
                table: "WorkRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DowntimeEventId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RunsHoursPerDay",
                table: "Assets",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 24m);

            migrationBuilder.AddColumn<string>(
                name: "RunsOn",
                table: "Assets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "[\"Sunday\",\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\",\"Saturday\"]");

            migrationBuilder.CreateTable(
                name: "AssetParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_AssetParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetParts_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DowntimeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EndedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_DowntimeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DowntimeEvents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_DowntimeEventId",
                table: "WorkRequests",
                column: "DowntimeEventId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_DowntimeEventId",
                table: "WorkOrders",
                column: "DowntimeEventId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetParts_AssetId",
                table: "AssetParts",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetParts_PartId",
                table: "AssetParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetParts_TenantId_AssetId_PartId",
                table: "AssetParts",
                columns: new[] { "TenantId", "AssetId", "PartId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssetParts_TenantId_LegacyRef",
                table: "AssetParts",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssetParts_TenantId_PartId",
                table: "AssetParts",
                columns: new[] { "TenantId", "PartId" });

            migrationBuilder.CreateIndex(
                name: "IX_DowntimeEvents_AssetId",
                table: "DowntimeEvents",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DowntimeEvents_OpenPerAsset",
                table: "DowntimeEvents",
                columns: new[] { "TenantId", "AssetId" },
                unique: true,
                filter: "[EndedAt] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DowntimeEvents_TenantId_AssetId_StartedAt",
                table: "DowntimeEvents",
                columns: new[] { "TenantId", "AssetId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DowntimeEvents_TenantId_LegacyRef",
                table: "DowntimeEvents",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_DowntimeEvents_DowntimeEventId",
                table: "WorkOrders",
                column: "DowntimeEventId",
                principalTable: "DowntimeEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkRequests_DowntimeEvents_DowntimeEventId",
                table: "WorkRequests",
                column: "DowntimeEventId",
                principalTable: "DowntimeEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_DowntimeEvents_DowntimeEventId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkRequests_DowntimeEvents_DowntimeEventId",
                table: "WorkRequests");

            migrationBuilder.DropTable(
                name: "AssetParts");

            migrationBuilder.DropTable(
                name: "DowntimeEvents");

            migrationBuilder.DropIndex(
                name: "IX_WorkRequests_DowntimeEventId",
                table: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_DowntimeEventId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "DowntimeEventId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "DowntimeEventId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RunsHoursPerDay",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "RunsOn",
                table: "Assets");
        }
    }
}
