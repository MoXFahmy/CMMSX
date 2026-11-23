using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractsContractors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManufacturer",
                table: "Suppliers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSupplier",
                table: "Suppliers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "PmSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractorCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_ContractorCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractTypes",
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
                    table.PrimaryKey("PK_ContractTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierAddresses_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierContacts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCategoryRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCategoryRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCategoryRates_ContractorCategories_ContractorCategoryId",
                        column: x => x.ContractorCategoryId,
                        principalTable: "ContractorCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SupplierCategoryRates_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderContractors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PersonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PlannedHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    HoursDone = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    DoneOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderContractors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderContractors_ContractorCategories_ContractorCategoryId",
                        column: x => x.ContractorCategoryId,
                        principalTable: "ContractorCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkOrderContractors_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkOrderContractors_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManagerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpectedWorkOrders = table.Column<int>(type: "int", nullable: true),
                    ExpectedPer = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    RenewalMonths = table.Column<int>(type: "int", nullable: true),
                    NoticeDays = table.Column<int>(type: "int", nullable: false),
                    AlertDays = table.Column<int>(type: "int", nullable: false),
                    TerminatedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TerminationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AlertSentFor = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_ContractTypes_ContractTypeId",
                        column: x => x.ContractTypeId,
                        principalTable: "ContractTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contracts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contracts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contracts_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractAmendments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    NewValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractAmendments_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearlyCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractAssets_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsRenewal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractPeriods_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ContractId",
                table: "WorkOrders",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PmSchedules_ContractId",
                table: "PmSchedules",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId",
                table: "Employees",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAmendments_ContractId",
                table: "ContractAmendments",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAssets_AssetId",
                table: "ContractAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAssets_ContractId_AssetId",
                table: "ContractAssets",
                columns: new[] { "ContractId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractAssets_TenantId_AssetId",
                table: "ContractAssets",
                columns: new[] { "TenantId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorCategories_TenantId_Code",
                table: "ContractorCategories",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorCategories_TenantId_LegacyRef",
                table: "ContractorCategories",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContractPeriods_ContractId_Number",
                table: "ContractPeriods",
                columns: new[] { "ContractId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ContractTypeId",
                table: "Contracts",
                column: "ContractTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_SiteId",
                table: "Contracts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_SupplierId",
                table: "Contracts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_EndDate",
                table: "Contracts",
                columns: new[] { "TenantId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_LegacyRef",
                table: "Contracts",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TenantId_Number",
                table: "Contracts",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_TradeId",
                table: "Contracts",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypes_TenantId_Code",
                table: "ContractTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypes_TenantId_LegacyRef",
                table: "ContractTypes",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAddresses_SupplierId",
                table: "SupplierAddresses",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCategoryRates_ContractorCategoryId",
                table: "SupplierCategoryRates",
                column: "ContractorCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCategoryRates_SupplierId_ContractorCategoryId",
                table: "SupplierCategoryRates",
                columns: new[] { "SupplierId", "ContractorCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierContacts_SupplierId",
                table: "SupplierContacts",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderContractors_ContractorCategoryId",
                table: "WorkOrderContractors",
                column: "ContractorCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderContractors_SupplierId",
                table: "WorkOrderContractors",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderContractors_TenantId_WorkOrderId",
                table: "WorkOrderContractors",
                columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderContractors_WorkOrderId",
                table: "WorkOrderContractors",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Suppliers_CompanyId",
                table: "Employees",
                column: "CompanyId",
                principalTable: "Suppliers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PmSchedules_Contracts_ContractId",
                table: "PmSchedules",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Contracts_ContractId",
                table: "WorkOrders",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id");

            // Existing organizations get the contract types new ones start with (0.17.0)...
            migrationBuilder.Sql("""
                INSERT INTO ContractTypes (Id, TenantId, Code, Name, IsActive, CreatedAt, IsDeleted)
                SELECT NEWID(), t.Id, o.Code, o.Name, 1, SYSDATETIMEOFFSET(), 0
                FROM Tenants t CROSS JOIN (VALUES ('MAINT', N'Maintenance'), ('INSP', N'Inspection'), ('LEASE', N'Lease'), ('WARR', N'Warranty')) o(Code, Name)
                WHERE NOT EXISTS (SELECT 1 FROM ContractTypes x WHERE x.TenantId = t.Id AND x.Code = o.Code AND x.IsDeleted = 0);
                """);
            // ...and the built-in roles get the contract permissions, as new organizations do.
            migrationBuilder.Sql("""
                UPDATE Roles SET Permissions = JSON_MODIFY(Permissions, 'append $', 'contracts.view')
                WHERE Name IN (N'Administrator', N'Maintenance Manager', N'Technician', N'Viewer')
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(Permissions) WHERE [value] = 'contracts.view');
                UPDATE Roles SET Permissions = JSON_MODIFY(Permissions, 'append $', 'contracts.manage')
                WHERE Name IN (N'Administrator', N'Maintenance Manager')
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON(Permissions) WHERE [value] = 'contracts.manage');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Suppliers_CompanyId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_PmSchedules_Contracts_ContractId",
                table: "PmSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Contracts_ContractId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "ContractAmendments");

            migrationBuilder.DropTable(
                name: "ContractAssets");

            migrationBuilder.DropTable(
                name: "ContractPeriods");

            migrationBuilder.DropTable(
                name: "SupplierAddresses");

            migrationBuilder.DropTable(
                name: "SupplierCategoryRates");

            migrationBuilder.DropTable(
                name: "SupplierContacts");

            migrationBuilder.DropTable(
                name: "WorkOrderContractors");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "ContractorCategories");

            migrationBuilder.DropTable(
                name: "ContractTypes");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ContractId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_PmSchedules_ContractId",
                table: "PmSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CompanyId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "IsManufacturer",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "IsSupplier",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Employees");
        }
    }
}
