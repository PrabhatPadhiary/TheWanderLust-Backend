namespace TheWanderLustWebAPI.Models
{
    public class Trip
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? CoverPhotoUrl { get; set; }
        public int TravelersCount { get; set; }
        public string? PrimaryDestination { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; }

        public string Status
        {
            get
            {
                if (StartDate == null || EndDate == null)
                    return "draft";

                var today = DateTime.UtcNow.Date;

                if (EndDate.Value.Date < today)
                    return "completed";

                if (StartDate.Value.Date <= today && today <= EndDate.Value.Date)
                    return "in_progress";

                return "planning";
            }
        }

        // Navigation properties
        public User User { get; set; }
        public ICollection<TripDestination> Destinations { get; set; } = new List<TripDestination>();
    }
}
