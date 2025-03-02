using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddingBlogCommentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogComments",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BlogId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Author = table.Column<string>(type: "VARCHAR(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogComments", x => x.CommentId);
                    table.ForeignKey(
                        name: "FK_BlogComments_Blogs_BlogId",
                        column: x => x.BlogId,
                        principalTable: "Blogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BlogComments_BlogId",
                table: "BlogComments",
                column: "BlogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogComments");
        }
    }
}
