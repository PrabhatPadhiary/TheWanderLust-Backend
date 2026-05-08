namespace TheWanderLustWebAPI.Models.Dtos
{
    public class PlaceCategoriesResponseDto
    {
        public string PlaceId { get; set; }
        public string Name { get; set; }
        public string FormattedAddress { get; set; }
        public GeometryDto Geometry { get; set; }
        public List<PlaceDto> Restaurants { get; set; } = new();
        public List<PlaceDto> Lodging { get; set; } = new();
        public List<PlaceDto> TouristAttractions { get; set; } = new();
    }

    public class PlaceDto
    {
        public string PlaceId { get; set; }
        public string Name { get; set; }
        public string Vicinity { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public GeometryDto Geometry { get; set; }
        public List<PhotoDto> Photos { get; set; } = new();
        public List<string> Types { get; set; } = new();
    }

    public class GeometryDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class PhotoDto
    {
        public string Url { get; set; }
    }
}
