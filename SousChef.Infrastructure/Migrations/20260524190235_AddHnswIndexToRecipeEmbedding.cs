using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SousChef.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHnswIndexToRecipeEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_recipes_embedding_hnsw " +
                "ON \"Recipes\" USING hnsw (\"Embedding\" vector_cosine_ops)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_recipes_embedding_hnsw");
        }
    }
}
