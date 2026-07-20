using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddItineraryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItineraryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: true),
                    EndTime = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItineraryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_TripDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "TripDestinations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItineraryItems_TripPlaces_TripPlaceId",
                        column: x => x.TripPlaceId,
                        principalTable: "TripPlaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ItineraryItems_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_DestinationId",
                table: "ItineraryItems",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripId",
                table: "ItineraryItems",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_ItineraryItems_TripPlaceId",
                table: "ItineraryItems",
                column: "TripPlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItineraryItems");
        }
    }
}
