using System.Globalization;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Decides which trip changes the rest of the trip hears about by push, and
    /// queues them (<see cref="PendingNotification"/>). NotificationDigestWorker
    /// groups and sends them. Called from TripLiveUpdatesFilter after a
    /// successful change; never fails the request.
    ///
    /// Notified: someone joins, adds a place, adds an expense, or changes the
    /// itinerary. Everything else (checklist, dates, stops...) only updates live.
    /// </summary>
    public class TripNotifier
    {
        private readonly AppDbContext _dbContext;
        private readonly NotificationQueueSignal _signal;
        private readonly ILogger<TripNotifier> _logger;

        public TripNotifier(AppDbContext dbContext, NotificationQueueSignal signal, ILogger<TripNotifier> logger)
        {
            _dbContext = dbContext;
            _signal = signal;
            _logger = logger;
        }

        /// <param name="controller">e.g. "TripPlaces"</param>
        /// <param name="action">e.g. "Add"</param>
        /// <param name="change">"added", "updated", "removed", "joined"</param>
        /// <param name="result">What the action returned (for the item's name / amount).</param>
        public async Task QueueAsync(Guid tripId, Guid actorId, string controller, string action, string change,
            string? itemName, object? result)
        {
            var kind = (controller, action) switch
            {
                ("Invitations", "Join") => NotificationKinds.Joined,
                ("TripPlaces", "Add") => NotificationKinds.Places,
                ("TripExpenses", "Create") => NotificationKinds.Expenses,
                // Not DeleteOutsideRange: that runs by itself when the trip's dates change.
                ("Itinerary", "Create" or "Update" or "Delete" or "Arrange") => NotificationKinds.Itinerary,
                _ => null,
            };
            if (kind == null) return;

            try
            {
                string? detail = null;
                if (kind == NotificationKinds.Expenses
                    && result?.GetType().GetProperty("Amount")?.GetValue(result) is decimal amount)
                {
                    var currency = _dbContext.Trips.Where(t => t.Id == tripId).Select(t => t.Currency).FirstOrDefault();
                    detail = FormatAmount(amount, currency);
                }

                _dbContext.PendingNotifications.Add(new PendingNotification
                {
                    Id = Guid.NewGuid(),
                    TripId = tripId,
                    ActorId = actorId,
                    Kind = kind,
                    Change = action == "Arrange" ? "arranged" : change,
                    ItemName = Truncate(itemName, 200),
                    Detail = detail,
                    CreatedAt = DateTime.UtcNow,
                });
                await _dbContext.SaveChangesAsync();
                _signal.Wake(); // the worker works out when to send it
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push: couldn't queue a notification for trip {TripId}", tripId);
            }
        }

        private static string FormatAmount(decimal amount, string? currency)
        {
            var number = amount % 1 == 0
                ? amount.ToString("#,0", CultureInfo.InvariantCulture)
                : amount.ToString("#,0.00", CultureInfo.InvariantCulture);
            var symbol = currency?.ToUpperInvariant() switch
            {
                "INR" => "₹",
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "JPY" => "¥",
                null or "" => null,
                // Trips store a symbol ("₹"); a code ("AED") gets a space.
                var c => c.Trim().Length <= 2 ? c.Trim() : c.Trim() + " ",
            };
            return symbol + number;
        }

        private static string? Truncate(string? s, int max) =>
            s == null || s.Length <= max ? s : s[..max];
    }
}
