namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateTripDto
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? CoverPhotoUrl { get; set; }
        public int TravelersCount { get; set; }
        public string? PrimaryDestination { get; set; }
        public CreateTripDestinationDto Destination { get; set; }
    }

    public class UpdateTripDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? CoverPhotoUrl { get; set; }
        public int? TravelersCount { get; set; }
        public string? PrimaryDestination { get; set; }
    }
}
