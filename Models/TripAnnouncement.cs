namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// A note from the trip's owner to everyone on it ("Flight PNR AB123, meet
    /// at Gate 4, 6am"), shown at the top of the trip and pushed to the others.
    /// </summary>
    public class TripAnnouncement
    {
        public const int MaxLength = 500;

        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        /// <summary>Null once the author deleted their account (the note stays).</summary>
        public Guid? AuthorId { get; set; }
        public string Text { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>Set when the text was edited.</summary>
        public DateTime? EditedAt { get; set; }

        public Trip Trip { get; set; }
        public User? Author { get; set; }
    }
}
