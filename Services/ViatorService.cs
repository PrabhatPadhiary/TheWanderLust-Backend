#nullable enable
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>
    /// Tours and tickets from the Viator Partner API (affiliate, Basic access).
    /// Users book on Viator through the tracked productUrl; nothing about the
    /// user is sent, only place names and coordinates.
    ///
    /// A place is matched to one of Viator's destinations (its list is cached
    /// for a day): by name when one is close enough, otherwise the nearest one.
    /// Results are cached for a few hours. Any failure returns no results, so
    /// the app just hides the row.
    /// </summary>
    public partial class ViatorService
    {
        private readonly HttpClient _http;
        private readonly ViatorSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ViatorService> _logger;

        private static readonly TimeSpan DestinationsTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan ResultsTtl = TimeSpan.FromHours(4);
        // After a failed call, don't retry for this long (keeps a Viator outage
        // from making every screen wait on a timeout), but recover quickly.
        private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(1);
        // Searches are small; the destinations list is ~1 MB.
        private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DestinationsTimeout = TimeSpan.FromSeconds(45);
        private static readonly SemaphoreSlim DestinationsLock = new(1, 1);

        // Viator's destinations, kept for the life of the server (not in the
        // memory cache): only a full, non-empty list replaces it, so a failed
        // or odd refresh keeps the last good one instead of breaking matching.
        private static List<ViatorDestination>? _destinations;
        private static DateTime _destinationsAt = DateTime.MinValue;
        private static DateTime _destinationsRetryAt = DateTime.MinValue;

        private const int MaxResults = 10;
        // Fetched before filtering (some are dropped as out-of-area or poorly rated).
        private const int FetchCount = 20;
        // A same-name destination farther than this is a different place.
        private const double NameMatchMaxKm = 200;
        // Nearest destination when no name matches.
        private const double NearestMaxKm = 120;

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        public ViatorService(HttpClient http, IOptions<ViatorSettings> options, IMemoryCache cache, ILogger<ViatorService> logger)
        {
            _http = http;
            _settings = options.Value;
            _cache = cache;
            _logger = logger;
            _http.Timeout = DestinationsTimeout + TimeSpan.FromSeconds(5); // per-call limits below
        }

        private bool Enabled => !string.IsNullOrWhiteSpace(_settings.ApiKey);

        // Viator's "Food & Drink" tag: food tours, cooking classes, tastings, dinner cruises.
        private const int FoodAndDrinkTag = 21911;

        /// <summary>
        /// Top tours for a city or region (destination screen). theme "food":
        /// only food experiences (the Food tab).
        /// </summary>
        public async Task<ActivityOffersDto> ForDestinationAsync(string? name, double lat, double lng, CancellationToken ct, string? theme = null)
        {
            if (!Enabled) return ActivityOffersDto.Empty;
            if (await GetDestinationsAsync() == null) return ActivityOffersDto.Unavailable;
            var dest = await MatchDestinationAsync(name, lat, lng, ct);
            if (dest == null) return ActivityOffersDto.Empty;
            return await TopForDestinationAsync(dest, ct, theme == "food" ? FoodAndDrinkTag : null);
        }

        /// <summary>
        /// Tours for one place (a beach, a fort, a stop on a trip): tours that
        /// mention it in its area. With none, the area's top tours unless
        /// exactOnly (the itinerary chip only shows for real matches).
        /// </summary>
        public async Task<ActivityOffersDto> ForPlaceAsync(string name, double lat, double lng, bool exactOnly, CancellationToken ct)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(name)) return ActivityOffersDto.Empty;
            // A place is matched to the nearest destination, not by name: its
            // name is the place's own ("Baga Beach"), not the area's.
            if (await GetDestinationsAsync() == null) return ActivityOffersDto.Unavailable;
            var dest = await MatchDestinationAsync(null, lat, lng, ct);
            if (dest == null) return ActivityOffersDto.Empty;

            var term = name.Trim();
            var key = $"viator_place_{dest.DestinationId}_{term.ToLowerInvariant()}";
            if (!_cache.TryGetValue(key, out List<ActivityOfferDto>? items) || items == null)
            {
                var body = new
                {
                    searchTerm = term,
                    productFiltering = new { destination = dest.DestinationId.ToString() },
                    searchTypes = new[] { new { searchType = "PRODUCTS", pagination = new { start = 1, count = FetchCount } } },
                    currency = _settings.Currency,
                };
                var res = await PostAsync<FreetextResponse>("/search/freetext", body, ct);
                if (res == null) return ActivityOffersDto.Unavailable;
                items = await CleanAsync(res.Products?.Results, dest, ct);
                _cache.Set(key, items, ResultsTtl);
            }

            if (items.Count > 0)
                return new ActivityOffersDto { AreaName = dest.Name, SeeAllUrl = dest.DestinationUrl, Scope = "place", Items = items };
            return exactOnly ? ActivityOffersDto.Empty : await TopForDestinationAsync(dest, ct);
        }

        private async Task<ActivityOffersDto> TopForDestinationAsync(ViatorDestination dest, CancellationToken ct, int? tag = null)
        {
            var key = $"viator_top_{dest.DestinationId}_{tag}";
            if (!_cache.TryGetValue(key, out List<ActivityOfferDto>? items) || items == null)
            {
                var body = new
                {
                    filtering = tag is int t
                        ? (object)new { destination = dest.DestinationId.ToString(), tags = new[] { t } }
                        : new { destination = dest.DestinationId.ToString() },
                    // Viator's own ranking (popularity and quality).
                    sorting = new { sort = "DEFAULT" },
                    pagination = new { start = 1, count = FetchCount },
                    currency = _settings.Currency,
                };
                var res = await PostAsync<ProductSearchResponse>("/products/search", body, ct);
                if (res == null) return ActivityOffersDto.Unavailable;
                items = await CleanAsync(res.Products, dest, ct);
                _cache.Set(key, items, ResultsTtl);
            }
            return new ActivityOffersDto { AreaName = dest.Name, SeeAllUrl = dest.DestinationUrl, Items = items };
        }

        // ── Destinations ─────────────────────────────────────────────────────

        private async Task<ViatorDestination?> MatchDestinationAsync(string? name, double lat, double lng, CancellationToken ct)
        {
            var all = await GetDestinationsAsync();
            if (all == null || all.Count == 0) return null;

            var candidates = all.Where(d => d.Center != null && d.Type != "COUNTRY")
                .Select(d => (d, km: Km(lat, lng, d.Center!.Latitude, d.Center.Longitude)))
                .ToList();

            // "Goa", "Goa, India", "Kyoto Prefecture": try the full name, then the first part.
            if (!string.IsNullOrWhiteSpace(name))
            {
                var names = new[] { name.Trim(), name.Split(',')[0].Trim() }
                    .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var n in names)
                {
                    var byName = candidates
                        .Where(c => string.Equals(c.d.Name, n, StringComparison.OrdinalIgnoreCase) && c.km <= NameMatchMaxKm)
                        .OrderBy(c => c.km).FirstOrDefault();
                    if (byName.d != null) return byName.d;
                }
            }

            var nearest = candidates.Where(c => c.km <= NearestMaxKm).OrderBy(c => c.km).FirstOrDefault();
            return nearest.d;
        }

        /// <summary>
        /// Viator's destinations: the kept list, refreshed once a day. A failed
        /// refresh keeps the old list and tries again after a minute. Null only
        /// when no list has ever loaded (Viator unreachable since start-up).
        /// Not tied to the app's request: a slow first download still finishes
        /// and is kept, even if that request gave up.
        /// </summary>
        private async Task<List<ViatorDestination>?> GetDestinationsAsync()
        {
            var now = DateTime.UtcNow;
            if (_destinations != null && now - _destinationsAt < DestinationsTtl) return _destinations;
            if (now < _destinationsRetryAt) return _destinations;

            await DestinationsLock.WaitAsync();
            try
            {
                now = DateTime.UtcNow;
                if (_destinations != null && now - _destinationsAt < DestinationsTtl) return _destinations;
                if (now < _destinationsRetryAt) return _destinations;

                var res = await GetAsync<DestinationsResponse>("/destinations", DestinationsTimeout, ignoreBackoff: true);
                var list = res?.Destinations?.Where(d => d.DestinationId > 0 && d.Center != null).ToList();
                if (list != null && list.Count > 100)
                {
                    _destinations = list;
                    _destinationsAt = DateTime.UtcNow;
                    _logger.LogInformation("Viator destinations loaded: {Count}", list.Count);
                }
                else
                {
                    _destinationsRetryAt = DateTime.UtcNow + FailureTtl;
                    _logger.LogWarning("Viator destinations refresh failed ({Count} usable); keeping {Kept}",
                        list?.Count ?? 0, _destinations?.Count ?? 0);
                }
                return _destinations;
            }
            finally
            {
                DestinationsLock.Release();
            }
        }

        // ── HTTP ─────────────────────────────────────────────────────────────

        private HttpRequestMessage Request(HttpMethod method, string path)
        {
            var req = new HttpRequestMessage(method, _settings.BaseUrl.TrimEnd('/') + path);
            req.Headers.Add("exp-api-key", _settings.ApiKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json;version=2.0");
            req.Headers.Add("Accept-Language", "en-US");
            return req;
        }

        private Task<T?> GetAsync<T>(string path, TimeSpan timeout, bool ignoreBackoff = false) where T : class =>
            SendAsync<T>(Request(HttpMethod.Get, path), path, timeout, CancellationToken.None, ignoreBackoff);

        private Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
        {
            var req = Request(HttpMethod.Post, path);
            req.Content = JsonContent.Create(body);
            return SendAsync<T>(req, path, SearchTimeout, ct, false);
        }

        private const string DownKey = "viator_down";

        /// <summary>
        /// Null on any failure. 429, 5xx and timeouts pause searches for a
        /// minute (FailureTtl) so screens don't all wait on a struggling API.
        /// </summary>
        private async Task<T?> SendAsync<T>(HttpRequestMessage req, string path, TimeSpan timeout, CancellationToken ct, bool ignoreBackoff) where T : class
        {
            if (!ignoreBackoff && _cache.TryGetValue(DownKey, out _)) return null;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            try
            {
                using (req)
                using (var res = await _http.SendAsync(req, limit.Token))
                {
                    if (!res.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Viator {Path} returned {Status}", path, (int)res.StatusCode);
                        // 4xx is about this request; 429 / 5xx mean back off for a while.
                        if ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                            _cache.Set(DownKey, true, FailureTtl);
                        return null;
                    }
                    return await res.Content.ReadFromJsonAsync<T>(Json, limit.Token);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Timeouts and network errors.
                _logger.LogWarning("Viator {Path} failed: {Error}", path, ex.Message);
                _cache.Set(DownKey, true, FailureTtl);
                return null;
            }
        }

        // ── Mapping ──────────────────────────────────────────────────────────

        /// <summary>
        /// Keeps tours based in the area (Viator sometimes lists far-away ones,
        /// e.g. a Bali tour under Goa), drops poorly rated ones, and puts
        /// well-reviewed tours first (otherwise keeping Viator's order).
        /// </summary>
        private async Task<List<ActivityOfferDto>> CleanAsync(List<ViatorProduct>? products, ViatorDestination dest, CancellationToken ct)
        {
            var all = await GetDestinationsAsync() ?? new();
            var lookup = all.ToDictionary(d => d.DestinationId.ToString(), d => d.LookupId ?? "");
            var root = dest.LookupId ?? "";

            bool InArea(ViatorProduct p)
            {
                var primary = p.Destinations?.FirstOrDefault(d => d.Primary)?.Ref ?? p.Destinations?.FirstOrDefault()?.Ref;
                if (primary == null || root.Length == 0 || !lookup.TryGetValue(primary, out var path) || path.Length == 0)
                    return true; // can't tell: keep it
                // The area itself, anything inside it, or the area is inside the tour's base (a city tour of a region's city).
                return path == root || path.StartsWith(root + ".") || root.StartsWith(path + ".");
            }

            return (products ?? new())
                .Where(InArea)
                .Select(ToDto).Where(x => x != null).Select(x => x!)
                .Where(x => x.Rating == null || x.Rating >= 3.5 || x.ReviewCount < 3)
                .Where(x => !(x.Rating < 3.0))
                .Select((x, i) => (x, i))
                .OrderBy(t => t.x.ReviewCount >= 5 ? 0 : 1)
                .ThenBy(t => t.i)
                .Select(t => t.x)
                .Take(MaxResults)
                .ToList();
        }

        private ActivityOfferDto? ToDto(ViatorProduct p)
        {
            if (string.IsNullOrWhiteSpace(p.ProductCode) || string.IsNullOrWhiteSpace(p.ProductUrl) || string.IsNullOrWhiteSpace(p.Title))
                return null;
            return new ActivityOfferDto
            {
                Id = p.ProductCode,
                Title = p.Title.Trim(),
                ImageUrl = PickImage(p.Images),
                Rating = p.Reviews?.TotalReviews > 0 ? Math.Round(p.Reviews.CombinedAverageRating ?? 0, 1) : null,
                ReviewCount = p.Reviews?.TotalReviews ?? 0,
                DurationMinutes = p.Duration?.FixedDurationInMinutes ?? p.Duration?.VariableDurationFromMinutes,
                DurationToMinutes = p.Duration?.FixedDurationInMinutes == null ? p.Duration?.VariableDurationToMinutes : null,
                FromPrice = p.Pricing?.Summary?.FromPrice is decimal price ? Math.Round(price, 0) : null,
                Currency = p.Pricing?.Currency ?? _settings.Currency,
                FreeCancellation = p.Flags?.Contains("FREE_CANCELLATION") == true,
                // Kept exactly as Viator sends it: it carries our affiliate tracking.
                BookingUrl = p.ProductUrl,
            };
        }

        // The cover image's variant closest to 720 px wide (sharp on phone cards).
        private static string? PickImage(List<ViatorImage>? images)
        {
            var img = images?.FirstOrDefault(i => i.IsCover) ?? images?.FirstOrDefault();
            return img?.Variants?
                .Where(v => !string.IsNullOrEmpty(v.Url))
                .OrderBy(v => Math.Abs(v.Width - 720))
                .FirstOrDefault()?.Url;
        }

        private static double Km(double lat1, double lng1, double lat2, double lng2)
        {
            const double r = 6371;
            double dLat = (lat2 - lat1) * Math.PI / 180, dLng = (lng2 - lng1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return 2 * r * Math.Asin(Math.Sqrt(a));
        }

        // ── Viator response shapes (only the fields we use) ─────────────────

        private class DestinationsResponse { public List<ViatorDestination>? Destinations { get; set; } }

        private class ViatorDestination
        {
            public int DestinationId { get; set; }
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            /// <summary>Path in Viator's tree, e.g. "8.723.4594" (country.region.state).</summary>
            public string? LookupId { get; set; }
            public string? DestinationUrl { get; set; }
            public ViatorCenter? Center { get; set; }
        }

        private class ViatorCenter { public double Latitude { get; set; } public double Longitude { get; set; } }

        private class ProductSearchResponse { public List<ViatorProduct>? Products { get; set; } }

        private class FreetextResponse { public FreetextProducts? Products { get; set; } }
        private class FreetextProducts { public List<ViatorProduct>? Results { get; set; } }

        private class ViatorProduct
        {
            public string? ProductCode { get; set; }
            public string? Title { get; set; }
            public List<ViatorImage>? Images { get; set; }
            public ViatorReviews? Reviews { get; set; }
            public ViatorDuration? Duration { get; set; }
            public ViatorPricing? Pricing { get; set; }
            public string? ProductUrl { get; set; }
            public List<string>? Flags { get; set; }
            public List<ViatorProductDestination>? Destinations { get; set; }
        }

        private class ViatorProductDestination { public string? Ref { get; set; } public bool Primary { get; set; } }

        private class ViatorImage
        {
            public bool IsCover { get; set; }
            public List<ViatorImageVariant>? Variants { get; set; }
        }

        private class ViatorImageVariant { public int Width { get; set; } public int Height { get; set; } public string? Url { get; set; } }

        private class ViatorReviews
        {
            public int TotalReviews { get; set; }
            public double? CombinedAverageRating { get; set; }
        }

        private class ViatorDuration
        {
            public int? FixedDurationInMinutes { get; set; }
            public int? VariableDurationFromMinutes { get; set; }
            public int? VariableDurationToMinutes { get; set; }
        }

        private class ViatorPricing
        {
            public ViatorPriceSummary? Summary { get; set; }
            public string? Currency { get; set; }
        }

        private class ViatorPriceSummary { public decimal? FromPrice { get; set; } }
    }
}
