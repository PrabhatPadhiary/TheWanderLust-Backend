using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPhotoSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GooglePhotoSourceUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoSource",
                table: "Users",
                type: "text",
                nullable: true);

            // Photos people already have were chosen by them: Google never
            // replaces those.
            migrationBuilder.Sql(@"UPDATE ""Users"" SET ""PhotoSource"" = 'upload' WHERE ""PhotoUrl"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GooglePhotoSourceUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhotoSource",
                table: "Users");
        }
    }
}
