using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUniqueIndexIpSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_submissions_form_id_respondent_token_ip_address",
                table: "submissions");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "question_options",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_form_id_respondent_token",
                table: "submissions",
                columns: new[] { "form_id", "respondent_token" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_submissions_form_id_respondent_token",
                table: "submissions");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "question_options",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_form_id_respondent_token_ip_address",
                table: "submissions",
                columns: new[] { "form_id", "respondent_token", "ip_address" },
                unique: true);
        }
    }
}
