#nullable enable
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Services
{
    /// <summary>A place photo ready to send to the client.</summary>
    public sealed record PlacePhoto(byte[] Bytes, string ContentType);

    /// <summary>
    /// Turns a Google place id straight into its main photo, so clients only
    /// need one image URL per place (no details lookup first).
    ///
    /// Two caches, both in this service's own size-limited MemoryCache so they
    /// can't grow without bound or interfere with the app's shared cache:
    ///  - place id -> photo reference (tiny; "" marks a place with no photo)
    ///  - place id + width -> image bytes
    /// Photo references expire on Google's side, so a failed image fetch with a
    /// cached reference refreshes the reference once and retries.
    /// </summary>
    public class PlacePhotoService : IDisposable
    {
        // Short durations: Google's terms restrict how long Places content may
        // be stored. Review these against the current Maps Platform terms.
        private static readonly TimeSpan ReferenceTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan ImageTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan NoPhotoTtl = TimeSpan.FromHours(6);

        // Total budget for this cache, in bytes (image size + a small fixed
        // cost per reference entry).
        private const long CacheSizeLimitBytes = 100L * 1024 * 1024;
        private const long ReferenceEntrySize = 256;

        public const int MinWidth = 100;
        public const int MaxWidth = 1600;

        private readonly HttpClient _httpClient;
        private readonly GooglePlacesSettings _settings;
        private readonly ILogger<PlacePhotoService> _logger;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimitBytes });

        public PlacePhotoService(
            IHttpClientFactory httpClientFactory,
            IOptions<GooglePlacesSettings> settings,
            ILogger<PlacePhotoService> logger)
        {
            _httpClient = httpClientFactory.CreateClient(nameof(PlacePhotoService));
            _settings = settings.Value;
            _logger = logger;
        }

        /// <summary>Snap a requested width to a few buckets so the cache stays small.</summary>
        public static int NormalizeWidth(int requested)
        {
            var clamped = Math.Clamp(requested, MinWidth, MaxWidth);
            int[] buckets = { 200, 400, 800, 1200, 1600 };
            foreach (var bucket in buckets)
            {
                if (clamped <= bucket) return bucket;
            }
            return MaxWidth;
        }

        /// <summary>
        /// Returns the place's main photo, or null when the place has no photo.
        /// Throws HttpRequestException when Google can't be reached.
        /// </summary>
        public async Task<PlacePhoto?> GetPhotoAsync(string placeId, int maxWidth, CancellationToken cancellationToken)
        {
            var width = NormalizeWidth(maxWidth);
            var imageKey = $"img:{placeId}:{width}";
            if (_cache.TryGetValue(imageKey, out PlacePhoto? cached) && cached != null)
                return cached;

            var reference = await GetReferenceAsync(placeId, forceRefresh: false, cancellationToken);
            if (string.IsNullOrEmpty(reference))
                return null;

            var photo = await TryDownloadAsync(reference, width, cancellationToken);
            if (photo == null)
            {
                // The cached reference may have expired. Refresh it once.
                reference = await GetReferenceAsync(placeId, forceRefresh: true, cancellationToken);
                if (string.IsNullOrEmpty(reference))
                    return null;
                photo = await TryDownloadAsync(reference, width, cancellationToken);
                if (photo == null)
                    throw new HttpRequestException($"Google photo request failed for place {placeId}.");
            }

            _cache.Set(imageKey, photo, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ImageTtl,
                Size = photo.Bytes.LongLength,
            });
            return photo;
        }

        private async Task<string?> GetReferenceAsync(string placeId, bool forceRefresh, CancellationToken cancellationToken)
        {
            var refKey = $"ref:{placeId}";
            if (!forceRefresh && _cache.TryGetValue(refKey, out string? cachedRef) && cachedRef != null)
                return cachedRef;

            // Only the photos field: far lighter than the full details request.
            var url = $"{_settings.BaseUrl}/details/json?place_id={Uri.EscapeDataString(placeId)}&fields=photos&key={_settings.ApiKey}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<GooglePlaceDetailsResponse>(cancellationToken: cancellationToken);
            var reference = content?.Result?.Photos?.FirstOrDefault()?.PhotoReference ?? string.Empty;

            if (content?.Status is not ("OK" or "ZERO_RESULTS" or "NOT_FOUND" or "INVALID_REQUEST"))
            {
                // Quota, key or server problems: report a failure rather than
                // "no photo", and don't cache anything.
                _logger.LogWarning("Place details for {PlaceId} returned status {Status}", placeId, content?.Status);
                throw new HttpRequestException($"Place details returned {content?.Status ?? "no response"}.");
            }

            _cache.Set(refKey, reference, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = reference.Length > 0 ? ReferenceTtl : NoPhotoTtl,
                Size = ReferenceEntrySize,
            });
            return reference;
        }

        private async Task<PlacePhoto?> TryDownloadAsync(string reference, int width, CancellationToken cancellationToken)
        {
            var url = $"{_settings.BaseUrl}/photo?maxwidth={width}&photo_reference={Uri.EscapeDataString(reference)}&key={_settings.ApiKey}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Google photo returned {Status}", (int)response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            // Google answers expired references with an error page, not an image.
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length == 0 ? null : new PlacePhoto(bytes, contentType);
        }

        public void Dispose() => _cache.Dispose();
    }
}
