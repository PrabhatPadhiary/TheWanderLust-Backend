namespace TheWanderLustWebAPI.Models.Dtos
{
    public class CreateJournalDto
    {
        public Guid? TripId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Destination { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? TravelersCount { get; set; }
        public decimal? Budget { get; set; }
        public string? Currency { get; set; }
        public string Visibility { get; set; } = "private";
        public string? ProTips { get; set; }
        public string Status { get; set; } = "draft";
        public List<string>? Vibes { get; set; }
        public List<CreateJournalPlaceDto>? Places { get; set; }
    }

    public class UpdateJournalDto
    {
        public Guid? TripId { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Destination { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? TravelersCount { get; set; }
        public decimal? Budget { get; set; }
        public string? Currency { get; set; }
        public string? Visibility { get; set; }
        public string? ProTips { get; set; }
        public string? Status { get; set; }
        public List<string>? Vibes { get; set; }
        public List<CreateJournalPlaceDto>? Places { get; set; }
    }

    public class CreateJournalPlaceDto
    {
        public string PlaceName { get; set; }
        public string Category { get; set; } = "other";
        public string? GooglePlaceId { get; set; }
    }

    public class AddCommentDto
    {
        public string Body { get; set; }
    }
}
