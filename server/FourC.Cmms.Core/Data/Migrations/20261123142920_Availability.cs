using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Availability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PlannedOn",
                table: "WorkOrderContractors",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AvailabilityTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_AvailabilityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilityEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_AvailabilityEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilityEntries_AvailabilityTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "AvailabilityTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AvailabilityEntries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AvailabilityEntries_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderContractors_TenantId_PlannedOn",
                table: "WorkOrderContractors",
                columns: new[] { "TenantId", "PlannedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityEntries_EmployeeId",
                table: "AvailabilityEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityEntries_TeamId",
                table: "AvailabilityEntries",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityEntries_TenantId_From_To",
                table: "AvailabilityEntries",
                columns: new[] { "TenantId", "From", "To" });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityEntries_TenantId_LegacyRef",
                table: "AvailabilityEntries",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityEntries_TypeId",
                table: "AvailabilityEntries",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityTypes_TenantId_Code",
                table: "AvailabilityTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityTypes_TenantId_LegacyRef",
                table: "AvailabilityTypes",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            // Every existing organization gets the standard availability types (0.19.0), as new ones do.
            migrationBuilder.Sql("""
                INSERT INTO AvailabilityTypes (Id, TenantId, Code, Name, Color, Kind, IsActive, CreatedAt, IsDeleted)
                SELECT NEWID(), t.Id, a.Code, a.Name, a.Color, a.Kind, 1, SYSDATETIMEOFFSET(), 0
                FROM Tenants t CROSS JOIN (VALUES ('HOL', N'Holiday', '#40c057', 'Away'), ('SICK', N'Sick leave', '#fa5252', 'Away'),
                    ('TRAIN', N'Training', '#7950f2', 'Away'), ('MEET', N'Meeting', '#fab005', 'Away'), ('EXTRA', N'Extra shift', '#228be6', 'Extra')) a(Code, Name, Color, Kind)
                WHERE NOT EXISTS (SELECT 1 FROM AvailabilityTypes x WHERE x.TenantId = t.Id AND x.Code = a.Code AND x.IsDeleted = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityEntries");

            migrationBuilder.DropTable(
                name: "AvailabilityTypes");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderContractors_TenantId_PlannedOn",
                table: "WorkOrderContractors");

            migrationBuilder.DropColumn(
                name: "PlannedOn",
                table: "WorkOrderContractors");
        }
    }
}
