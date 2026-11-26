using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Works out someone's traveller rank from what they've done (see
    /// TravellerRanks for the rules). Used for your own profile and, level and
    /// name only, on a traveller's public profile.
    /// </summary>
    public static class RankCalculator
    {
        public static async Task<TravellerRanks.Progress> ForUserAsync(AppDbContext db, Guid userId)
        {
            var tripIds = db.TripMembers
                .Where(m => m.UserId == userId)
                .Select(m => m.TripId)
                .Union(db.Trips.Where(t => t.UserId == userId).Select(t => t.Id));

            var placesSaved = await db.TripPlaces
                .Where(p => tripIds.Contains(p.TripDestination.TripId))
                .CountAsync();

            var today = DateTime.UtcNow.Date;
            var rankTrips = await db.Trips
                .Where(t => tripIds.Contains(t.Id) && t.Destinations.Any())
                .Select(t => new { t.Id, t.EndDate, t.CreatedAt })
                .ToListAsync();
            // Taken: its dates have passed, and it was made before it ended
            // (a trip back-dated after the fact doesn't count).
            var takenIds = rankTrips
                .Where(t => t.EndDate != null && t.EndDate.Value.Date < today && t.CreatedAt.Date <= t.EndDate.Value.Date)
                .Select(t => t.Id)
                .ToList();
            var citiesVisited = takenIds.Count == 0 ? 0 : await db.TripDestinations
                .Where(d => takenIds.Contains(d.TripId))
                .Select(d => d.GooglePlaceId)
                .Distinct()
                .CountAsync();
            // Shared stories: published, public, with at least one photo.
            var shared = await db.Journals
                .Where(j => j.UserId == userId && j.Status == "published" && j.Visibility == "public" && j.Photos.Any())
                .Select(j => j.LikesCount)
                .ToListAsync();

            return TravellerRanks.Compute(
                tripsPlanned: rankTrips.Count,
                tripsTaken: takenIds.Count,
                cities: citiesVisited,
                stories: shared.Count,
                likes: shared.Sum(),
                places: placesSaved);
        }
    }
}
