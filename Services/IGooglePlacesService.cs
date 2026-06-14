using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Services
{
    public interface IGooglePlacesService
    {
        Task<List<PlaceDto>> SearchByFilter(string placeId, string filter, CancellationToken cancellationToken = default);
        Task<List<PlaceDto>> SearchByCategory(string placeId, string category, CancellationToken cancellationToken = default);
        Task<PlaceDetailsDto> GetPlaceDetails(string placeId, CancellationToken cancellationToken = default);
    }
}
