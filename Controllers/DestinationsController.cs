using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using TheWanderLustWebAPI.Services;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // Every endpoint here can call Google Places (paid): rate-limited per IP.
    // Photo endpoints use a looser limit (see their attributes).
    [EnableRateLimiting(RateLimits.Google)]
    public class DestinationsController : ControllerBase
    {
        private readonly IGooglePlacesService _placesService;
        private readonly IMemoryCache _cache;
        private readonly PlacePhotoService _placePhotos;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public DestinationsController(
            IGooglePlacesService placesService,
            IMemoryCache cache,
            PlacePhotoService placePhotos,
            IHttpClientFactory httpClientFactory,
            IOptions<GooglePlacesSettings> placesSettings)
        {
            _placesService = placesService;
            _cache = cache;
            _placePhotos = placePhotos;
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = placesSettings.Value.ApiKey;
        }

        /// <summary>
        /// Autocomplete search for destinations. Proxies Google Places Autocomplete API.
        /// </summary>
        /// <summary>
        /// Place autocomplete. By default only regions (cities, areas, countries);
        /// scope=all also returns specific places such as restaurants, hotels and
        /// landmarks (used when tagging places in a journal). Each result carries
        /// Google's place types so clients can tell a city from a business.
        /// lat + lng (+ radius in metres, default 50 km) keep results inside that
        /// circle, e.g. only places in the journal's destination city.
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string query,
            [FromQuery] string? scope,
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] int? radius,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return BadRequest("Query must be at least 2 characters.");

            var allPlaces = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var near = lat.HasValue && lng.HasValue
                && lat.Value is >= -90 and <= 90 && lng.Value is >= -180 and <= 180;
            var radiusMetres = Math.Clamp(radius ?? 50000, 1000, 500000);
            var nearKey = near ? $"_{lat!.Value:F3},{lng!.Value:F3},{radiusMetres}" : "";
            var cacheKey = $"autocomplete_{(allPlaces ? "all" : "regions")}{nearKey}_{query.ToLowerInvariant().Trim()}";
            if (_cache.TryGetValue(cacheKey, out object cachedResult))
                return Ok(cachedResult);

            try
            {
                var typesFilter = allPlaces ? "" : "&types=(regions)";
                var areaFilter = near
                    ? FormattableString.Invariant($"&location={lat!.Value},{lng!.Value}&radius={radiusMetres}&strictbounds=true")
                    : "";
                var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(query)}{typesFilter}{areaFilter}&key={_apiKey}";
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
                        secondary = p.StructuredFormatting?.SecondaryText ?? "",
                        types = p.Types ?? new List<string>()
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

        // Google place types the "Near you" list can ask for.
        private static readonly HashSet<string> NearbyTypes = new(StringComparer.Ordinal)
        {
            "tourist_attraction", "restaurant", "cafe", "park", "museum", "bar", "shopping_mall",
        };

        /// <summary>
        /// Get places near a latitude and longitude: tourist attractions by
        /// default, or one of a few other Google place types (food, cafés, ...).
        /// <paramref name="limit"/> caps the count (Google returns at most 20).
        /// </summary>
        [HttpGet("nearby")]
        public async Task<IActionResult> Nearby(
            [FromQuery] double lat,
            [FromQuery] double lng,
            [FromQuery] int radius = 30000,
            [FromQuery] string type = "tourist_attraction",
            [FromQuery] int limit = 10,
            CancellationToken cancellationToken = default)
        {
            if (lat == 0 && lng == 0)
                return BadRequest("Valid latitude and longitude are required.");
            if (!NearbyTypes.Contains(type))
                return BadRequest("Unsupported place type.");
            radius = Math.Clamp(radius, 500, 50000);
            limit = Math.Clamp(limit, 1, 20);

            var cacheKey = $"nearby_{lat:F2}_{lng:F2}_{radius}_{type}_{limit}";
            if (_cache.TryGetValue(cacheKey, out object cachedResult))
                return Ok(cachedResult);

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/place/nearbysearch/json?location={lat},{lng}&radius={radius}&type={type}&key={_apiKey}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var results = new List<object>();
                if (root.TryGetProperty("results", out var resultsArray))
                {
                    foreach (var place in resultsArray.EnumerateArray().Take(limit))
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
                            userRatingsTotal = place.TryGetProperty("user_ratings_total", out var urt) ? urt.GetInt32() : 0,
                            openNow = place.TryGetProperty("opening_hours", out var oh) && oh.TryGetProperty("open_now", out var on)
                                ? on.GetBoolean() : (bool?)null,
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
        /// Get a place's main photo straight from its Google place id, so the
        /// client needs no details lookup first. Cached on the server and sent
        /// with Cache-Control so the phone can keep it too.
        /// 404 when the place has no photo; 502 when Google can't be reached.
        /// </summary>
        [HttpGet("place-photo")]
        [EnableRateLimiting(RateLimits.Photos)]
        public async Task<IActionResult> PlacePhoto(
            [FromQuery] string placeId,
            [FromQuery] int maxWidth = 800,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId) || placeId.Length > 512)
                return BadRequest("placeId query parameter is required.");

            try
            {
                var photo = await _placePhotos.GetPhotoAsync(placeId.Trim(), maxWidth, cancellationToken);
                if (photo == null)
                {
                    Response.Headers.CacheControl = "no-store";
                    return NotFound();
                }

                // Matches the server cache lifetime (see PlacePhotoService).
                Response.Headers.CacheControl = "public, max-age=86400";
                return File(photo.Bytes, photo.ContentType);
            }
            catch (HttpRequestException)
            {
                Response.Headers.CacheControl = "no-store";
                return StatusCode(502, "Failed to retrieve photo from Google Places API.");
            }
        }

        /// <summary>
        /// Get a place photo by reference. Proxies Google Places Photo API.
        /// </summary>
        [HttpGet("photo")]
        [EnableRateLimiting(RateLimits.Photos)]
        public async Task<IActionResult> Photo(
            [FromQuery] string reference,
            [FromQuery(Name = "photo_reference")] string photoReference,
            [FromQuery] int maxWidth = 400,
            CancellationToken cancellationToken = default)
        {
            // Links we hand out use photo_reference (see PlacePhotoLinks);
            // older ones use reference.
            reference = string.IsNullOrWhiteSpace(reference) ? photoReference : reference;
            if (string.IsNullOrWhiteSpace(reference))
                return BadRequest("Photo reference is required.");
            maxWidth = Math.Clamp(maxWidth, 50, 1600);

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth={maxWidth}&photo_reference={Uri.EscapeDataString(reference)}&key={_apiKey}";
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

        /// <summary>
        /// Get themed destinations for a home-screen category (beaches, mountains,
        /// cities, historical, islands, wildlife). When lat/lng are supplied:
        /// real places of that kind near the user, nearest first, each with its
        /// distance (`near` = the user's city name, for the search). Otherwise
        /// a global search.
        /// </summary>
        [HttpGet("by-category")]
        public async Task<IActionResult> ByCategory([FromQuery] string theme, [FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] string near, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(theme))
                return BadRequest("theme query parameter is required.");

            try
            {
                var biased = lat.HasValue && lng.HasValue && !(lat == 0 && lng == 0);
                if (biased)
                    return Ok(await _placesService.SearchThemeNearby(theme, lat.Value, lng.Value, near, cancellationToken));

                var cacheKey = biased
                    ? $"destinations_theme_{theme.ToLowerInvariant()}_{lat:F1}_{lng:F1}"
                    : $"destinations_theme_{theme.ToLowerInvariant()}_global";
                var fromCache = _cache.TryGetValue(cacheKey, out _);

                var result = await _placesService.SearchByTheme(theme, lat, lng, cancellationToken);

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

        /// <summary>
        /// Paged places inside a destination, for "See all" and "Search in X".
        /// Give one of: query (free text, e.g. "biryani"), filter (e.g. "Cafes")
        /// or category (restaurants | stays | attractions | activities).
        /// page 0, 1 or 2 (20 a page from Google, up to 60 in all).
        /// </summary>
        [HttpGet("places/search")]
        public async Task<IActionResult> SearchInDestination(
            [FromQuery] string placeId,
            [FromQuery] string? category,
            [FromQuery] string? filter,
            [FromQuery] string? query,
            [FromQuery] int page,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");
            if (!string.IsNullOrWhiteSpace(query) && query.Trim().Length < 2)
                return BadRequest("Query must be at least 2 characters.");

            try
            {
                var result = await _placesService.SearchInDestination(placeId, category, filter, query, page, cancellationToken);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve places from Google Places API.");
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

        [JsonPropertyName("types")]
        public List<string>? Types { get; set; }
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
