using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeVault.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaAndRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "mfa_secret_encrypted",
                table: "users",
                newName: "mfa_secret_nonce");

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret_algorithm",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mfa_secret_auth_tag",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mfa_secret_ciphertext",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "mfa_secret_encrypted_dek",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "mfa_verified",
                table: "refresh_tokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mfa_secret_algorithm",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_secret_auth_tag",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_secret_ciphertext",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_secret_encrypted_dek",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_verified",
                table: "refresh_tokens");

            migrationBuilder.RenameColumn(
                name: "mfa_secret_nonce",
                table: "users",
                newName: "mfa_secret_encrypted");
        }
    }
}
