using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using TheWanderLustWebAPI.Services;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DestinationsController : ControllerBase
    {
        private readonly IGooglePlacesService _placesService;
        private readonly IPexelsService _pexelsService;
        private readonly IMemoryCache _cache;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public DestinationsController(
            IGooglePlacesService placesService,
            IPexelsService pexelsService,
            IMemoryCache cache,
            IHttpClientFactory httpClientFactory,
            IOptions<GooglePlacesSettings> placesSettings)
        {
            _placesService = placesService;
            _pexelsService = pexelsService;
            _cache = cache;
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = placesSettings.Value.ApiKey;
        }

        /// <summary>
        /// Autocomplete search for destinations. Proxies Google Places Autocomplete API.
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return BadRequest("Query must be at least 2 characters.");

            var cacheKey = $"autocomplete_{query.ToLowerInvariant().Trim()}";
            if (_cache.TryGetValue(cacheKey, out object cachedResult))
                return Ok(cachedResult);

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(query)}&types=(regions)&key={_apiKey}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadFromJsonAsync<GoogleAutocompleteResponse>(cancellationToken: cancellationToken);

                var results = data?.Predictions?.Select(p => new
                {
                    placeId = p.PlaceId,
                    name = p.StructuredFormatting?.MainText ?? p.Description,
                    description = p.Description,
                    secondary = p.StructuredFormatting?.SecondaryText ?? ""
                }).ToList() ?? new List<object>();

                _cache.Set(cacheKey, results, TimeSpan.FromHours(1));

                return Ok(results);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve autocomplete results from Google Places API.");
            }
        }

        [HttpGet("places")]
        public async Task<IActionResult> GetByCategory([FromQuery] string placeId, [FromQuery] string category, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");
            if (string.IsNullOrWhiteSpace(category))
                return BadRequest("category query parameter is required. Valid values: restaurants, stays, attractions, activities.");

            try
            {
                var cacheKey = $"destinations_category_{placeId}_{category.ToLowerInvariant()}";
                var fromCache = _cache.TryGetValue(cacheKey, out _);

                var result = await _placesService.SearchByCategory(placeId, category, cancellationToken);

                Response.Headers["X-Data-Source"] = fromCache ? "cache" : "api";
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve data from Google Places API.");
            }
        }

        [HttpGet("filter")]
        public async Task<IActionResult> Filter([FromQuery] string placeId, [FromQuery] string filter, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");
            if (string.IsNullOrWhiteSpace(filter))
                return BadRequest("filter query parameter is required.");

            try
            {
                var filterCacheKey = $"destinations_filter_{placeId}_{filter.ToLowerInvariant()}";
                var fromCache = _cache.TryGetValue(filterCacheKey, out _);

                var result = await _placesService.SearchByFilter(placeId, filter, cancellationToken);

                Response.Headers["X-Data-Source"] = fromCache ? "cache" : "api";
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve data from Google Places API.");
            }
        }

        [HttpGet("details")]
        public async Task<IActionResult> Details([FromQuery] string placeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");

            try
            {
                var cacheKey = $"place_details_{placeId}";
                var fromCache = _cache.TryGetValue(cacheKey, out _);

                var result = await _placesService.GetPlaceDetails(placeId, cancellationToken);

                Response.Headers["X-Data-Source"] = fromCache ? "cache" : "api";
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve data from Google Places API.");
            }
        }
    }

    // Models for Google Places Autocomplete response
    public class GoogleAutocompleteResponse
    {
        [JsonPropertyName("predictions")]
        public List<AutocompletePrediction> Predictions { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    public class AutocompletePrediction
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("structured_formatting")]
        public StructuredFormatting StructuredFormatting { get; set; }
    }

    public class StructuredFormatting
    {
        [JsonPropertyName("main_text")]
        public string MainText { get; set; }

        [JsonPropertyName("secondary_text")]
        public string SecondaryText { get; set; }
    }
}
