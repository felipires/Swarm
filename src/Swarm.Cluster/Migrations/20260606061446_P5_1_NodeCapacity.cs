using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P5_1_NodeCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CpuCores",
                table: "Nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalMemoryBytes",
                table: "Nodes",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuCores",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "TotalMemoryBytes",
                table: "Nodes");
        }
    }
}
