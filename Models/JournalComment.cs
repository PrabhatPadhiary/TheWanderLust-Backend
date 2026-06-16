namespace TheWanderLustWebAPI.Models
{
    public class JournalComment
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public Guid UserId { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public Journal Journal { get; set; }
        public User User { get; set; }
    }
}
