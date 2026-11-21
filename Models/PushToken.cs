namespace TheWanderLustWebAPI.Models
{
    /// <summary>
    /// One phone that can receive push notifications for a user (Expo push
    /// token, e.g. "ExponentPushToken[xxxx]"). A user can have several phones;
    /// a token belongs to whoever signed in on that phone most recently.
    /// </summary>
    public class PushToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Token { get; set; }
        /// <summary>"ios" or "android".</summary>
        public string? Platform { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>Last time the app registered this token (every app start).</summary>
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; }
    }
}
