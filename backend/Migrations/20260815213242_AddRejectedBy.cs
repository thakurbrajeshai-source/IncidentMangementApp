using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentManagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "Incidents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedById",
                table: "Incidents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_RejectedById",
                table: "Incidents",
                column: "RejectedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_Users_RejectedById",
                table: "Incidents",
                column: "RejectedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_Users_RejectedById",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_RejectedById",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "RejectedById",
                table: "Incidents");
        }
    }
}
