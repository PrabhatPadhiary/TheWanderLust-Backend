namespace TheWanderLustWebAPI.Models
{
    public class JournalPhoto
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public string Url { get; set; }
        public string? Caption { get; set; }
        public int Order { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Journal Journal { get; set; }
    }
}
