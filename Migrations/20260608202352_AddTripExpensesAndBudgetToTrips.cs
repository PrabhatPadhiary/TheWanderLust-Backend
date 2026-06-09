using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheWanderLustWebAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddTripExpensesAndBudgetToTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalBudget",
                table: "Trips",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TripExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidByMemberId = table.Column<string>(type: "text", nullable: true),
                    PaidByName = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripExpenses_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripExpenses_TripId",
                table: "TripExpenses",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripExpenses");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TotalBudget",
                table: "Trips");
        }
    }
}
