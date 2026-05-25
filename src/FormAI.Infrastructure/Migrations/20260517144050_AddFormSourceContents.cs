using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FormAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormSourceContents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_source_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_source_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_source_contents_forms_form_id",
                        column: x => x.form_id,
                        principalTable: "forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_source_contents_form_id",
                table: "form_source_contents",
                column: "form_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_source_contents");
        }
    }
}
