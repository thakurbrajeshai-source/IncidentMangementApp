using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentManagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowIncidentAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowIncidentAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IncidentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttachedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    VisibleInComments = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttachedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowIncidentAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowIncidentAssignments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowIncidentAssignments_Users_AttachedById",
                        column: x => x.AttachedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowIncidentAssignments_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowIncidentAssignments_AttachedById",
                table: "WorkflowIncidentAssignments",
                column: "AttachedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowIncidentAssignments_IncidentId_WorkflowId",
                table: "WorkflowIncidentAssignments",
                columns: new[] { "IncidentId", "WorkflowId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowIncidentAssignments_WorkflowId",
                table: "WorkflowIncidentAssignments",
                column: "WorkflowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowIncidentAssignments");
        }
    }
}
