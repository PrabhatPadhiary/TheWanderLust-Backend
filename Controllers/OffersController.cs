#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TheWanderLustWebAPI.Models.Dtos;
using TheWanderLustWebAPI.Services;
using TheWanderLustWebAPI.Settings;

namespace TheWanderLustWebAPI.Controllers
{
    /// <summary>
    /// Bookable offers from partners (affiliate): users book on the partner's
    /// site. Open to signed-out users like the rest of Explore. 200 with an
    /// empty list means "no tours here"; 503 means the partner is unreachable.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting(RateLimits.Partners)]
    public class OffersController : ControllerBase
    {
        private readonly ViatorService _viator;

        public OffersController(ViatorService viator)
        {
            _viator = viator;
        }

        /// <summary>Top tours and tickets for a city or region.</summary>
        [HttpGet("activities/destination")]
        public async Task<ActionResult<ActivityOffersDto>> ForDestination(
            [FromQuery] string? name, [FromQuery] double lat, [FromQuery] double lng,
            [FromQuery] string? theme = null, CancellationToken ct = default)
        {
            if (!ValidCoords(lat, lng)) return BadRequest("lat and lng are required.");
            // theme=food: food tours, cooking classes and tastings only.
            return Reply(await _viator.ForDestinationAsync(name, lat, lng, ct, theme == "food" ? "food" : null));
        }

        /// <summary>
        /// Tours and tickets for one place. exact=true returns only tours that
        /// match the place itself (no area fallback).
        /// </summary>
        [HttpGet("activities/place")]
        public async Task<ActionResult<ActivityOffersDto>> ForPlace(
            [FromQuery] string name, [FromQuery] double lat, [FromQuery] double lng,
            [FromQuery] bool exact = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required.");
            if (name.Length > 120) name = name[..120];
            if (!ValidCoords(lat, lng)) return BadRequest("lat and lng are required.");
            return Reply(await _viator.ForPlaceAsync(name, lat, lng, exact, ct));
        }

        /// <summary>One tour's details (photos, what's included, stops, meeting point, policies).</summary>
        [HttpGet("activities/{code}")]
        public async Task<ActionResult<ActivityDetailDto>> Detail(string code, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 30 || !code.All(ch => char.IsAsciiLetterOrDigit(ch)))
                return BadRequest("Invalid tour code.");
            var (detail, failed) = await _viator.DetailAsync(code, ct);
            if (failed) return StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (detail == null) return NotFound();
            return Ok(detail);
        }

        // 503 when Viator couldn't be reached, so the app tries again next
        // time instead of remembering "no tours here".
        private ActionResult<ActivityOffersDto> Reply(ActivityOffersDto result) =>
            result.Failed ? StatusCode(StatusCodes.Status503ServiceUnavailable, result) : Ok(result);

        private static bool ValidCoords(double lat, double lng) =>
            lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0);
    }
}
