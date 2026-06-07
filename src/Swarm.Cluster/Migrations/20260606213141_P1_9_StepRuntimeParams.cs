using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P1_9_StepRuntimeParams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuntimeParamsJson",
                table: "PipelineSteps",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeParamsJson",
                table: "PipelineSteps");
        }
    }
}
