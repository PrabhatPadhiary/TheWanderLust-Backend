namespace TheWanderLustWebAPI.Settings
{
    /// <summary>Names of the rate-limit policies (see Program.cs).</summary>
    public static class RateLimits
    {
        /// <summary>Place search, nearby, lists and details: 60 per minute per IP.</summary>
        public const string Google = "google";

        /// <summary>Place photos: 300 per minute per IP.</summary>
        public const string Photos = "photos";
    }
}
