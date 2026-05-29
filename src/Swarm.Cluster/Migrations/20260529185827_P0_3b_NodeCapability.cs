using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P0_3b_NodeCapability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    JsonSchema = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    RequiredEnvKeysJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    RequiredParamsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeCapabilities_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeCapabilities_NodeId_TaskType",
                table: "NodeCapabilities",
                columns: new[] { "NodeId", "TaskType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeCapabilities_TaskType",
                table: "NodeCapabilities",
                column: "TaskType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeCapabilities");
        }
    }
}
