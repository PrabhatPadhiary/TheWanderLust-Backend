namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateTripPlaceDto
    {
        public string PlaceId { get; set; }
        public string PlaceName { get; set; }
        public string? Vicinity { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string? PhotoUrl { get; set; }
        public string Category { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Notes { get; set; }
    }
}
