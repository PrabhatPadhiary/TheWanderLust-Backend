namespace TheWanderLustWebAPI.Models
{
    public class Invitation
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public string Role { get; set; } = "member"; // "owner", "member", or "viewer"
        public DateTime ExpiresAt { get; set; }
        public Guid? UsedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Trip Trip { get; set; }
        public User? UsedByUser { get; set; }
    }
}
