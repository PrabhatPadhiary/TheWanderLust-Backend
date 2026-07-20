namespace TheWanderLustWebAPI.Models
{
    public class ItineraryItem
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public Guid DestinationId { get; set; }
        public Guid? TripPlaceId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; } = "custom"; // "stay", "food", "activity", "transport", "custom"
        public DateTime ScheduledDate { get; set; }
        public string? StartTime { get; set; } // "09:00"
        public string? EndTime { get; set; } // "11:00"
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Trip Trip { get; set; }
        public TripDestination Destination { get; set; }
        public TripPlace? TripPlace { get; set; }
    }
}
