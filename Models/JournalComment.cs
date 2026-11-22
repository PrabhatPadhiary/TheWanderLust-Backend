namespace TheWanderLustWebAPI.Models
{
    public class JournalComment
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public Guid UserId { get; set; }
        /// <summary>
        /// Set on a reply: the top-level comment it sits under. Replies are one
        /// level deep (answering a reply attaches to the same top-level
        /// comment), and go when that comment is deleted.
        /// </summary>
        public Guid? ParentCommentId { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public Journal Journal { get; set; }
        public User User { get; set; }
        public JournalComment? ParentComment { get; set; }
    }
}
