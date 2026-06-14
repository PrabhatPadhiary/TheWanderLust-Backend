using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Services
{
    public class GooglePlacesService : IGooglePlacesService
    {
        private readonly HttpClient _httpClient;
        private readonly GooglePlacesSettings _settings;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public GooglePlacesService(HttpClient httpClient, IOptions<GooglePlacesSettings> options, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _settings = options.Value;
            _cache = cache;
        }

        public async Task<List<PlaceDto>> SearchByFilter(string placeId, string filter, CancellationToken cancellationToken = default)        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));
            if (string.IsNullOrWhiteSpace(filter))
                throw new ArgumentException("Filter cannot be null or empty.", nameof(filter));

            var filterCacheKey = $"destinations_filter_{placeId}_{filter.ToLowerInvariant()}";
            if (_cache.TryGetValue(filterCacheKey, out List<PlaceDto> cachedFilterResult))
                return cachedFilterResult;

            // Get the place name from the main search cache, or fetch it
            var mainCacheKey = $"destinations_{placeId}";
            string placeName;
            if (_cache.TryGetValue(mainCacheKey, out PlaceCategoriesResponseDto cachedMain))
            {
                placeName = cachedMain.Name;
            }
            else
            {
                var detailsUrl = $"{_settings.BaseUrl}/details/json?place_id={placeId}&fields=name&key={_settings.ApiKey}";
                var detailsResponse = await _httpClient.GetAsync(detailsUrl, cancellationToken);
                detailsResponse.EnsureSuccessStatusCode();
                var placeDetails = await detailsResponse.Content.ReadFromJsonAsync<GooglePlacesApiResponse>(cancellationToken: cancellationToken);
                placeName = placeDetails?.Result?.Name;
            }

            if (string.IsNullOrWhiteSpace(placeName))
                return new List<PlaceDto>();

            var query = $"top {filter} in {placeName}";
            var results = await TextSearchPlaces(query, cancellationToken);

            _cache.Set(filterCacheKey, results, CacheDuration);
            return results;
        }

        // Supported categories: restaurants | stays | attractions | activities
        private static readonly Dictionary<string, string> CategoryQueryMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["restaurants"] = "best restaurants in {0}",
            ["stays"]        = "best hotels and stays in {0}",
            ["attractions"]  = "top tourist attractions in {0}",
            ["activities"]   = "top things to do in {0}",
        };

        public async Task<List<PlaceDto>> SearchByCategory(string placeId, string category, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));

            if (!CategoryQueryMap.TryGetValue(category?.Trim() ?? "", out var queryTemplate))
                throw new ArgumentException($"Invalid category '{category}'. Valid values: restaurants, stays, attractions, activities.");

            var cacheKey = $"destinations_category_{placeId}_{category.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<PlaceDto> cached))
                return cached;

            var placeName = await ResolvePlaceNameAsync(placeId, cancellationToken);
            if (string.IsNullOrWhiteSpace(placeName))
                return new List<PlaceDto>();

            var query = string.Format(queryTemplate, placeName);
            var results = await TextSearchPlaces(query, cancellationToken);

            _cache.Set(cacheKey, results, CacheDuration);
            return results;
        }

        private async Task<string> ResolvePlaceNameAsync(string placeId, CancellationToken cancellationToken)
        {
            var mainCacheKey = $"destinations_{placeId}";
            if (_cache.TryGetValue(mainCacheKey, out PlaceCategoriesResponseDto cachedMain))
                return cachedMain.Name;

            var detailsUrl = $"{_settings.BaseUrl}/details/json?place_id={placeId}&fields=name&key={_settings.ApiKey}";
            var detailsResponse = await _httpClient.GetAsync(detailsUrl, cancellationToken);
            detailsResponse.EnsureSuccessStatusCode();
            var placeDetails = await detailsResponse.Content.ReadFromJsonAsync<GooglePlacesApiResponse>(cancellationToken: cancellationToken);
            return placeDetails?.Result?.Name;
        }

        private async Task<List<PlaceDto>> TextSearchPlaces(string query, CancellationToken cancellationToken)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_settings.BaseUrl}/textsearch/json?query={encodedQuery}&language=en&key={_settings.ApiKey}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<GoogleTextSearchResponse>(cancellationToken: cancellationToken);
            return MapTextSearchResults(content);
        }

        private List<PlaceDto> MapTextSearchResults(GoogleTextSearchResponse response)
        {
            if (response?.Results == null)
                return new List<PlaceDto>();

            return response.Results
                .Where(r => r.Rating >= 4.0 && r.UserRatingsTotal >= 500)
                .OrderByDescending(r => (r.Rating ?? 0) * Math.Log10((r.UserRatingsTotal ?? 0) + 1))
                .Take(8)
                .Select(r => new PlaceDto
            {
                PlaceId = r.PlaceId,
                Name = r.Name,
                Vicinity = r.FormattedAddress,
                Rating = r.Rating,
                UserRatingsTotal = r.UserRatingsTotal,
                PriceLevel = r.PriceLevel,
                Types = r.Types ?? new List<string>(),
                Geometry = r.Geometry?.Location != null
                    ? new GeometryDto
                    {
                        Latitude = r.Geometry.Location.Lat,
                        Longitude = r.Geometry.Location.Lng
                    }
                    : null,
                Photos = r.Photos?.Take(1).Select(p => new PhotoDto
                {
                    Url = $"{_settings.BaseUrl}/photo?maxwidth=800&photo_reference={p.PhotoReference}&key={_settings.ApiKey}"
                }).ToList() ?? new List<PhotoDto>()
            }).ToList();
        }

        public async Task<PlaceDetailsDto> GetPlaceDetails(string placeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));

            var cacheKey = $"place_details_{placeId}";
            if (_cache.TryGetValue(cacheKey, out PlaceDetailsDto cachedDetails))
                return cachedDetails;

            var fields = "place_id,name,formatted_address,formatted_phone_number,website,rating,user_ratings_total,price_level,geometry,photos,reviews,opening_hours";
            var url = $"{_settings.BaseUrl}/details/json?place_id={placeId}&fields={fields}&language=en&key={_settings.ApiKey}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<GooglePlaceDetailsResponse>(cancellationToken: cancellationToken);
            var dto = MapToPlaceDetailsDto(content);

            _cache.Set(cacheKey, dto, CacheDuration);
            return dto;
        }

        private PlaceDetailsDto MapToPlaceDetailsDto(GooglePlaceDetailsResponse response)
        {
            var result = response?.Result;
            if (result == null)
                return new PlaceDetailsDto();

            return new PlaceDetailsDto
            {
                PlaceId = result.PlaceId,
                Name = result.Name,
                FormattedAddress = result.FormattedAddress,
                FormattedPhoneNumber = result.FormattedPhoneNumber,
                Website = result.Website,
                Rating = result.Rating,
                UserRatingsTotal = result.UserRatingsTotal,
                PriceLevel = result.PriceLevel,
                Geometry = result.Geometry?.Location != null
                    ? new GeometryDto
                    {
                        Latitude = result.Geometry.Location.Lat,
                        Longitude = result.Geometry.Location.Lng
                    }
                    : null,
                Photos = result.Photos?.Take(5).Select(p => new PhotoDto
                {
                    Url = $"{_settings.BaseUrl}/photo?maxwidth=800&photo_reference={p.PhotoReference}&key={_settings.ApiKey}"
                }).ToList() ?? new List<PhotoDto>(),
                Reviews = result.Reviews?.Select(r => new ReviewDto
                {
                    AuthorName = r.AuthorName,
                    ProfilePhotoUrl = r.ProfilePhotoUrl,
                    Rating = r.Rating,
                    Text = r.Text,
                    RelativeTimeDescription = r.RelativeTimeDescription
                }).ToList() ?? new List<ReviewDto>(),
                OpeningHours = result.OpeningHours != null
                    ? new OpeningHoursDto
                    {
                        OpenNow = result.OpeningHours.OpenNow,
                        WeekdayText = result.OpeningHours.WeekdayText ?? new List<string>()
                    }
                    : null
            };
        }
    }
}
