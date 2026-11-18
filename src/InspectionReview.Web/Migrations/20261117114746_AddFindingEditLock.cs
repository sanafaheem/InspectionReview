using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InspectionReview.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFindingEditLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "Findings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedById",
                table: "Findings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Findings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Give existing findings a real version instead of the empty Guid.
            migrationBuilder.Sql("UPDATE Findings SET Version = NEWID()");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_LockedById",
                table: "Findings",
                column: "LockedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_AspNetUsers_LockedById",
                table: "Findings",
                column: "LockedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Findings_AspNetUsers_LockedById",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_Findings_LockedById",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "LockedById",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Findings");
        }
    }
}
