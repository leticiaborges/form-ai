using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAnswerSelectedOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_answer_selected_options_question_options_option_id",
                table: "answer_selected_options");

            migrationBuilder.DropPrimaryKey(
                name: "pk_answer_selected_options",
                table: "answer_selected_options");

            migrationBuilder.DropIndex(
                name: "ix_answer_selected_options_option_id",
                table: "answer_selected_options");

            migrationBuilder.RenameColumn(
                name: "option_id",
                table: "answer_selected_options",
                newName: "id");

            migrationBuilder.AddColumn<string>(
                name: "option_text",
                table: "answer_selected_options",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "pk_answer_selected_options",
                table: "answer_selected_options",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_answer_selected_options_answer_id",
                table: "answer_selected_options",
                column: "answer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_answer_selected_options",
                table: "answer_selected_options");

            migrationBuilder.DropIndex(
                name: "ix_answer_selected_options_answer_id",
                table: "answer_selected_options");

            migrationBuilder.DropColumn(
                name: "option_text",
                table: "answer_selected_options");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "answer_selected_options",
                newName: "option_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_answer_selected_options",
                table: "answer_selected_options",
                columns: new[] { "answer_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_answer_selected_options_option_id",
                table: "answer_selected_options",
                column: "option_id");

            migrationBuilder.AddForeignKey(
                name: "fk_answer_selected_options_question_options_option_id",
                table: "answer_selected_options",
                column: "option_id",
                principalTable: "question_options",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
