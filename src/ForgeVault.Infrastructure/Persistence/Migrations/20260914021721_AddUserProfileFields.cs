using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_data_url",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true,
                filter: "username IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_data_url",
                table: "users");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "users");
        }
    }
}
