namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// One entry in a person's in-app notifications (the bell on Home). Saved
    /// together with each push (NotificationSender), so the bell shows the
    /// same things, also for people who turned push off.
    /// </summary>
    public class UserNotification
    {
        public Guid Id { get; set; }
        /// <summary>Whose bell it's in.</summary>
        public Guid UserId { get; set; }
        public Guid? TripId { get; set; }
        /// <summary>Who did it (their photo on the row). Null once they deleted their account.</summary>
        public Guid? ActorId { get; set; }
        /// <summary>"joined", "places", "expenses", "itinerary", "announcement".</summary>
        public string Type { get; set; }
        /// <summary>Usually the trip's name.</summary>
        public string Title { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>When it was seen (the bell screen was opened), null while unread.</summary>
        public DateTime? ReadAt { get; set; }

        public User User { get; set; }
        public Trip? Trip { get; set; }
        public User? Actor { get; set; }
    }
}
