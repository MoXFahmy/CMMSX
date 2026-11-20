using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlannedParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PlannedHours",
                table: "WorkOrderAssignees",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PmTaskParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PmTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmTaskParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PmTaskParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PmTaskParts_PmTasks_PmTaskId",
                        column: x => x.PmTaskId,
                        principalTable: "PmTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderParts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PmTaskParts_PartId",
                table: "PmTaskParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PmTaskParts_PmTaskId_PartId",
                table: "PmTaskParts",
                columns: new[] { "PmTaskId", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderParts_PartId",
                table: "WorkOrderParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderParts_StoreId",
                table: "WorkOrderParts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderParts_TenantId_PartId_StoreId",
                table: "WorkOrderParts",
                columns: new[] { "TenantId", "PartId", "StoreId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderParts_WorkOrderId_PartId",
                table: "WorkOrderParts",
                columns: new[] { "WorkOrderId", "PartId" },
                unique: true);

            // Stock reserved for open work per part and store: planned less net issued, never below 0 (Reservations.StillReserved).
            // Open = Open (1), InProgress (2), OnHold (3); issues (2) and returns (3) count against the plan.
            migrationBuilder.Sql(@"
CREATE VIEW StockReservations AS
SELECT wp.TenantId, wp.PartId, wp.StoreId,
       CAST(SUM(CASE WHEN wp.Quantity > ISNULL(i.Net, 0) THEN wp.Quantity - ISNULL(i.Net, 0) ELSE 0 END) AS decimal(18, 3)) AS Reserved
FROM WorkOrderParts wp
JOIN WorkOrders w ON w.Id = wp.WorkOrderId AND w.IsDeleted = 0 AND w.Status IN (1, 2, 3)
OUTER APPLY (SELECT -SUM(t.Quantity) AS Net FROM StockTransactions t
             WHERE t.WorkOrderId = wp.WorkOrderId AND t.PartId = wp.PartId AND t.Type IN (2, 3)) i
WHERE wp.StoreId IS NOT NULL
GROUP BY wp.TenantId, wp.PartId, wp.StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS StockReservations");

            migrationBuilder.DropTable(
                name: "PmTaskParts");

            migrationBuilder.DropTable(
                name: "WorkOrderParts");

            migrationBuilder.DropColumn(
                name: "PlannedHours",
                table: "WorkOrderAssignees");
        }
    }
}
