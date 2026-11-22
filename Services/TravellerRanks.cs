namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Traveller ranks: points for what you do in the app, plus a few real
    /// requirements on the higher ranks so they can't be farmed with points alone.
    ///
    /// Points:
    ///   Story shared (published, public, with a photo)   +50
    ///   Trip taken (dates passed, planned before it ended) +30
    ///   Trip planned (has at least one destination)        +20
    ///   City visited (distinct, from trips taken)          +10
    ///   Like on your shared stories                         +2  (at most 500 from likes)
    ///   Place added to one of your trips                    +1  (at most 100 from places)
    ///
    /// The ladder is sent to the apps with the profile, so this is the one place
    /// the rules live.
    /// </summary>
    public static class TravellerRanks
    {
        public const int StoryPoints = 50;
        public const int TripTakenPoints = 30;
        public const int TripPlannedPoints = 20;
        public const int CityPoints = 10;
        public const int LikePoints = 2;
        public const int LikePointsCap = 500;
        public const int PlacePoints = 1;
        public const int PlacePointsCap = 100;

        public record Rank(string Name, int Points, int Stories, int Cities, int TripsTaken);

        public static readonly Rank[] Ladder =
        {
            new("Wanderer", 0, 0, 0, 0),
            new("Explorer", 100, 0, 0, 0),
            new("Pathfinder", 300, 1, 0, 0),
            new("Voyager", 750, 3, 5, 0),
            new("Globetrotter", 1500, 8, 12, 5),
            new("Legend", 3000, 15, 25, 10),
        };

        public record Progress(
            int Points, int Level, string Name,
            int TripsPlanned, int TripsTaken, int Cities, int Stories, int Likes, int Places);

        public static Progress Compute(int tripsPlanned, int tripsTaken, int cities, int stories, int likes, int places)
        {
            var points =
                stories * StoryPoints +
                tripsTaken * TripTakenPoints +
                tripsPlanned * TripPlannedPoints +
                cities * CityPoints +
                Math.Min(likes * LikePoints, LikePointsCap) +
                Math.Min(places * PlacePoints, PlacePointsCap);

            var level = 0;
            for (var i = 0; i < Ladder.Length; i++)
            {
                var r = Ladder[i];
                if (points >= r.Points && stories >= r.Stories && cities >= r.Cities && tripsTaken >= r.TripsTaken)
                    level = i;
                else
                    break; // ranks are earned in order
            }

            return new Progress(points, level, Ladder[level].Name,
                tripsPlanned, tripsTaken, cities, stories, likes, places);
        }
    }
}
