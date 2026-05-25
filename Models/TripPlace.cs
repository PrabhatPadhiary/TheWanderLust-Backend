namespace TheWanderLustWebAPI.Models
{
    public class TripPlace
    {
        public Guid Id { get; set; }
        public Guid TripDestinationId { get; set; }
        public string PlaceId { get; set; }
        public string PlaceName { get; set; }
        public string? Vicinity { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string? PhotoUrl { get; set; }
        public string Category { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public TripDestination TripDestination { get; set; }
    }
}
