using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SmartChecklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskId",
                table: "WorkRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceWorkOrderId",
                table: "WorkRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Answer",
                table: "WorkOrderTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BeforeStart",
                table: "WorkOrderTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Decimals",
                table: "WorkOrderTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FollowUp",
                table: "WorkOrderTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpRequestId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpTemplateId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpWorkOrderId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "WorkOrderTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MeasurementPointId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MeterId",
                table: "WorkOrderTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhotoRequired",
                table: "WorkOrderTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TextValue",
                table: "WorkOrderTasks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "WorkOrderTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "WorkOrderTasks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                table: "WorkOrderTasks",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTaskId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceWorkOrderId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BeforeStart",
                table: "PmTaskSteps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Decimals",
                table: "PmTaskSteps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FollowUp",
                table: "PmTaskSteps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FollowUpTemplateId",
                table: "PmTaskSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MeasurementPointId",
                table: "PmTaskSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhotoRequired",
                table: "PmTaskSteps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "PmTaskSteps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "PmTaskSteps",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkOrderTaskId",
                table: "Attachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MeasurementPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Decimals = table.Column<int>(type: "int", nullable: false),
                    ValidMin = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ValidMax = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AlarmLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AlertLow = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Target = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AlertHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AlarmHigh = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AlarmLowTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AlertLowTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AlertHighTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AlarmHighTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_MeasurementPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementPointAssets",
                columns: table => new
                {
                    PointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementPointAssets", x => new { x.PointId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_MeasurementPointAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MeasurementPointAssets_MeasurementPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "MeasurementPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkOrderTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FollowUpWorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FollowUpRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_MeasurementReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementReadings_MeasurementPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "MeasurementPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PmTaskSteps_MeasurementPointId",
                table: "PmTaskSteps",
                column: "MeasurementPointId",
                filter: "[MeasurementPointId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_WorkOrderTaskId",
                table: "Attachments",
                column: "WorkOrderTaskId",
                filter: "[WorkOrderTaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementPointAssets_AssetId",
                table: "MeasurementPointAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementPoints_TenantId_Code",
                table: "MeasurementPoints",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementPoints_TenantId_LegacyRef",
                table: "MeasurementPoints",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReadings_PointId",
                table: "MeasurementReadings",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReadings_TenantId_LegacyRef",
                table: "MeasurementReadings",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReadings_TenantId_PointId_AssetId_ReadAt",
                table: "MeasurementReadings",
                columns: new[] { "TenantId", "PointId", "AssetId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReadings_TenantId_ReadAt_Level",
                table: "MeasurementReadings",
                columns: new[] { "TenantId", "ReadAt", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementReadings_TenantId_WorkOrderTaskId",
                table: "MeasurementReadings",
                columns: new[] { "TenantId", "WorkOrderTaskId" },
                filter: "[WorkOrderTaskId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PmTaskSteps_MeasurementPoints_MeasurementPointId",
                table: "PmTaskSteps",
                column: "MeasurementPointId",
                principalTable: "MeasurementPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Steps ticked before 0.13.0 are OK answers (the tick stays as it is).
            migrationBuilder.Sql("UPDATE [WorkOrderTasks] SET [Answer] = 1 WHERE [IsDone] = 1 AND [Answer] IS NULL AND [Type] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PmTaskSteps_MeasurementPoints_MeasurementPointId",
                table: "PmTaskSteps");

            migrationBuilder.DropTable(
                name: "MeasurementPointAssets");

            migrationBuilder.DropTable(
                name: "MeasurementReadings");

            migrationBuilder.DropTable(
                name: "MeasurementPoints");

            migrationBuilder.DropIndex(
                name: "IX_PmTaskSteps_MeasurementPointId",
                table: "PmTaskSteps");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_WorkOrderTaskId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "SourceTaskId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "SourceWorkOrderId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "Answer",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "BeforeStart",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Decimals",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "FollowUp",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "FollowUpRequestId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "FollowUpTemplateId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "FollowUpWorkOrderId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "MeasurementPointId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "MeterId",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "PhotoRequired",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "TextValue",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "SourceTaskId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceWorkOrderId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "BeforeStart",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "Decimals",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "FollowUp",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "FollowUpTemplateId",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "MeasurementPointId",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "PhotoRequired",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "WorkOrderTaskId",
                table: "Attachments");
        }
    }
}
