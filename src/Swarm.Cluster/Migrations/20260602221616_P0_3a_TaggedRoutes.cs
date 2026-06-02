using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swarm.Cluster.Migrations
{
    /// <inheritdoc />
    public partial class P0_3a_TaggedRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaggedRoutes",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SelectorJson = table.Column<string>(type: "jsonb", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaggedRoutes", x => x.Hash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaggedRoutes");
        }
    }
}
