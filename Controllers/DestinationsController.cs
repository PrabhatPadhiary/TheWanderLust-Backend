using Microsoft.AspNetCore.Mvc;
using TheWanderLustWebAPI.Services;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DestinationsController : ControllerBase
    {
        private readonly IGooglePlacesService _placesService;

        public DestinationsController(IGooglePlacesService placesService)
        {
            _placesService = placesService;
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string placeId)
        {
            if (string.IsNullOrWhiteSpace(placeId))
                return BadRequest("placeId query parameter is required.");

            try
            {
                var result = await _placesService.GetAllCategories(placeId);
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
