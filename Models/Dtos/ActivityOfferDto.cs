#nullable enable
namespace TheWanderLustWebAPI.Models.Dtos
{
    /// <summary>A bookable tour or ticket from a partner (Viator).</summary>
    public class ActivityOfferDto
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? ImageUrl { get; set; }
        public double? Rating { get; set; }
        public int ReviewCount { get; set; }
        /// <summary>Fixed length, or the shortest length of a variable one.</summary>
        public int? DurationMinutes { get; set; }
        /// <summary>Longest length when the duration varies (e.g. 6–7 hours).</summary>
        public int? DurationToMinutes { get; set; }
        public decimal? FromPrice { get; set; }
        public string Currency { get; set; } = "INR";
        public bool FreeCancellation { get; set; }
        /// <summary>The partner's page, with our affiliate tracking: never edit it.</summary>
        public string BookingUrl { get; set; } = "";
    }

    public class ActivityOffersDto
    {
        public string Provider { get; set; } = "Viator";
        /// <summary>The partner's name for the area these results are from.</summary>
        public string? AreaName { get; set; }
        /// <summary>"place": tours for the place asked about; "area": the area's top tours.</summary>
        public string Scope { get; set; } = "area";
        /// <summary>The partner's page with every tour in the area (tracked link).</summary>
        public string? SeeAllUrl { get; set; }
        public List<ActivityOfferDto> Items { get; set; } = new();

        /// <summary>The partner couldn't be reached: not "no tours" (not sent to the app).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Failed { get; set; }

        public static ActivityOffersDto Empty => new();
        public static ActivityOffersDto Unavailable => new() { Failed = true };
    }
}

namespace TheWanderLustWebAPI.Models.Dtos
{
    /// <summary>One tour's full details (Viator product content), for the details page.</summary>
    public class ActivityDetailDto
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public List<string> Images { get; set; } = new();
        public double? Rating { get; set; }
        public int ReviewCount { get; set; }
        public int? DurationMinutes { get; set; }
        public int? DurationToMinutes { get; set; }
        public bool FreeCancellation { get; set; }
        public string? CancellationPolicy { get; set; }
        public bool InstantConfirmation { get; set; }
        public bool PrivateTour { get; set; }
        public bool SkipTheLine { get; set; }
        public int? MaxGroupSize { get; set; }
        /// <summary>Guide / audio / written languages, e.g. "English".</summary>
        public List<string> Languages { get; set; } = new();
        public List<string> Inclusions { get; set; } = new();
        public List<string> Exclusions { get; set; } = new();
        /// <summary>Accessibility, fitness and other notes.</summary>
        public List<string> GoodToKnow { get; set; } = new();
        public List<ActivityStopDto> Stops { get; set; } = new();
        /// <summary>Free-text itinerary when the tour has no list of stops.</summary>
        public string? ItineraryText { get; set; }
        public ActivityPointDto? MeetingPoint { get; set; }
        public ActivityPointDto? EndPoint { get; set; }
        /// <summary>"Pickup included", "Pickup offered, or meet at the start".</summary>
        public string? Pickup { get; set; }
        public string? PickupNote { get; set; }
        public string? TicketInfo { get; set; }
        /// <summary>"person" or "group": what the "from" price is for.</summary>
        public string? PricePer { get; set; }
        /// <summary>The partner's page for this tour (tracked link: never edit it).</summary>
        public string BookingUrl { get; set; } = "";
    }

    public class ActivityStopDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? DurationMinutes { get; set; }
        /// <summary>"Day 2" for multi-day tours.</summary>
        public string? Day { get; set; }
        public bool PassBy { get; set; }
        /// <summary>true / false when the partner says whether admission is included.</summary>
        public bool? AdmissionIncluded { get; set; }
    }

    public class ActivityPointDto
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Description { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
