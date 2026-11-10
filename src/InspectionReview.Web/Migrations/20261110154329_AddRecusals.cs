using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InspectionReview.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRecusals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Recusals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FirmId = table.Column<int>(type: "int", nullable: false),
                    RecusedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recusals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recusals_AspNetUsers_InspectorId",
                        column: x => x.InspectorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recusals_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recusals_FirmId",
                table: "Recusals",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_Recusals_InspectorId_FirmId",
                table: "Recusals",
                columns: new[] { "InspectorId", "FirmId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recusals");
        }
    }
}
