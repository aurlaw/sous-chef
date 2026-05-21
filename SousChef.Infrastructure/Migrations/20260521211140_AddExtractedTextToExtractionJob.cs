using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SousChef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractedTextToExtractionJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "ExtractionJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "ExtractionJobs");
        }
    }
}
