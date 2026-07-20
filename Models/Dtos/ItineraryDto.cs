namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateItineraryItemDto
    {
        public Guid DestinationId { get; set; }
        public Guid? TripPlaceId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; } = "custom";
        public DateTime ScheduledDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateItineraryItemDto
    {
        public Guid? DestinationId { get; set; }
        public Guid? TripPlaceId { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Notes { get; set; }
    }
}
