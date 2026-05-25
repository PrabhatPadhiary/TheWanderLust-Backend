namespace TheWanderLustWebAPI.Models
{
    public class TripDestination
    {
        public Guid Id { get; set; }
        public string GooglePlaceId { get; set; }
        public Guid TripId { get; set; }
        public string Name { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhotoUrl { get; set; }
        public int Order { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Trip Trip { get; set; }
        public ICollection<TripPlace> Places { get; set; } = new List<TripPlace>();
    }
}
