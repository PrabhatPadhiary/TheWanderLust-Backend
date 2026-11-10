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
        public string? Slot { get; set; }
        public int? SortOrder { get; set; }
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
        /// <summary>"morning" | "afternoon" | "evening" | "anytime"; "" clears it.</summary>
        public string? Slot { get; set; }
        public int? SortOrder { get; set; }
    }

    /// <summary>One item's new place after a drag: day, part of day and order.</summary>
    public class ArrangeItineraryItemDto
    {
        public Guid Id { get; set; }
        public DateTime ScheduledDate { get; set; }
        public string? Slot { get; set; }
        public int SortOrder { get; set; }
        /// <summary>Clear the exact time (moved to another part of the day).</summary>
        public bool ClearTime { get; set; }
    }

    public class DateRangeDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
