namespace TheWanderLustWebAPI.Models
{
    public class JournalLike
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public Guid UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Journal Journal { get; set; }
        public User User { get; set; }
    }
}
