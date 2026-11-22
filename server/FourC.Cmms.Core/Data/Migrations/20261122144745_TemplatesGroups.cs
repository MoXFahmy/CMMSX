using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class TemplatesGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_TenantId_PmScheduleId_PmDueDate",
                table: "WorkOrders");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientIds",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedStopHours",
                table: "PmTasks",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PmTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientIds",
                table: "PmTasks",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "PmTasks",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkType",
                table: "PmTasks",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CycleStart",
                table: "PmSchedules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupPlan",
                table: "PmSchedules",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MultiAssetMode",
                table: "PmSchedules",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "NoStackingOverride",
                table: "PmSchedules",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NonWorkingDaysOverride",
                table: "PmSchedules",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientIds",
                table: "PmSchedules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "SkipStatusesOverride",
                table: "PmSchedules",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpreadIndex",
                table: "PmSchedules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SpreadParts",
                table: "PmSchedules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PmTaskId",
                table: "PmScheduleAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceMeterId",
                table: "Meters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssetCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PmTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_AssetChecklists", x => x.Id);
                    table.CheckConstraint("CK_AssetChecklists_Target", "([AssetId] IS NULL AND [AssetCategoryId] IS NOT NULL) OR ([AssetId] IS NOT NULL AND [AssetCategoryId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_AssetChecklists_AssetCategories_AssetCategoryId",
                        column: x => x.AssetCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetChecklists_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetChecklists_PmTasks_PmTaskId",
                        column: x => x.PmTaskId,
                        principalTable: "PmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PmScheduleMeters",
                columns: table => new
                {
                    PmScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Interval = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NextDueReading = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmScheduleMeters", x => new { x.PmScheduleId, x.MeterId });
                    table.ForeignKey(
                        name: "FK_PmScheduleMeters_Meters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "Meters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PmScheduleMeters_PmSchedules_PmScheduleId",
                        column: x => x.PmScheduleId,
                        principalTable: "PmSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Frequency = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Dates = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
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
                    table.PrimaryKey("PK_ScheduleCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PmScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PmTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PmDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_WorkOrderGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderGroups_PmSchedules_PmScheduleId",
                        column: x => x.PmScheduleId,
                        principalTable: "PmSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderGroups_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PmScheduleCalendars",
                columns: table => new
                {
                    PmScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleCalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmScheduleCalendars", x => new { x.PmScheduleId, x.ScheduleCalendarId });
                    table.ForeignKey(
                        name: "FK_PmScheduleCalendars_PmSchedules_PmScheduleId",
                        column: x => x.PmScheduleId,
                        principalTable: "PmSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PmScheduleCalendars_ScheduleCalendars_ScheduleCalendarId",
                        column: x => x.ScheduleCalendarId,
                        principalTable: "ScheduleCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderGroupComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderGroupComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderGroupComments_WorkOrderGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "WorkOrderGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_GroupId",
                table: "WorkOrders",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_GroupId",
                table: "WorkOrders",
                columns: new[] { "TenantId", "GroupId" },
                filter: "[GroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_PmScheduleId_PmDueDate",
                table: "WorkOrders",
                columns: new[] { "TenantId", "PmScheduleId", "PmDueDate" },
                unique: true,
                filter: "[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [GroupId] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PmScheduleAssets_PmTaskId",
                table: "PmScheduleAssets",
                column: "PmTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Meters_SourceMeterId",
                table: "Meters",
                column: "SourceMeterId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_AssetCategoryId",
                table: "AssetChecklists",
                column: "AssetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_AssetId",
                table: "AssetChecklists",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_PmTaskId",
                table: "AssetChecklists",
                column: "PmTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_TenantId_AssetCategoryId_PmTaskId",
                table: "AssetChecklists",
                columns: new[] { "TenantId", "AssetCategoryId", "PmTaskId" },
                unique: true,
                filter: "[AssetCategoryId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_TenantId_AssetId_PmTaskId",
                table: "AssetChecklists",
                columns: new[] { "TenantId", "AssetId", "PmTaskId" },
                unique: true,
                filter: "[AssetId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssetChecklists_TenantId_LegacyRef",
                table: "AssetChecklists",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PmScheduleCalendars_ScheduleCalendarId",
                table: "PmScheduleCalendars",
                column: "ScheduleCalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_PmScheduleMeters_MeterId",
                table: "PmScheduleMeters",
                column: "MeterId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleCalendars_TenantId_Code",
                table: "ScheduleCalendars",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleCalendars_TenantId_LegacyRef",
                table: "ScheduleCalendars",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroupComments_GroupId",
                table: "WorkOrderGroupComments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroupComments_TenantId_GroupId",
                table: "WorkOrderGroupComments",
                columns: new[] { "TenantId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroups_PmScheduleId",
                table: "WorkOrderGroups",
                column: "PmScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroups_SiteId",
                table: "WorkOrderGroups",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroups_TenantId_LegacyRef",
                table: "WorkOrderGroups",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroups_TenantId_Number",
                table: "WorkOrderGroups",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderGroups_TenantId_PmScheduleId_PmDueDate",
                table: "WorkOrderGroups",
                columns: new[] { "TenantId", "PmScheduleId", "PmDueDate" },
                unique: true,
                filter: "[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Meters_Meters_SourceMeterId",
                table: "Meters",
                column: "SourceMeterId",
                principalTable: "Meters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PmScheduleAssets_PmTasks_PmTaskId",
                table: "PmScheduleAssets",
                column: "PmTaskId",
                principalTable: "PmTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_WorkOrderGroups_GroupId",
                table: "WorkOrders",
                column: "GroupId",
                principalTable: "WorkOrderGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Meters_Meters_SourceMeterId",
                table: "Meters");

            migrationBuilder.DropForeignKey(
                name: "FK_PmScheduleAssets_PmTasks_PmTaskId",
                table: "PmScheduleAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_WorkOrderGroups_GroupId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "AssetChecklists");

            migrationBuilder.DropTable(
                name: "PmScheduleCalendars");

            migrationBuilder.DropTable(
                name: "PmScheduleMeters");

            migrationBuilder.DropTable(
                name: "WorkOrderGroupComments");

            migrationBuilder.DropTable(
                name: "ScheduleCalendars");

            migrationBuilder.DropTable(
                name: "WorkOrderGroups");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_GroupId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_TenantId_GroupId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_TenantId_PmScheduleId_PmDueDate",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_PmScheduleAssets_PmTaskId",
                table: "PmScheduleAssets");

            migrationBuilder.DropIndex(
                name: "IX_Meters_SourceMeterId",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RecipientIds",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ExpectedStopHours",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "RecipientIds",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "CycleStart",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "GroupPlan",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "MultiAssetMode",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "NoStackingOverride",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "NonWorkingDaysOverride",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "RecipientIds",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "SkipStatusesOverride",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "SpreadIndex",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "SpreadParts",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "PmTaskId",
                table: "PmScheduleAssets");

            migrationBuilder.DropColumn(
                name: "SourceMeterId",
                table: "Meters");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_PmScheduleId_PmDueDate",
                table: "WorkOrders",
                columns: new[] { "TenantId", "PmScheduleId", "PmDueDate" },
                unique: true,
                filter: "[PmScheduleId] IS NOT NULL AND [PmDueDate] IS NOT NULL AND [IsDeleted] = 0");
        }
    }
}
