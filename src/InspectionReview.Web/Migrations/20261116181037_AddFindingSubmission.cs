using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InspectionReview.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFindingSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "Findings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedById",
                table: "Findings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Findings_SubmittedById",
                table: "Findings",
                column: "SubmittedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Findings_AspNetUsers_SubmittedById",
                table: "Findings",
                column: "SubmittedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Best guess for findings already Submitted: nothing edits them, so UpdatedAt is the
            // submit time, and the creator is the likeliest submitter.
            migrationBuilder.Sql(
                "UPDATE Findings SET SubmittedAt = UpdatedAt, SubmittedById = CreatedById WHERE State = 'Submitted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Findings_AspNetUsers_SubmittedById",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_Findings_SubmittedById",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "Findings");
        }
    }
}
