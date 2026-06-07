using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P1_8_StepOutputMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutputMappingsJson",
                table: "PipelineSteps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "PipelineStepInstances",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputMappingsJson",
                table: "PipelineSteps");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "PipelineStepInstances");
        }
    }
}
