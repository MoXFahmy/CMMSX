using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequestsLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessWindows",
                table: "WorkRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AwaitingFeedbackSince",
                table: "WorkRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraRecipientIds",
                table: "WorkRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackTokenExpiresAt",
                table: "WorkRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackTokenHash",
                table: "WorkRequests",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyWhenStopped",
                table: "WorkRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestTypeId",
                table: "WorkRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessWindows",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<Guid>(
                name: "CancelReasonId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyWhenStopped",
                table: "WorkOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverdueNotifiedFor",
                table: "WorkOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyWhenStopped",
                table: "PmSchedules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyWhenStopped",
                table: "Assets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CancelReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_CancelReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ViaLink = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestFeedback_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestFeedback_WorkRequests_WorkRequestId",
                        column: x => x.WorkRequestId,
                        principalTable: "WorkRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DefaultTradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultPriority = table.Column<int>(type: "int", nullable: true),
                    RecipientIds = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    AutoApprove = table.Column<bool>(type: "bit", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_RequestTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestTypes_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestTypes_Trades_DefaultTradeId",
                        column: x => x.DefaultTradeId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkRequestComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ByRequester = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRequestComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkRequestComments_WorkRequests_WorkRequestId",
                        column: x => x.WorkRequestId,
                        principalTable: "WorkRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_FeedbackTokenHash",
                table: "WorkRequests",
                column: "FeedbackTokenHash",
                filter: "[FeedbackTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequests_RequestTypeId",
                table: "WorkRequests",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CancelReasonId",
                table: "WorkOrders",
                column: "CancelReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_CancelReasons_TenantId_Code",
                table: "CancelReasons",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CancelReasons_TenantId_LegacyRef",
                table: "CancelReasons",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFeedback_TenantId_AnsweredAt",
                table: "RequestFeedback",
                columns: new[] { "TenantId", "AnsweredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestFeedback_TenantId_WorkRequestId",
                table: "RequestFeedback",
                columns: new[] { "TenantId", "WorkRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestFeedback_WorkOrderId",
                table: "RequestFeedback",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFeedback_WorkRequestId",
                table: "RequestFeedback",
                column: "WorkRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_DefaultTradeId",
                table: "RequestTypes",
                column: "DefaultTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_TeamId",
                table: "RequestTypes",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_TenantId_Code",
                table: "RequestTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_TenantId_LegacyRef",
                table: "RequestTypes",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequestComments_TenantId_WorkRequestId",
                table: "WorkRequestComments",
                columns: new[] { "TenantId", "WorkRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRequestComments_WorkRequestId",
                table: "WorkRequestComments",
                column: "WorkRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_CancelReasons_CancelReasonId",
                table: "WorkOrders",
                column: "CancelReasonId",
                principalTable: "CancelReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkRequests_RequestTypes_RequestTypeId",
                table: "WorkRequests",
                column: "RequestTypeId",
                principalTable: "RequestTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Every existing organization gets the default cancel reasons (new ones get them when they are created).
            migrationBuilder.Sql("""
                INSERT INTO CancelReasons (Id, TenantId, Code, Name, IsActive, CreatedAt, IsDeleted)
                SELECT NEWID(), t.Id, r.Code, r.Name, 1, SYSDATETIMEOFFSET(), 0
                FROM Tenants t CROSS JOIN (VALUES ('DUP', N'Duplicate'), ('NOTNEEDED', N'No longer needed'), ('ERROR', N'Raised in error'), ('OTHER', N'Other')) r(Code, Name)
                WHERE NOT EXISTS (SELECT 1 FROM CancelReasons c WHERE c.TenantId = t.Id AND c.Code = r.Code AND c.IsDeleted = 0);
                """);
            // Work already past its due time counts as notified, so the first overdue run sends nothing old.
            migrationBuilder.Sql("""
                UPDATE WorkOrders SET OverdueNotifiedFor = DueAt
                WHERE Status IN (1, 2, 3) AND DueAt IS NOT NULL AND DueAt < SYSDATETIMEOFFSET();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_CancelReasons_CancelReasonId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkRequests_RequestTypes_RequestTypeId",
                table: "WorkRequests");

            migrationBuilder.DropTable(
                name: "CancelReasons");

            migrationBuilder.DropTable(
                name: "RequestFeedback");

            migrationBuilder.DropTable(
                name: "RequestTypes");

            migrationBuilder.DropTable(
                name: "WorkRequestComments");

            migrationBuilder.DropIndex(
                name: "IX_WorkRequests_FeedbackTokenHash",
                table: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkRequests_RequestTypeId",
                table: "WorkRequests");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_CancelReasonId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "AccessWindows",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "AwaitingFeedbackSince",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "ExtraRecipientIds",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "FeedbackTokenExpiresAt",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "FeedbackTokenHash",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "OnlyWhenStopped",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RequestTypeId",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "AccessWindows",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CancelReasonId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OnlyWhenStopped",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OverdueNotifiedFor",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OnlyWhenStopped",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "OnlyWhenStopped",
                table: "Assets");
        }
    }
}
