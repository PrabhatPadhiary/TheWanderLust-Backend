namespace TheWanderLustWebAPI.Settings
{
    /// <summary>Names of the rate-limit policies (see Program.cs).</summary>
    public static class RateLimits
    {
        /// <summary>Place search, nearby, lists and details: 60 per minute per IP.</summary>
        public const string Google = "google";

        /// <summary>Place photos: 300 per minute per IP.</summary>
        public const string Photos = "photos";

        /// <summary>Partner offers (Viator tours): 60 per minute per IP.</summary>
        public const string Partners = "partners";

        // Per signed-in user (per IP if signed out), so one account or script
        // can't flood the community or people's notifications.

        /// <summary>Comments and replies (new and edited): 10 per minute.</summary>
        public const string Comments = "comments";

        /// <summary>Likes: 60 per minute.</summary>
        public const string Likes = "likes";

        /// <summary>Reports, "not interested", block / unblock: 20 per minute.</summary>
        public const string Safety = "safety";

        /// <summary>New stories, story edits and photo uploads (story and profile): 30 per minute.</summary>
        public const string Uploads = "uploads";
    }
}
