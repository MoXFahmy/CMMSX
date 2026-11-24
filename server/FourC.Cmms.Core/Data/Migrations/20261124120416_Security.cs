using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Security : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAt",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PasswordHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordHistories_TenantId",
                table: "PasswordHistories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordHistories_UserId_SetAt",
                table: "PasswordHistories",
                columns: new[] { "UserId", "SetAt" });

            // Everyone's expiry clock starts on release day: no password expires the day 0.25.0 arrives.
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [PasswordChangedAt] = SYSDATETIMEOFFSET() WHERE [PasswordChangedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordHistories");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "AspNetUsers");
        }
    }
}
