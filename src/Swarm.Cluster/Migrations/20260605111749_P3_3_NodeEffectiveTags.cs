using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P3_3_NodeEffectiveTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EffectiveTagsJson",
                table: "Nodes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_EffectiveTagsJson",
                table: "Nodes",
                column: "EffectiveTagsJson")
                .Annotation("Npgsql:IndexMethod", "gin");

            // Backfill the denormalized projection for existing rows:
            // effective = overlay || static  (|| is right-biased, so static
            // wins on key conflict — D6). Overlay rows are aggregated into a
            // jsonb object per Node; Nodes with neither layer get NULL (an
            // untagged Node can never satisfy a non-empty selector). StaticTags
            // is stored as text, so cast it to jsonb.
            migrationBuilder.Sql("""
                UPDATE "Nodes" n
                SET "EffectiveTagsJson" = NULLIF(
                    COALESCE(ov.tags, '{}'::jsonb)
                    || COALESCE(NULLIF(n."StaticTagsJson", '')::jsonb, '{}'::jsonb),
                    '{}'::jsonb)
                FROM (
                    SELECT n2."Id" AS node_id,
                           (SELECT jsonb_object_agg(t."Key", t."Value")
                            FROM "NodeOverlayTags" t
                            WHERE t."NodeId" = n2."Id") AS tags
                    FROM "Nodes" n2
                ) ov
                WHERE ov.node_id = n."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nodes_EffectiveTagsJson",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "EffectiveTagsJson",
                table: "Nodes");
        }
    }
}
