using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SignUpConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignUpPendingSince",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignUpTokenExpires",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignUpTokenHash",
                table: "Tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignUpPendingSince",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignUpTokenExpires",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignUpTokenHash",
                table: "Tenants");
        }
    }
}
