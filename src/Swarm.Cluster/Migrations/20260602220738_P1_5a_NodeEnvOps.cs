using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P1_5a_NodeEnvOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeEnvOps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeEnvOps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeEnvOps_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnvOps_NodeId",
                table: "NodeEnvOps",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeEnvOps");
        }
    }
}
