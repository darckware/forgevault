using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_server_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transport = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    command = table.Column<string>(type: "text", nullable: true),
                    args_json = table.Column<string>(type: "text", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true),
                    timeout = table.Column<int>(type: "integer", nullable: true),
                    connect_timeout = table.Column<int>(type: "integer", nullable: true),
                    static_env_json = table.Column<string>(type: "text", nullable: true),
                    secret_param_names_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_server_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_server_definitions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mcp_server_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mcp_server_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    param_values_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_server_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_server_assignments_mcp_server_definitions_mcp_server_de",
                        column: x => x.mcp_server_definition_id,
                        principalTable: "mcp_server_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_server_assignments_identity_id_mcp_server_definition_id",
                table: "mcp_server_assignments",
                columns: new[] { "identity_id", "mcp_server_definition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mcp_server_assignments_mcp_server_definition_id",
                table: "mcp_server_assignments",
                column: "mcp_server_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_server_definitions_organization_id_name",
                table: "mcp_server_definitions",
                columns: new[] { "organization_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_server_assignments");

            migrationBuilder.DropTable(
                name: "mcp_server_definitions");
        }
    }
}
