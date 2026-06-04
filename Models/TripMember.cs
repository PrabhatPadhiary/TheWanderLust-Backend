namespace TheWanderLustWebAPI.Models
{
    public class TripMember
    {
        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        public Guid UserId { get; set; }
        public string Role { get; set; } = "member"; // "owner" or "member"
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Trip Trip { get; set; }
        public User User { get; set; }
    }
}
