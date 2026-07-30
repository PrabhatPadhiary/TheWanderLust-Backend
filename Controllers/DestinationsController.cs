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

                object results;
                var predictions = data?.Predictions;
                if (predictions != null)
                {
                    results = predictions.Select(p => new
                    {
                        placeId = p.PlaceId,
                        name = p.StructuredFormatting?.MainText ?? p.Description,
                        description = p.Description,
                        secondary = p.StructuredFormatting?.SecondaryText ?? ""
                    }).ToList();
                }
                else
                {
                    results = new List<object>();
                }

                _cache.Set(cacheKey, results, TimeSpan.FromHours(1));

                return Ok(results);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve autocomplete results from Google Places API.");
            }
        }

        /// <summary>
        /// Get nearby tourist attractions based on latitude and longitude.
        /// </summary>
        [HttpGet("nearby")]
        public async Task<IActionResult> Nearby([FromQuery] double lat, [FromQuery] double lng, [FromQuery] int radius = 30000, CancellationToken cancellationToken = default)
        {
            if (lat == 0 && lng == 0)
                return BadRequest("Valid latitude and longitude are required.");

            var cacheKey = $"nearby_{lat:F2}_{lng:F2}_{radius}";
            if (_cache.TryGetValue(cacheKey, out object cachedResult))
                return Ok(cachedResult);

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json?location={lat},{lng}&radius={radius}&type=tourist_attraction&key={_apiKey}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var results = new List<object>();
                if (root.TryGetProperty("results", out var resultsArray))
                {
                    foreach (var place in resultsArray.EnumerateArray().Take(10))
                    {
                        var photoRef = "";
                        if (place.TryGetProperty("photos", out var photos) && photos.GetArrayLength() > 0)
                            photoRef = photos[0].GetProperty("photo_reference").GetString();

                        double placeLat = 0, placeLng = 0;
                        if (place.TryGetProperty("geometry", out var geo) && geo.TryGetProperty("location", out var loc))
                        {
                            placeLat = loc.GetProperty("lat").GetDouble();
                            placeLng = loc.GetProperty("lng").GetDouble();
                        }

                        results.Add(new
                        {
                            placeId = place.GetProperty("place_id").GetString(),
                            name = place.GetProperty("name").GetString(),
                            vicinity = place.TryGetProperty("vicinity", out var v) ? v.GetString() : "",
                            rating = place.TryGetProperty("rating", out var r) ? r.GetDouble() : 0,
                            photoReference = photoRef,
                            lat = placeLat,
                            lng = placeLng
                        });
                    }
                }

                _cache.Set(cacheKey, results, TimeSpan.FromHours(2));
                return Ok(results);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve nearby places from Google Places API.");
            }
        }

        /// <summary>
        /// Get a place photo by reference. Proxies Google Places Photo API.
        /// </summary>
        [HttpGet("photo")]
        public async Task<IActionResult> Photo([FromQuery] string reference, [FromQuery] int maxWidth = 400, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Photo reference is required.");

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth={maxWidth}&photo_reference={reference}&key={_apiKey}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                return File(imageBytes, contentType);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve photo from Google Places API.");
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

    // Models for Google Places Nearby Search response
    public class GoogleNearbyResponse
    {
        [JsonPropertyName("results")]
        public List<NearbyPlace> Results { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    public class NearbyPlace
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("vicinity")]
        public string Vicinity { get; set; }

        [JsonPropertyName("rating")]
        public double? Rating { get; set; }

        [JsonPropertyName("geometry")]
        public NearbyGeometry Geometry { get; set; }

        [JsonPropertyName("photos")]
        public List<NearbyPhoto> Photos { get; set; }
    }

    public class NearbyGeometry
    {
        [JsonPropertyName("location")]
        public NearbyLatLng Location { get; set; }
    }

    public class NearbyLatLng
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }

    public class NearbyPhoto
    {
        [JsonPropertyName("photo_reference")]
        public string PhotoReference { get; set; }
    }
}
