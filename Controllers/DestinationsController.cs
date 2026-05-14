using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DestinationsController : ControllerBase
    {
        private readonly IGooglePlacesService _placesService;
        private readonly IPexelsService _pexelsService;
        private readonly IMemoryCache _cache;

        public DestinationsController(IGooglePlacesService placesService, IPexelsService pexelsService, IMemoryCache cache)
        {
            _placesService = placesService;
            _pexelsService = pexelsService;
            _cache = cache;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string placeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");

            try
            {
                var cacheKey = $"destinations_{placeId}";
                var fromCache = _cache.TryGetValue(cacheKey, out _);

                var result = await _placesService.GetAllCategories(placeId, cancellationToken);

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

        [HttpGet("hero-image")]
        public async Task<IActionResult> HeroImage([FromQuery] string placeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");

            try
            {
                var cacheKey = $"pexels_hero_{placeId}";
                var fromCache = _cache.TryGetValue(cacheKey, out _);

                var imageUrls = await _pexelsService.GetHeroImagesAsync(placeId, cancellationToken);

                Response.Headers["X-Data-Source"] = fromCache ? "cache" : "api";

                if (imageUrls == null || imageUrls.Count == 0)
                    return NotFound("No hero images found for this destination.");

                return Ok(new { imageUrls });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, "Failed to retrieve images from Pexels API.");
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
}
