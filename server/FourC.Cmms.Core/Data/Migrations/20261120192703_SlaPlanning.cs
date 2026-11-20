using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SlaPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                table: "WorkRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DueFromTarget",
                table: "WorkRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RespondBy",
                table: "WorkRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DueAt",
                table: "WorkOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DueFromTarget",
                table: "WorkOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RespondedAt",
                table: "WorkOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartBy",
                table: "WorkOrders",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "DueFromTarget",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "RespondBy",
                table: "WorkRequests");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "DueFromTarget",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "StartBy",
                table: "WorkOrders");
        }
    }
}
