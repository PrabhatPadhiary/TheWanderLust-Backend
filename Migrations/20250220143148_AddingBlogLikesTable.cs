using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddingBlogLikesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogLike",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BlogId = table.Column<int>(type: "int", nullable: false),
                    UserEmail = table.Column<string>(type: "VARCHAR(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogLike", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogLike_Blogs_BlogId",
                        column: x => x.BlogId,
                        principalTable: "Blogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BlogLike_BlogId_UserEmail",
                table: "BlogLike",
                columns: new[] { "BlogId", "UserEmail" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogLike");
        }
    }
}
