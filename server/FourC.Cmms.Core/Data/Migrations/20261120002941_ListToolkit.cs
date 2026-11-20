using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ListToolkit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    List = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    HiddenColumns = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserListSettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    List = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HiddenColumns = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserListSettings", x => new { x.UserId, x.List });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_TenantId",
                table: "SavedViews",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_UserId_List_Name",
                table: "SavedViews",
                columns: new[] { "UserId", "List", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserListSettings_TenantId",
                table: "UserListSettings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedViews");

            migrationBuilder.DropTable(
                name: "UserListSettings");
        }
    }
}
