namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// Someone reporting a traveller's profile (their name, photo or bio:
    /// spam, harassment, ...). Reviewed by a moderator (Reports in the app),
    /// who can clear the bio or suspend the account. One report per person
    /// per profile.
    /// </summary>
    public class UserReport
    {
        public Guid Id { get; set; }
        public Guid ReportedUserId { get; set; }
        public Guid ReporterId { get; set; }
        public string Reason { get; set; } = "other"; // spam | inappropriate | harassment | false_info | other
        public string? Details { get; set; }
        public string Status { get; set; } = "open"; // open | resolved
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User ReportedUser { get; set; }
        public User Reporter { get; set; }
    }
}
