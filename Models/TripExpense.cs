namespace TheWanderLustWebAPI.Models
{
    public enum ExpenseCategory
    {
        Stay,
        Food,
        Activity,
        Transport,
        Other
    }

    public class TripExpense
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public ExpenseCategory Category { get; set; }
        public DateTime Date { get; set; }
        public string PaidByMemberId { get; set; }
        public string PaidByName { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Trip Trip { get; set; }
    }
}
