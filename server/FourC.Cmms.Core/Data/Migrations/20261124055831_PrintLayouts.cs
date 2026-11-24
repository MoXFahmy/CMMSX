using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrintLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Paper = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KindKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PaperSize = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ShowLogo = table.Column<bool>(type: "bit", nullable: false),
                    HeaderText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HeaderTextAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterTextAr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Terms = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TermsAr = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Blocks = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    LabelSize = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LabelFields = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
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
                    table.PrimaryKey("PK_PrintLayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintLayouts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintLayouts_SiteId",
                table: "PrintLayouts",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintLayouts_TenantId_LegacyRef",
                table: "PrintLayouts",
                columns: new[] { "TenantId", "LegacyRef" },
                filter: "[LegacyRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrintLayouts_TenantId_Paper_Name",
                table: "PrintLayouts",
                columns: new[] { "TenantId", "Paper", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintLayouts");
        }
    }
}
