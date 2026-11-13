namespace TheWanderLustWebAPI.Models
{
    public class Invitation
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public string Role { get; set; } = "member"; // "owner", "member", or "viewer"
        public DateTime ExpiresAt { get; set; }
        public Guid? UsedBy { get; set; }

        /// <summary>
        /// A trip's shared link: anyone with it can join (with this role) until the
        /// owner resets it. Single-use links (false) work for
        /// one person and expire at <see cref="ExpiresAt"/>.
        /// </summary>
        public bool IsShared { get; set; }

        /// <summary>When a shared link stopped working (the owner reset it).</summary>
        public DateTime? RevokedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Trip Trip { get; set; }
        public User? UsedByUser { get; set; }
    }
}
