using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackSize",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "SupplierPartNumber",
                table: "PurchaseOrderLines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PackName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PackSize = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PackPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    LeadTimeDays = table.Column<int>(type: "int", nullable: true),
                    MinPacks = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastPaidPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    LastPaidDiscount = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    LastPaidCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    LastPaidOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_SupplierParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierParts_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierParts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierParts_PartId",
                table: "SupplierParts",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierParts_SupplierId",
                table: "SupplierParts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierParts_TenantId_LegacyRef",
                table: "SupplierParts",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierParts_TenantId_PartId_SupplierId",
                table: "SupplierParts",
                columns: new[] { "TenantId", "PartId", "SupplierId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // Every part's preferred supplier becomes its first catalogue row (0.18.0): a pack of 1 in the part's unit, the part's lead time, the
            // company's currency (else the organization's). Last purchase / standard prices are per unit in the base currency, so they are copied
            // only when the row's currency is the base currency.
            migrationBuilder.Sql("""
                INSERT INTO SupplierParts (Id, TenantId, PartId, SupplierId, PackName, PackSize, PackPrice, Currency, DiscountPercent, LeadTimeDays, MinPacks,
                                           IsActive, CreatedAt, IsDeleted)
                SELECT NEWID(), p.TenantId, p.Id, s.Id, p.UnitOfMeasure, 1,
                       CASE WHEN x.Currency = x.BaseCurrency THEN COALESCE(p.LastPurchasePrice, p.StandardPrice) END,
                       x.Currency, 0, p.LeadTimeDays, 1, 1, SYSDATETIMEOFFSET(), 0
                FROM Parts p
                JOIN Suppliers s ON s.Id = p.PreferredSupplierId AND s.TenantId = p.TenantId
                JOIN Tenants t ON t.Id = p.TenantId
                CROSS APPLY (SELECT UPPER(COALESCE(NULLIF(LTRIM(RTRIM(s.Currency)), ''), JSON_VALUE(t.Settings, '$.currency'), 'USD')) AS Currency,
                                    UPPER(COALESCE(JSON_VALUE(t.Settings, '$.currency'), 'USD')) AS BaseCurrency) x
                WHERE p.IsDeleted = 0 AND p.PreferredSupplierId IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM SupplierParts r WHERE r.PartId = p.Id AND r.SupplierId = s.Id AND r.IsDeleted = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierParts");

            migrationBuilder.DropColumn(
                name: "PackSize",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "SupplierPartNumber",
                table: "PurchaseOrderLines");
        }
    }
}
