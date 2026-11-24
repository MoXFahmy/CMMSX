using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChangeId",
                table: "AuditEntries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "AuditEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentType",
                table: "AuditEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AuditEntries",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Web"); // changes from before 0.25.0 were made on the web

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_ChangeId",
                table: "AuditEntries",
                columns: new[] { "TenantId", "ChangeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_ParentType_ParentId",
                table: "AuditEntries",
                columns: new[] { "TenantId", "ParentType", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_TenantId_Timestamp",
                table: "AuditEntries",
                columns: new[] { "TenantId", "Timestamp" });

            // Changes from before 0.25.0: each one its own change number.
            migrationBuilder.Sql("UPDATE [AuditEntries] SET [ChangeId] = [Id] WHERE [ChangeId] = '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_ChangeId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_ParentType_ParentId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_TenantId_Timestamp",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ChangeId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "ParentType",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AuditEntries");
        }
    }
}
