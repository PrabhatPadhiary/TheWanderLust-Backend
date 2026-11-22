using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentRepliesAndStoryNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JournalId",
                table: "UserNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TripId",
                table: "PendingNotifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "JournalId",
                table: "PendingNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentCommentId",
                table: "JournalComments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_JournalId",
                table: "UserNotifications",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingNotifications_JournalId",
                table: "PendingNotifications",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalComments_ParentCommentId",
                table: "JournalComments",
                column: "ParentCommentId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalComments_JournalComments_ParentCommentId",
                table: "JournalComments",
                column: "ParentCommentId",
                principalTable: "JournalComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PendingNotifications_Journals_JournalId",
                table: "PendingNotifications",
                column: "JournalId",
                principalTable: "Journals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserNotifications_Journals_JournalId",
                table: "UserNotifications",
                column: "JournalId",
                principalTable: "Journals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalComments_JournalComments_ParentCommentId",
                table: "JournalComments");

            migrationBuilder.DropForeignKey(
                name: "FK_PendingNotifications_Journals_JournalId",
                table: "PendingNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_UserNotifications_Journals_JournalId",
                table: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_UserNotifications_JournalId",
                table: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_PendingNotifications_JournalId",
                table: "PendingNotifications");

            migrationBuilder.DropIndex(
                name: "IX_JournalComments_ParentCommentId",
                table: "JournalComments");

            migrationBuilder.DropColumn(
                name: "JournalId",
                table: "UserNotifications");

            migrationBuilder.DropColumn(
                name: "JournalId",
                table: "PendingNotifications");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "JournalComments");

            migrationBuilder.AlterColumn<Guid>(
                name: "TripId",
                table: "PendingNotifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
