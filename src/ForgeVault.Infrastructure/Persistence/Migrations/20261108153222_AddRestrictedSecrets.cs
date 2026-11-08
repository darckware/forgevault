using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictedSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "restricted",
                table: "secrets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "restricted",
                table: "secrets");
        }
    }
}
