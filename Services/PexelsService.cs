using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Services
{
    public class PexelsService : IPexelsService
    {
        private readonly HttpClient _httpClient;
        private readonly PexelsSettings _pexelsSettings;
        private readonly GooglePlacesSettings _placesSettings;
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

        public PexelsService(
            HttpClient httpClient,
            IOptions<PexelsSettings> pexelsOptions,
            IOptions<GooglePlacesSettings> placesOptions,
            IMemoryCache cache)
        {
            _httpClient = httpClient;
            _pexelsSettings = pexelsOptions.Value;
            _placesSettings = placesOptions.Value;
            _cache = cache;
        }

        public async Task<List<string>> GetHeroImagesAsync(string placeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));

            var cacheKey = $"pexels_hero_{placeId}";
            if (_cache.TryGetValue(cacheKey, out List<string> cachedUrls))
                return cachedUrls;

            // Resolve place name from main search cache or fetch from Google
            var destination = await GetPlaceName(placeId, cancellationToken);
            if (string.IsNullOrWhiteSpace(destination))
                return new List<string>();

            var query = Uri.EscapeDataString($"{destination} scenic landscape");

            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_pexelsSettings.BaseUrl}/search?query={query}&per_page=3&orientation=landscape");

            request.Headers.Add("Authorization", _pexelsSettings.ApiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<PexelsResponse>(cancellationToken: cancellationToken);

            var imageUrls = content?.Photos?
                .Where(p => p.Src?.Landscape != null)
                .Select(p => p.Src.Landscape)
                .ToList() ?? new List<string>();

            if (imageUrls.Count > 0)
                _cache.Set(cacheKey, imageUrls, CacheDuration);

            return imageUrls;
        }

        private async Task<string> GetPlaceName(string placeId, CancellationToken cancellationToken)
        {
            // Try to get from the main destinations cache first
            var mainCacheKey = $"destinations_{placeId}";
            if (_cache.TryGetValue(mainCacheKey, out PlaceCategoriesResponseDto cachedMain))
                return cachedMain.Name;

            // Otherwise fetch just the name from Google
            var detailsUrl = $"{_placesSettings.BaseUrl}/details/json?place_id={placeId}&fields=name&key={_placesSettings.ApiKey}";
            var detailsResponse = await _httpClient.GetAsync(detailsUrl, cancellationToken);
            detailsResponse.EnsureSuccessStatusCode();

            var placeDetails = await detailsResponse.Content.ReadFromJsonAsync<GooglePlacesApiResponse>(cancellationToken: cancellationToken);
            return placeDetails?.Result?.Name;
        }
    }
}
