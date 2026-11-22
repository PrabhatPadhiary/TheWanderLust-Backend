namespace TheWanderLustWebAPI.Models
{
    public class Favourite
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string PlaceId { get; set; }
        public string PlaceName { get; set; }
        public string Vicinity { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string PhotoUrl { get; set; }
        public string Category { get; set; }

        // The place's city, from Google's address (Place Details), for the app's
        // city collections. Null = not looked up yet; "" = Google has no city.
        public string City { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public User User { get; set; }
    }
}
