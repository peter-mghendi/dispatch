using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dispatch.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateUniqueIndexOnHandlerSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Handlers_Slug",
                table: "Handlers",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Handlers_Slug",
                table: "Handlers");
        }
    }
}
