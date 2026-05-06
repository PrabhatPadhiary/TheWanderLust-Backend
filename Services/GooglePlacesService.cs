using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Services
{
    public class GooglePlacesService : IGooglePlacesService
    {
        private readonly HttpClient _httpClient;
        private readonly GooglePlacesSettings _settings;

        public GooglePlacesService(HttpClient httpClient, IOptions<GooglePlacesSettings> options)
        {
            _httpClient = httpClient;
            _settings = options.Value;
        }

        public async Task<PlaceCategoriesResponseDto> GetAllCategories(string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));

            // 1. Get place details (only the fields we need)
            var detailsUrl = $"{_settings.BaseUrl}/details/json?place_id={placeId}&fields=place_id,name,formatted_address,geometry&key={_settings.ApiKey}";
            var detailsResponse = await _httpClient.GetAsync(detailsUrl);
            detailsResponse.EnsureSuccessStatusCode();

            var placeDetails = await detailsResponse.Content.ReadFromJsonAsync<GooglePlacesApiResponse>();
            var dto = MapToDto(placeDetails);

            // 2. If we have coordinates, make 3 nearby search calls in parallel
            if (dto.Geometry != null)
            {
                var lat = dto.Geometry.Latitude;
                var lng = dto.Geometry.Longitude;

                var restaurantsTask = GetNearbyPlaces(lat, lng, "restaurant");
                var lodgingTask = GetNearbyPlaces(lat, lng, "lodging");
                var attractionsTask = GetNearbyPlaces(lat, lng, "tourist_attraction");

                await Task.WhenAll(restaurantsTask, lodgingTask, attractionsTask);

                dto.Restaurants = restaurantsTask.Result;
                dto.Lodging = lodgingTask.Result;
                dto.TouristAttractions = attractionsTask.Result;
            }

            return dto;
        }

        private async Task<List<NearbyPlaceDto>> GetNearbyPlaces(double latitude, double longitude, string type)
        {
            var url = $"{_settings.BaseUrl}/nearbysearch/json?location={latitude},{longitude}&radius=5000&type={type}&key={_settings.ApiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<GoogleNearbySearchResponse>();
            return MapNearbyResults(content);
        }

        private List<NearbyPlaceDto> MapNearbyResults(GoogleNearbySearchResponse response)
        {
            if (response?.Results == null)
                return new List<NearbyPlaceDto>();

            return response.Results.Select(r => new NearbyPlaceDto
            {
                PlaceId = r.PlaceId,
                Name = r.Name,
                Vicinity = r.Vicinity,
                Rating = r.Rating,
                Types = r.Types ?? new List<string>(),
                Geometry = r.Geometry?.Location != null
                    ? new GeometryDto
                    {
                        Latitude = r.Geometry.Location.Lat,
                        Longitude = r.Geometry.Location.Lng
                    }
                    : null,
                Photos = r.Photos?.Select(p => new PhotoDto
                {
                    PhotoReference = p.PhotoReference,
                    Width = p.Width,
                    Height = p.Height
                }).ToList() ?? new List<PhotoDto>()
            }).ToList();
        }

        private PlaceCategoriesResponseDto MapToDto(GooglePlacesApiResponse apiResponse)
        {
            var result = apiResponse?.Result;
            if (result == null)
            {
                return new PlaceCategoriesResponseDto();
            }

            return new PlaceCategoriesResponseDto
            {
                PlaceId = result.PlaceId,
                Name = result.Name,
                FormattedAddress = result.FormattedAddress,
                Geometry = result.Geometry?.Location != null
                    ? new GeometryDto
                    {
                        Latitude = result.Geometry.Location.Lat,
                        Longitude = result.Geometry.Location.Lng
                    }
                    : null
            };
        }
    }
}
