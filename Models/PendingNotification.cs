namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// A trip change waiting to be pushed to the rest of the trip. Changes by
    /// the same person, on the same trip, of the same kind, are sent together
    /// as one notification once that person stops for a while
    /// (see NotificationDigestWorker), so adding 6 places sends one push, not 6.
    ///
    /// Also likes on a story (JournalId set, TripId null): all likes on the
    /// same story go together, "Satish and 4 others liked your story".
    /// </summary>
    public class PendingNotification
    {
        public Guid Id { get; set; }
        public Guid? TripId { get; set; }
        /// <summary>Set for likes on a story (then TripId is null).</summary>
        public Guid? JournalId { get; set; }
        /// <summary>Who made the change (they don't get notified about it).</summary>
        public Guid ActorId { get; set; }
        /// <summary>See <see cref="NotificationKinds"/>.</summary>
        public string Kind { get; set; }
        /// <summary>"added", "updated", "removed" or "joined".</summary>
        public string Change { get; set; }
        /// <summary>The place / expense / itinerary item's name, when known.</summary>
        public string? ItemName { get; set; }
        /// <summary>Extra text for the message, e.g. an expense's amount.</summary>
        public string? Detail { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Trip? Trip { get; set; }
        public Journal? Journal { get; set; }
        public User Actor { get; set; }
    }

    public static class NotificationKinds
    {
        public const string Joined = "joined";
        public const string Places = "places";
        public const string Expenses = "expenses";
        public const string Itinerary = "itinerary";
        // Stories (sent to the story's author / the comment's author).
        public const string Likes = "likes";
        public const string Comment = "comment";
        public const string Reply = "reply";
    }
}
