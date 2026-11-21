using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// The one way a notification goes out: saved to each person's bell
    /// (<see cref="UserNotification"/>), then pushed to their phones. Keeping
    /// both in one place means the bell and the pushes always match.
    /// </summary>
    public class NotificationSender
    {
        private readonly AppDbContext _dbContext;
        private readonly PushService _push;
        private readonly ILogger<NotificationSender> _logger;

        public NotificationSender(AppDbContext dbContext, PushService push, ILogger<NotificationSender> logger)
        {
            _dbContext = dbContext;
            _push = push;
            _logger = logger;
        }

        /// <param name="recipients">Who gets it (leave out whoever caused it).</param>
        /// <param name="type">"joined", "places", "expenses", "itinerary", "announcement".</param>
        /// <param name="title">Usually the trip's name.</param>
        public async Task SendAsync(IEnumerable<Guid> recipients, Guid? tripId, Guid? actorId, string type,
            string title, string body, string? pushTitle = null)
        {
            var userIds = recipients.Distinct().ToList();
            if (userIds.Count == 0) return;

            try
            {
                var now = DateTime.UtcNow;
                _dbContext.UserNotifications.AddRange(userIds.Select(userId => new UserNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TripId = tripId,
                    ActorId = actorId,
                    Type = type,
                    Title = Truncate(title, 200),
                    Body = Truncate(body, 600),
                    CreatedAt = now,
                }));
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Still push: the phone matters more than the bell.
                _logger.LogWarning(ex, "Notifications: couldn't save to the bell");
            }

            await _push.SendToUsersAsync(userIds, pushTitle ?? title, body, new
            {
                type,
                tripId,
                tripName = title,
            });
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
