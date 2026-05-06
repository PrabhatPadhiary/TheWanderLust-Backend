using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Services
{
    public interface IGooglePlacesService
    {
        Task<PlaceCategoriesResponseDto> GetAllCategories(string placeId);
    }
}
