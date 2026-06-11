namespace TheWanderLustWebAPI.Models
{
    public class TripChecklistItem
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; } = "other"; // "packing", "bookings", "documents", "money", "safety", "other"
        public DateTime? DueDate { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public bool IsCompleted { get; set; } = false;
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedByUserId { get; set; }
        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Order { get; set; }

        // Navigation properties
        public Trip Trip { get; set; }
        public User? AssignedToUser { get; set; }
        public User? CompletedByUser { get; set; }
        public User CreatedByUser { get; set; }
    }
}
