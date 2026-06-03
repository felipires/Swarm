using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P1_4_P1_2_SnapshotAndRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigJsonSnapshot",
                table: "TaskInstances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RetryAfter",
                table: "TaskInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "TaskInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TaskDefinitionVersion",
                table: "TaskInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TaskType",
                table: "TaskInstances",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxRetries",
                table: "TaskDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryBackoff",
                table: "TaskDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryDelaySeconds",
                table: "TaskDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "TaskDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigJsonSnapshot",
                table: "TaskInstances");

            migrationBuilder.DropColumn(
                name: "RetryAfter",
                table: "TaskInstances");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "TaskInstances");

            migrationBuilder.DropColumn(
                name: "TaskDefinitionVersion",
                table: "TaskInstances");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "TaskInstances");

            migrationBuilder.DropColumn(
                name: "MaxRetries",
                table: "TaskDefinitions");

            migrationBuilder.DropColumn(
                name: "RetryBackoff",
                table: "TaskDefinitions");

            migrationBuilder.DropColumn(
                name: "RetryDelaySeconds",
                table: "TaskDefinitions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TaskDefinitions");
        }
    }
}
