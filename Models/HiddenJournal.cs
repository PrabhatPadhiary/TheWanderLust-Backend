namespace TheWanderLustWebAPI.Models
{
    /// <summary>A story someone marked "Not interested": hidden from their feed only.</summary>
    public class HiddenJournal
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid JournalId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; }
        public Journal Journal { get; set; }
    }
}
