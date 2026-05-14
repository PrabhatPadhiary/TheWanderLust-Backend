namespace TheWanderLustWebAPI.Models.Dtos
{
    public class PlaceDetailsDto
    {
        public string PlaceId { get; set; }
        public string Name { get; set; }
        public string FormattedAddress { get; set; }
        public string FormattedPhoneNumber { get; set; }
        public string Website { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public int? PriceLevel { get; set; }
        public GeometryDto Geometry { get; set; }
        public List<PhotoDto> Photos { get; set; } = new();
        public List<ReviewDto> Reviews { get; set; } = new();
        public OpeningHoursDto OpeningHours { get; set; }
    }

    public class ReviewDto
    {
        public string AuthorName { get; set; }
        public string ProfilePhotoUrl { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; }
        public string RelativeTimeDescription { get; set; }
    }

    public class OpeningHoursDto
    {
        public bool? OpenNow { get; set; }
        public List<string> WeekdayText { get; set; } = new();
    }
}
