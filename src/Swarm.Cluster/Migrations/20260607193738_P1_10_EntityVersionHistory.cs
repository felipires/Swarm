using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P1_10_EntityVersionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TaskDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TaskDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Pipelines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Pipelines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EntityVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntityVersions_EntityType_EntityId",
                table: "EntityVersions",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityVersions_EntityType_EntityId_Version",
                table: "EntityVersions",
                columns: new[] { "EntityType", "EntityId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityVersions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TaskDefinitions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TaskDefinitions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Pipelines");
        }
    }
}
