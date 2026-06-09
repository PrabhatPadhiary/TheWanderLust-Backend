using System.Text.Json.Serialization;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateTripExpenseDto
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public string Category { get; set; }   // "stay" | "food" | "activity" | "transport" | "other"
        public DateTime Date { get; set; }
        public string PaidByMemberId { get; set; }
        public string PaidByName { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateTripExpenseDto
    {
        public string? Title { get; set; }
        public decimal? Amount { get; set; }
        public string? Category { get; set; }  // "stay" | "food" | "activity" | "transport" | "other"
        public DateTime? Date { get; set; }
        public string? PaidByMemberId { get; set; }
        public string? PaidByName { get; set; }
        public string? Notes { get; set; }
    }
}
