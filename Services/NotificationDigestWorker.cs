using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Sends the queued trip changes (<see cref="PendingNotification"/>) as push
    /// notifications, grouped: one notification per person, per trip, per kind
    /// of change. A group goes out once that person has stopped for
    /// <see cref="QuietFor"/> (so adding 6 places in a row sends one push:
    /// "Satish added 6 places: ..."), or at the latest <see cref="HoldAtMost"/>
    /// after their first change. Joins go out straight away.
    ///
    /// The queue is in the database, so a restart doesn't lose anything: what
    /// was waiting is sent once the server is back.
    /// </summary>
    public class NotificationDigestWorker : BackgroundService
    {
        public static readonly TimeSpan QuietFor = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan HoldAtMost = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);
        /// <summary>Changes older than this are dropped instead of sent (e.g. after a long outage).</summary>
        private static readonly TimeSpan TooOld = TimeSpan.FromDays(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<NotificationDigestWorker> _logger;

        public NotificationDigestWorker(IServiceScopeFactory scopes, ILogger<NotificationDigestWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Tick);
            do
            {
                try
                {
                    await SendDueAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Push: sending queued notifications failed; will retry");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task SendDueAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var push = scope.ServiceProvider.GetRequiredService<PushService>();

            var now = DateTime.UtcNow;
            await db.PendingNotifications.Where(p => p.CreatedAt < now - TooOld).ExecuteDeleteAsync(ct);

            var pending = await db.PendingNotifications.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(ct);
            if (pending.Count == 0) return;

            var due = pending
                .GroupBy(p => (p.TripId, p.ActorId, p.Kind))
                .Where(g => g.Key.Kind == NotificationKinds.Joined
                    || g.Max(p => p.CreatedAt) <= now - QuietFor
                    || g.Min(p => p.CreatedAt) <= now - HoldAtMost)
                .ToList();
            if (due.Count == 0) return;

            var tripIds = due.Select(g => g.Key.TripId).Distinct().ToList();
            var actorIds = due.Select(g => g.Key.ActorId).Distinct().ToList();
            var trips = await db.Trips
                .Where(t => tripIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name, MemberIds = t.Members.Select(m => m.UserId).ToList() })
                .ToDictionaryAsync(t => t.Id, ct);
            var actors = await db.Users
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

            foreach (var group in due)
            {
                var items = group.ToList();
                if (trips.TryGetValue(group.Key.TripId, out var trip))
                {
                    var recipients = trip.MemberIds.Where(id => id != group.Key.ActorId).ToList();
                    var actorName = FirstName(actors.GetValueOrDefault(group.Key.ActorId));
                    var body = Compose(group.Key.Kind, actorName, items);
                    if (recipients.Count > 0 && body != null)
                    {
                        await push.SendToUsersAsync(recipients, trip.Name, body, new
                        {
                            type = group.Key.Kind,
                            tripId = trip.Id,
                            tripName = trip.Name,
                        });
                    }
                }

                // Sent (or nobody left to tell): done with these.
                var ids = items.Select(p => p.Id).ToList();
                await db.PendingNotifications.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync(ct);
            }
        }

        /// <summary>The notification's text, e.g. "Satish added 3 places: Burj Khalifa, Dubai Mall and 1 more".</summary>
        internal static string? Compose(string kind, string actor, List<PendingNotification> items)
        {
            switch (kind)
            {
                case NotificationKinds.Joined:
                    return $"{actor} joined the trip. Say hi and start planning together!";

                case NotificationKinds.Places:
                {
                    var names = Names(items);
                    if (items.Count == 1)
                        return names.Count == 1 ? $"{actor} added {names[0]}" : $"{actor} added a place";
                    return $"{actor} added {items.Count} places: {List(names, items.Count)}";
                }

                case NotificationKinds.Expenses:
                {
                    if (items.Count == 1)
                    {
                        var e = items[0];
                        var what = string.IsNullOrWhiteSpace(e.ItemName) ? "an expense" : $"an expense: {e.ItemName}";
                        return e.Detail == null ? $"{actor} added {what}" : $"{actor} added {what} ({e.Detail})";
                    }
                    return $"{actor} added {items.Count} expenses: {List(Names(items), items.Count)}";
                }

                case NotificationKinds.Itinerary:
                {
                    // A rearrange usually comes with another change (adding an
                    // item shifts the rest): only count it when it's all there is.
                    var changes = items.Where(i => i.Change != "arranged").ToList();
                    if (changes.Count == 0)
                        return $"{actor} rearranged the itinerary";
                    if (changes.All(i => i.Change == "added"))
                    {
                        if (changes.Count == 1)
                        {
                            var name = changes[0].ItemName;
                            return string.IsNullOrWhiteSpace(name)
                                ? $"{actor} added a plan to the itinerary"
                                : $"{actor} added {name} to the itinerary";
                        }
                        return $"{actor} added {changes.Count} plans to the itinerary: {List(Names(changes), changes.Count)}";
                    }
                    if (changes.Count == 1)
                    {
                        var c = changes[0];
                        var name = string.IsNullOrWhiteSpace(c.ItemName) ? null : c.ItemName;
                        return c.Change switch
                        {
                            "removed" => $"{actor} removed a plan from the itinerary",
                            _ => name == null ? $"{actor} updated the itinerary" : $"{actor} updated {name} in the itinerary",
                        };
                    }
                    return $"{actor} made {changes.Count} changes to the itinerary";
                }

                default:
                    return null;
            }
        }

        private static List<string> Names(List<PendingNotification> items) => items
            .Select(i => i.ItemName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct()
            .ToList();

        /// <summary>"A, B and 3 more" (the total counts unnamed ones too).</summary>
        private static string List(List<string> names, int total)
        {
            var shown = names.Take(2).ToList();
            var rest = total - shown.Count;
            if (shown.Count == 0) return $"{total} items";
            if (rest <= 0) return shown.Count == 1 ? shown[0] : $"{shown[0]} and {shown[1]}";
            return $"{string.Join(", ", shown)} and {rest} more";
        }

        private static string FirstName(string? name)
        {
            var first = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrEmpty(first) ? "Someone" : first;
        }
    }
}
