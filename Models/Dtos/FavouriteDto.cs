namespace TheWanderLustWebAPI.Models.Dtos
{
    public class AddFavouriteDto
    {
        public string PlaceId { get; set; }
        public string PlaceName { get; set; }
        public string Vicinity { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingsTotal { get; set; }
        public string PhotoUrl { get; set; }
        public string Category { get; set; }
    }

    public class SyncFavouritesDto
    {
        public List<AddFavouriteDto> Favourites { get; set; } = new();
    }
}
