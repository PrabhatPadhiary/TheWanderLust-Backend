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

        /// <summary>food | stay | activity</summary>
        public string Category { get; set; }

        public string? Notes { get; set; }
    }
}
