namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// Someone reporting a story (spam, inappropriate, ...). Reviewed by hand;
    /// the reporter no longer sees the story. One report per person per story.
    /// </summary>
    public class JournalReport
    {
        public Guid Id { get; set; }
        public Guid JournalId { get; set; }
        public Guid ReporterId { get; set; }
        public string Reason { get; set; } = "other"; // spam | inappropriate | harassment | false_info | other
        public string? Details { get; set; }
        public string Status { get; set; } = "open"; // open | resolved
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Journal Journal { get; set; }
        public User Reporter { get; set; }
    }
}
