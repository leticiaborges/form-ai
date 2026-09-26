using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replaced_by_token",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "replaced_by_token_hash",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "replaced_by_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "replaced_by_token",
                table: "refresh_tokens",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);
        }
    }
}
