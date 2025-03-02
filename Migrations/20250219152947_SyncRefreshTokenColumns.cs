using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class SyncRefreshTokenColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                table: "User",
                type: "DATETIME",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AlterColumn<string>(
                name: "RefreshToken",
                table: "User",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                table: "User",
                type: "datetime(6)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "DATETIME");

            migrationBuilder.AlterColumn<string>(
                name: "RefreshToken",
                table: "User",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
