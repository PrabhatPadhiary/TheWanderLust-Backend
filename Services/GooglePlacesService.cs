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

        public GooglePlacesService(HttpClient httpClient, IOptions<GooglePlacesSettings> options, IMemoryCache cache, IHttpContextAccessor httpContextAccessor)
        {
            _httpClient = httpClient;
            _settings = options.Value;
            _cache = cache;
            _httpContextAccessor = httpContextAccessor;
        }

        private readonly IHttpContextAccessor _httpContextAccessor;

        // Photo link for clients: our proxy (never Google's URL, which needs
        // the API key). Built from the current request's host.
        private string PhotoLink(string reference, int maxWidth = 800)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            var baseUrl = request != null ? PlacePhotoLinks.BaseUrl(request) : "";
            return PlacePhotoLinks.Proxy(baseUrl, reference, maxWidth);
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

        // Supported home-screen themes. Queries are intentionally broad so Text
        // Search returns inspirational destinations rather than single POIs.
        private static readonly Dictionary<string, string> ThemeQueryMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["beaches"]    = "best beach destinations",
            ["mountains"]  = "best mountain destinations",
            ["cities"]     = "best cities to visit",
            ["historical"] = "best historical places to visit",
            ["islands"]    = "best island destinations",
            ["wildlife"]   = "best wildlife and safari destinations",
        };

        // ── "Near you" for a home-screen category ─────────────────────────────
        // Real places of that kind around the user, nearest first: "<kind> to
        // visit from <city>" through Text Search, then filtered hard — wrong
        // types (travel agents, resorts, restaurants called "... Beach"), names
        // that don't match the kind, little-known spots and anything too far
        // are dropped, and near-duplicates (the same hill listed twice) merged.
        // Cities have no "near you" list: you're already in one.
        private record NearbyTheme(
            string Query,
            int MaxKm,
            int MinReviews,
            string[] Kinds,
            System.Text.RegularExpressions.Regex NameMustMatch = null,
            string[] ExtraExcludedTypes = null);

        private static System.Text.RegularExpressions.Regex Rx(string pattern) =>
            new(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly Dictionary<string, NearbyTheme> NearbyThemeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["beaches"] = new("best beaches to visit from {0}", 800, 200,
                new[] { "natural_feature", "tourist_attraction", "park" },
                Rx(@"\bbeach")),
            ["mountains"] = new("hill stations to visit from {0}", 400, 500,
                new[] { "natural_feature", "tourist_attraction", "park" }),
            ["historical"] = new("historical places to visit from {0}", 400, 1000,
                new[] { "tourist_attraction", "museum", "place_of_worship", "hindu_temple", "church", "mosque" },
                ExtraExcludedTypes: new[] { "park", "amusement_park", "aquarium" }),
            ["islands"] = new("islands to visit from {0}", 1200, 300,
                new[] { "natural_feature", "tourist_attraction", "locality", "colloquial_area", "archipelago" },
                Rx(@"\b(island|islands|isle|dweep|dvip)\b")),
            ["wildlife"] = new("national parks to visit from {0}", 500, 200,
                new[] { "park", "zoo", "tourist_attraction", "natural_feature" },
                Rx(@"national park|tiger|reserve|sanctuary|safari|forest|wildlife")),
        };

        // Types that mark a business rather than a place to go.
        private static readonly HashSet<string> BusinessTypes = new(StringComparer.Ordinal)
        {
            "travel_agency", "lodging", "store", "real_estate_agency", "restaurant", "food", "cafe", "bar",
            "car_rental", "gym", "school", "university", "health", "finance", "general_contractor",
            "moving_company", "car_dealer", "shopping_mall", "clothing_store", "spa", "night_club",
            "insurance_agency", "local_government_office", "campground", "rv_park", "storage",
            "veterinary_care", "transit_station",
        };

        // Names that give away a business or a charity, whatever its types say.
        private static readonly System.Text.RegularExpressions.Regex BusinessName =
            Rx(@"\b(pet|rescue|rehab\w*|ngo|foundation|trip|trips|tour|tours|travels?|holidays?|adventures?|trek\w*|resorts?|stays?|homestay|camps?)\b");

        public async Task<List<PlaceDto>> SearchThemeNearby(string theme, double lat, double lng, string near, CancellationToken cancellationToken = default)
        {
            if (!NearbyThemeMap.TryGetValue(theme?.Trim() ?? "", out var cfg))
                throw new ArgumentException($"No nearby list for theme '{theme}'. Valid values: {string.Join(", ", NearbyThemeMap.Keys)}.");

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var place = string.IsNullOrWhiteSpace(near)
                ? $"{lat.ToString("F3", inv)},{lng.ToString("F3", inv)}"
                : near.Trim();
            if (place.Length > 80) place = place[..80];

            var cacheKey = $"destinations_theme_near_{theme.ToLowerInvariant()}_{lat:F1}_{lng:F1}";
            if (_cache.TryGetValue(cacheKey, out List<PlaceDto> cached))
                return cached;

            // No location bias: it pulls in small local matches; "from <city>"
            // in the query already anchors the search.
            var query = Uri.EscapeDataString(string.Format(cfg.Query, place));
            var url = $"{_settings.BaseUrl}/textsearch/json?query={query}&language=en&key={_settings.ApiKey}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadFromJsonAsync<GoogleTextSearchResponse>(cancellationToken: cancellationToken);

            var candidates = new List<(PlaceDto Dto, int Reviews)>();
            foreach (var r in content?.Results ?? new List<GoogleTextSearchResult>())
            {
                if (r.Geometry?.Location == null || string.IsNullOrWhiteSpace(r.Name)) continue;
                var types = r.Types ?? new List<string>();
                if (types.Any(BusinessTypes.Contains)) continue;
                if (cfg.ExtraExcludedTypes != null && types.Any(cfg.ExtraExcludedTypes.Contains)) continue;
                if (!types.Any(cfg.Kinds.Contains)) continue;
                if (BusinessName.IsMatch(r.Name)) continue;
                if (cfg.NameMustMatch != null && !cfg.NameMustMatch.IsMatch(r.Name)) continue;
                var reviews = r.UserRatingsTotal ?? 0;
                if (reviews < cfg.MinReviews || (r.Rating ?? 0) < 3.8) continue;

                var dto = ToPlaceDto(r);
                dto.DistanceKm = Math.Round(HaversineKm(lat, lng, r.Geometry.Location.Lat, r.Geometry.Location.Lng), 1);
                candidates.Add((dto, reviews));
            }

            // Merge near-duplicates (within 2 km): keep the better-known one.
            var kept = new List<(PlaceDto Dto, int Reviews)>();
            foreach (var c in candidates.OrderByDescending(c => c.Reviews))
            {
                var g = c.Dto.Geometry;
                if (kept.Any(k => HaversineKm(k.Dto.Geometry.Latitude, k.Dto.Geometry.Longitude, g.Latitude, g.Longitude) < 2))
                    continue;
                kept.Add(c);
            }

            var ordered = kept.Select(k => k.Dto).OrderBy(p => p.DistanceKm).ToList();
            // Within reach first; if nothing is, the few nearest anyway (the app
            // says how far they are).
            var within = ordered.Where(p => p.DistanceKm <= cfg.MaxKm).Take(12).ToList();
            var results = within.Count > 0 ? within : ordered.Take(3).ToList();

            _cache.Set(cacheKey, results, CacheDuration);
            return results;
        }

        private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371;
            double Rad(double d) => d * Math.PI / 180;
            var dLat = Rad(lat2 - lat1);
            var dLng = Rad(lng2 - lng1);
            var h = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
            return 2 * R * Math.Asin(Math.Sqrt(h));
        }

        public async Task<List<PlaceDto>> SearchByTheme(string theme, double? lat = null, double? lng = null, CancellationToken cancellationToken = default)
        {
            if (!ThemeQueryMap.TryGetValue(theme?.Trim() ?? "", out var query))
                throw new ArgumentException($"Invalid theme '{theme}'. Valid values: {string.Join(", ", ThemeQueryMap.Keys)}.");

            var hasBias = lat.HasValue && lng.HasValue && !(lat == 0 && lng == 0);
            // Separate cache entries for global vs. location-biased results.
            var cacheKey = hasBias
                ? $"destinations_theme_{theme.ToLowerInvariant()}_{lat:F1}_{lng:F1}"
                : $"destinations_theme_{theme.ToLowerInvariant()}_global";
            if (_cache.TryGetValue(cacheKey, out List<PlaceDto> cached))
                return cached;

            var results = await TextSearchPlaces(query, cancellationToken, hasBias ? lat : null, hasBias ? lng : null);

            _cache.Set(cacheKey, results, CacheDuration);
            return results;
        }

        private async Task<List<PlaceDto>> TextSearchPlaces(string query, CancellationToken cancellationToken, double? lat = null, double? lng = null)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"{_settings.BaseUrl}/textsearch/json?query={encodedQuery}&language=en&key={_settings.ApiKey}";

            // Optional location bias: Text Search still returns strong results
            // beyond the radius, so this nudges toward the user's region without
            // producing an empty list the way strict Nearby Search would.
            if (lat.HasValue && lng.HasValue)
            {
                var latStr = lat.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lngStr = lng.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                url += $"&location={latStr},{lngStr}&radius=50000";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<GoogleTextSearchResponse>(cancellationToken: cancellationToken);
            return MapTextSearchResults(content);
        }

        // Curated lists (the destination page preview): well-known places only.
        private List<PlaceDto> MapTextSearchResults(GoogleTextSearchResponse response)
        {
            if (response?.Results == null)
                return new List<PlaceDto>();

            return response.Results
                .Where(r => r.Rating >= 4.0 && r.UserRatingsTotal >= 500)
                .OrderByDescending(PlaceScore)
                .Take(8)
                .Select(ToPlaceDto)
                .ToList();
        }

        // Rating weighted by how many people rated it.
        private static double PlaceScore(GoogleTextSearchResult r) =>
            (r.Rating ?? 0) * Math.Log10((r.UserRatingsTotal ?? 0) + 1);

        private PlaceDto ToPlaceDto(GoogleTextSearchResult r) => new PlaceDto
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
                Url = PhotoLink(p.PhotoReference)
            }).ToList() ?? new List<PhotoDto>()
        };

        // ── Paged search inside a destination ("See all", "Search in X") ──
        // Google Text Search returns 20 results a page and at most 3 pages (60).
        // Each page is cached for the usual duration; the token for the next
        // page is short-lived (Google expires them), so it is kept briefly and
        // re-fetched from the previous page when missing.
        private const int MaxPages = 3;
        private static readonly TimeSpan PageTokenLifetime = TimeSpan.FromMinutes(2);

        public async Task<PagedPlacesDto> SearchInDestination(
            string placeId, string? category, string? filter, string? query, int page,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                throw new ArgumentException("Place ID cannot be null or empty.", nameof(placeId));
            page = Math.Clamp(page, 0, MaxPages - 1);

            var placeName = await ResolvePlaceNameAsync(placeId, cancellationToken);
            if (string.IsNullOrWhiteSpace(placeName))
                return new PagedPlacesDto { Page = page };

            string text;
            if (!string.IsNullOrWhiteSpace(query))
                text = $"{query.Trim()} in {placeName}";
            else if (!string.IsNullOrWhiteSpace(filter))
                text = $"top {filter.Trim()} in {placeName}";
            else if (CategoryQueryMap.TryGetValue(category?.Trim() ?? "", out var template))
                text = string.Format(template, placeName);
            else
                throw new ArgumentException("Give a query, a filter or a category (restaurants, stays, attractions, activities).");

            var key = $"destinations_paged_{placeId}_{text.ToLowerInvariant()}";
            if (_cache.TryGetValue($"{key}_p{page}", out PagedPlacesDto cachedPage))
                return cachedPage;

            var response = await FetchTextSearchPage(text, key, page, cancellationToken);
            var result = new PagedPlacesDto
            {
                Page = page,
                // Light quality bar: hide unrated and barely-reviewed places,
                // but keep smaller, newer ones the curated lists leave out.
                Places = (response?.Results ?? new List<GoogleTextSearchResult>())
                    .Where(r => (r.Rating ?? 0) >= 3.8 && (r.UserRatingsTotal ?? 0) >= 20)
                    .OrderByDescending(PlaceScore)
                    .Select(ToPlaceDto)
                    .ToList(),
                HasMore = page < MaxPages - 1 && !string.IsNullOrEmpty(response?.NextPageToken),
            };

            _cache.Set($"{key}_p{page}", result, CacheDuration);
            if (!string.IsNullOrEmpty(response?.NextPageToken))
                _cache.Set($"{key}_t{page + 1}", response.NextPageToken, PageTokenLifetime);
            return result;
        }

        // Raw page `page` of a text search. Pages after the first need the
        // token from the page before, so a missing token re-fetches that page.
        private async Task<GoogleTextSearchResponse> FetchTextSearchPage(string text, string key, int page, CancellationToken cancellationToken)
        {
            string token = null;
            if (page > 0 && !_cache.TryGetValue($"{key}_t{page}", out token))
            {
                var previous = await FetchTextSearchPage(text, key, page - 1, cancellationToken);
                token = previous?.NextPageToken;
                if (string.IsNullOrEmpty(token))
                    return new GoogleTextSearchResponse { Results = new List<GoogleTextSearchResult>() };
            }

            // Later pages must repeat the original parameters (query and
            // language) alongside the token, or Google answers INVALID_REQUEST.
            var url = $"{_settings.BaseUrl}/textsearch/json?query={Uri.EscapeDataString(text)}&language=en&key={_settings.ApiKey}";
            if (page > 0)
                url += $"&pagetoken={Uri.EscapeDataString(token)}";

            // A fresh token is also not valid for a moment (INVALID_REQUEST);
            // wait and retry a few times.
            for (var attempt = 0; ; attempt++)
            {
                var httpResponse = await _httpClient.GetAsync(url, cancellationToken);
                httpResponse.EnsureSuccessStatusCode();
                var content = await httpResponse.Content.ReadFromJsonAsync<GoogleTextSearchResponse>(cancellationToken: cancellationToken);
                if (page > 0 && content?.Status == "INVALID_REQUEST" && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
                    continue;
                }
                // Anything but a real answer is an error: never cache it as "no places".
                if (content?.Status != "OK" && content?.Status != "ZERO_RESULTS")
                    throw new HttpRequestException($"Google Text Search failed: {content?.Status ?? "no response"}");
                if (!string.IsNullOrEmpty(content.NextPageToken))
                    _cache.Set($"{key}_t{page + 1}", content.NextPageToken, PageTokenLifetime);
                return content;
            }
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
                Photos = result.Photos?.Take(10).Select(p => new PhotoDto
                {
                    Url = PhotoLink(p.PhotoReference)
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
