using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Services
{
    public interface IGooglePlacesService
    {
        Task<List<PlaceDto>> SearchByFilter(string placeId, string filter, CancellationToken cancellationToken = default);
        Task<List<PlaceDto>> SearchByCategory(string placeId, string category, CancellationToken cancellationToken = default);
        Task<List<PlaceDto>> SearchByTheme(string theme, double? lat = null, double? lng = null, CancellationToken cancellationToken = default);
        Task<List<PlaceDto>> SearchThemeNearby(string theme, double lat, double lng, string near, CancellationToken cancellationToken = default);
        Task<PlaceDetailsDto> GetPlaceDetails(string placeId, CancellationToken cancellationToken = default);
        Task<PagedPlacesDto> SearchInDestination(string placeId, string? category, string? filter, string? query, int page, CancellationToken cancellationToken = default);
    }
}
