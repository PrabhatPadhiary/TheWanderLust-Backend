using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Live
{
    /// <summary>
    /// Live updates for the trip screen. While a trip is open, the app joins that
    /// trip's group:
    ///  - whenever someone changes the trip, <see cref="TripLiveUpdatesFilter"/>
    ///    sends "tripChanged" to the group so everyone else refreshes;
    ///  - whenever someone opens or leaves it, the group gets "viewers" (who has
    ///    the trip open), for "Satish is here too".
    ///
    /// Mapped at /api/hubs/trips. Clients sign in with the same Firebase token as
    /// the API, passed as ?access_token= (WebSockets can't carry headers).
    /// </summary>
    [Authorize]
    public class TripHub : Hub
    {
        public const string Path = "/api/hubs/trips";

        private readonly AppDbContext _dbContext;
        private readonly TripPresence _presence;

        public TripHub(AppDbContext dbContext, TripPresence presence)
        {
            _dbContext = dbContext;
            _presence = presence;
        }

        public static string GroupFor(Guid tripId) => $"trip:{tripId}";

        /// <summary>Start getting updates for a trip. Only its owner and members may join.</summary>
        public async Task JoinTrip(Guid tripId)
        {
            var firebaseUid = Context.User?.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                throw new HubException("Not signed in.");

            var user = await _dbContext.Users
                .Where(u => u.FirebaseId == firebaseUid)
                .Select(u => new { u.Id, u.Name, u.PhotoUrl })
                .FirstOrDefaultAsync();

            var allowed = user != null && await _dbContext.Trips.AnyAsync(t => t.Id == tripId
                && (t.UserId == user.Id || t.Members.Any(m => m.UserId == user.Id)));
            if (!allowed)
                throw new HubException("You're not on this trip.");

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tripId));
            _presence.Add(tripId, Context.ConnectionId, new TripPresence.Viewer(user!.Id, user.Name, user.PhotoUrl));
            await BroadcastViewers(tripId);
        }

        /// <summary>Stop getting updates for a trip (leaving the trip screen).</summary>
        public async Task LeaveTrip(Guid tripId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(tripId));
            if (_presence.Remove(tripId, Context.ConnectionId))
                await BroadcastViewers(tripId);
        }

        /// <summary>App closed or connection lost: drop the person from the trips they had open.</summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var tripId in _presence.RemoveEverywhere(Context.ConnectionId))
                await BroadcastViewers(tripId);
            await base.OnDisconnectedAsync(exception);
        }

        private Task BroadcastViewers(Guid tripId) =>
            Clients.Group(GroupFor(tripId)).SendAsync("viewers", new
            {
                tripId,
                viewers = _presence.ViewersOf(tripId).Select(v => new { userId = v.UserId, name = v.Name, photoUrl = v.PhotoUrl }),
            });
    }
}
