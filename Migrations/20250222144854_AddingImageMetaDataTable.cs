using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddingImageMetaDataTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Url = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    BlogId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageMetadata_Blogs_BlogId",
                        column: x => x.BlogId,
                        principalTable: "Blogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMetadata_BlogId",
                table: "ImageMetadata",
                column: "BlogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageMetadata");
        }
    }
}
