using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Services
{
    public interface IGooglePlacesService
    {
        Task<PlaceCategoriesResponseDto> GetAllCategories(string placeId, CancellationToken cancellationToken = default);
        Task<List<PlaceDto>> SearchByFilter(string placeId, string filter, CancellationToken cancellationToken = default);
        Task<PlaceDetailsDto> GetPlaceDetails(string placeId, CancellationToken cancellationToken = default);
    }
}
