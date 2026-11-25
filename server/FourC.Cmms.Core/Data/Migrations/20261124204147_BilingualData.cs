using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FourC.Cmms.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class BilingualData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "WorkOrderTasks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyNotes",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyNotesAr",
                table: "WorkOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleAr",
                table: "WorkOrders",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Symptoms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "RequestTypes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Remedies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "PmTaskSteps",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "PmTasks",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyNotesAr",
                table: "PmTasks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleAr",
                table: "PmTasks",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "PmSchedules",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Parts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Locations",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotesToTechnicianAr",
                table: "Locations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "FailureCodes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "CancelReasons",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Assets",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotesToTechnicianAr",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "AssetCategories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "WorkOrderTasks");

            migrationBuilder.DropColumn(
                name: "SafetyNotes",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SafetyNotesAr",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "TitleAr",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Symptoms");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "RequestTypes");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Remedies");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "PmTaskSteps");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "SafetyNotesAr",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "TitleAr",
                table: "PmTasks");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "PmSchedules");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "NotesToTechnicianAr",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "FailureCodes");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "CancelReasons");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "NotesToTechnicianAr",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "AssetCategories");
        }
    }
}
