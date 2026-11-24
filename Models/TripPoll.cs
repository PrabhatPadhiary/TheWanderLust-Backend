namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// A question for everyone on the trip ("Dinner on Day 2: where?") with
    /// 2-6 options. One vote each, changeable until the poll closes. It closes
    /// when its creator or the owner ends it, or at <see cref="ClosesAt"/>.
    /// Optionally tied to a day and part of the day, so the winner can go
    /// straight into the itinerary there.
    /// </summary>
    public class TripPoll
    {
        public const int QuestionMaxLength = 140;
        public const int MinOptions = 2;
        public const int MaxOptions = 6;

        public Guid Id { get; set; }
        public Guid TripId { get; set; }
        /// <summary>Null once the creator deleted their account (the poll stays).</summary>
        public Guid? CreatedByUserId { get; set; }
        public string Question { get; set; }
        /// <summary>The day it's about (date only, UTC midnight), if any.</summary>
        public DateTime? ScheduledDate { get; set; }
        /// <summary>"morning" | "afternoon" | "evening" | "anytime", if any.</summary>
        public string? Slot { get; set; }
        /// <summary>Voting stops at this time, if set.</summary>
        public DateTime? ClosesAt { get; set; }
        /// <summary>Set when someone ended it early.</summary>
        public DateTime? ClosedAt { get; set; }
        /// <summary>The itinerary item made from the winner ("Add to Day 2").</summary>
        public Guid? ItineraryItemId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Trip Trip { get; set; }
        public User? CreatedByUser { get; set; }
        public List<TripPollOption> Options { get; set; } = new();
        public List<TripPollVote> Votes { get; set; } = new();

        public bool IsClosed(DateTime now) => ClosedAt != null || (ClosesAt != null && ClosesAt <= now);
    }

    /// <summary>
    /// One choice in a poll: a saved place on the trip (name and photo copied,
    /// so it survives the place being removed) or plain text ("Rest at the hotel").
    /// </summary>
    public class TripPollOption
    {
        public const int TextMaxLength = 120;

        public Guid Id { get; set; }
        public Guid PollId { get; set; }
        public string Text { get; set; }
        public Guid? TripPlaceId { get; set; }
        public string? PhotoUrl { get; set; }
        /// <summary>The saved place's category ("food", "stays"...), for the itinerary.</summary>
        public string? Category { get; set; }
        public int Order { get; set; }

        public TripPoll Poll { get; set; }
        public TripPlace? TripPlace { get; set; }
    }

    /// <summary>One person's vote. Unique per poll and person.</summary>
    public class TripPollVote
    {
        public Guid Id { get; set; }
        public Guid PollId { get; set; }
        public Guid OptionId { get; set; }
        public Guid UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public TripPoll Poll { get; set; }
        public TripPollOption Option { get; set; }
        public User User { get; set; }
    }
}
