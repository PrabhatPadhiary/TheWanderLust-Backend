namespace TheWanderLustWebAPI.Models
{
    /// <summary>One person blocking another: the blocker stops seeing their stories and comments.</summary>
    public class UserBlock
    {
        public Guid Id { get; set; }
        public Guid BlockerId { get; set; }
        public Guid BlockedId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User Blocker { get; set; }
        public User Blocked { get; set; }
    }
}
