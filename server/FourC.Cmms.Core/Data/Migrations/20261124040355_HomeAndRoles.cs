using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class HomeAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeOrder",
                table: "SavedViews",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PinnedToHome",
                table: "SavedViews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HiddenModules",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "HomeLayout",
                table: "Roles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoleListSettings",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    List = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HiddenColumns = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleListSettings", x => new { x.RoleId, x.List });
                    table.ForeignKey(
                        name: "FK_RoleListSettings_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_UserId_PinnedToHome",
                table: "SavedViews",
                columns: new[] { "UserId", "PinnedToHome" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleListSettings_TenantId",
                table: "RoleListSettings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleListSettings");

            migrationBuilder.DropIndex(
                name: "IX_SavedViews_UserId_PinnedToHome",
                table: "SavedViews");

            migrationBuilder.DropColumn(
                name: "HomeOrder",
                table: "SavedViews");

            migrationBuilder.DropColumn(
                name: "PinnedToHome",
                table: "SavedViews");

            migrationBuilder.DropColumn(
                name: "HiddenModules",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "HomeLayout",
                table: "Roles");
        }
    }
}
