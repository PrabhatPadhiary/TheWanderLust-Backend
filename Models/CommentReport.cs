namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// Someone reporting a comment or reply (spam, harassment, ...). Reviewed by
    /// a moderator (Reports in the app); the reporter no longer sees the comment.
    /// One report per person per comment.
    /// </summary>
    public class CommentReport
    {
        public Guid Id { get; set; }
        public Guid CommentId { get; set; }
        public Guid ReporterId { get; set; }
        public string Reason { get; set; } = "other"; // spam | inappropriate | harassment | false_info | other
        public string? Details { get; set; }
        public string Status { get; set; } = "open"; // open | resolved
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public JournalComment Comment { get; set; }
        public User Reporter { get; set; }
    }
}
