using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P2_ObservabilityLogSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Logs_Timestamp",
                table: "Logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "NodeId",
                table: "Logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Timestamp_Id",
                table: "Logs",
                columns: new[] { "Timestamp", "Id" });

            // GIN(jsonb_path_ops) backs the `Tags @> selector` containment used by
            // tag-faceted log search — smaller/faster than the default op class for
            // the @> queries we run.
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Logs_Tags\" ON \"Logs\" USING gin (\"Tags\" jsonb_path_ops);");

            // Trigram GIN indexes power substring (ILIKE '%q%') free-text search
            // over the message and template — EF can't express the op class.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Logs_Message_trgm\" ON \"Logs\" USING gin (\"Message\" gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX \"IX_Logs_MessageTemplate_trgm\" ON \"Logs\" USING gin (\"MessageTemplate\" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Logs_MessageTemplate_trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Logs_Message_trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Logs_Tags\";");

            migrationBuilder.DropIndex(
                name: "IX_Logs_Timestamp_Id",
                table: "Logs");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "NodeId",
                table: "Logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Logs_Timestamp",
                table: "Logs",
                column: "Timestamp");
        }
    }
}
