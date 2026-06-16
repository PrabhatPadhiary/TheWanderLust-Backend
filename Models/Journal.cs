namespace TheWanderLustWebAPI.Models
{
    public class Journal
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? TripId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Destination { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? TravelersCount { get; set; }
        public decimal? Budget { get; set; }
        public string? Currency { get; set; }
        public string Visibility { get; set; } = "private"; // "public" or "private"
        public string? ProTips { get; set; }
        public string Status { get; set; } = "draft"; // "draft" or "published"
        public int LikesCount { get; set; } = 0;
        public int CommentsCount { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }

        // Navigation properties
        public User User { get; set; }
        public Trip? Trip { get; set; }
        public ICollection<JournalPlace> Places { get; set; } = new List<JournalPlace>();
    }
}
