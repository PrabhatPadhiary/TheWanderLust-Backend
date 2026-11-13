using System.Collections.Concurrent;

namespace TheWanderLustWebAPI.Live
{
    /// <summary>
    /// Who has each trip open right now, for "Satish is here too" on the trip
    /// screen. Kept in memory per connection (one person on two phones counts
    /// once). Fine while the API runs as a single instance; with several
    /// instances this would move to Redis alongside a SignalR backplane.
    /// </summary>
    public class TripPresence
    {
        public record Viewer(Guid UserId, string Name, string? PhotoUrl);

        // tripId → (connectionId → viewer)
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, Viewer>> _trips = new();

        public void Add(Guid tripId, string connectionId, Viewer viewer) =>
            _trips.GetOrAdd(tripId, _ => new ConcurrentDictionary<string, Viewer>())[connectionId] = viewer;

        /// <summary>Removes the connection from one trip. Returns true if it was there.</summary>
        public bool Remove(Guid tripId, string connectionId)
        {
            if (!_trips.TryGetValue(tripId, out var viewers) || !viewers.TryRemove(connectionId, out _))
                return false;
            if (viewers.IsEmpty)
                _trips.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<string, Viewer>>(tripId, viewers));
            return true;
        }

        /// <summary>Removes a dropped connection from every trip; returns the trips it left.</summary>
        public List<Guid> RemoveEverywhere(string connectionId)
        {
            var left = new List<Guid>();
            foreach (var tripId in _trips.Keys)
            {
                if (Remove(tripId, connectionId))
                    left.Add(tripId);
            }
            return left;
        }

        /// <summary>Everyone with the trip open, once per person.</summary>
        public List<Viewer> ViewersOf(Guid tripId) =>
            _trips.TryGetValue(tripId, out var viewers)
                ? viewers.Values.GroupBy(v => v.UserId).Select(g => g.First()).ToList()
                : new List<Viewer>();
    }
}
