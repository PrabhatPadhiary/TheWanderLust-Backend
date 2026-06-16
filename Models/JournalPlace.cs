namespace TheWanderLustWebAPI.Models
{
    public class JournalPlace
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public string PlaceName { get; set; }
        public string Category { get; set; } = "other"; // "food", "stay", "attraction", "other"
        public string? GooglePlaceId { get; set; }

        // Navigation property
        public Journal Journal { get; set; }
    }
}
