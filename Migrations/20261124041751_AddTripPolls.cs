using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTripPolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripPolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Question = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ItineraryItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripPolls_ItineraryItems_ItineraryItemId",
                        column: x => x.ItineraryItemId,
                        principalTable: "ItineraryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TripPolls_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripPolls_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TripPollOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TripPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPollOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripPollOptions_TripPlaces_TripPlaceId",
                        column: x => x.TripPlaceId,
                        principalTable: "TripPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TripPollOptions_TripPolls_PollId",
                        column: x => x.PollId,
                        principalTable: "TripPolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripPollVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PollId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripPollVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripPollVotes_TripPollOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "TripPollOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripPollVotes_TripPolls_PollId",
                        column: x => x.PollId,
                        principalTable: "TripPolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripPollVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripPollOptions_PollId",
                table: "TripPollOptions",
                column: "PollId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPollOptions_TripPlaceId",
                table: "TripPollOptions",
                column: "TripPlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPolls_CreatedByUserId",
                table: "TripPolls",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPolls_ItineraryItemId",
                table: "TripPolls",
                column: "ItineraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPolls_TripId_CreatedAt",
                table: "TripPolls",
                columns: new[] { "TripId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TripPollVotes_OptionId",
                table: "TripPollVotes",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_TripPollVotes_PollId_UserId",
                table: "TripPollVotes",
                columns: new[] { "PollId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripPollVotes_UserId",
                table: "TripPollVotes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripPollVotes");

            migrationBuilder.DropTable(
                name: "TripPollOptions");

            migrationBuilder.DropTable(
                name: "TripPolls");
        }
    }
}
