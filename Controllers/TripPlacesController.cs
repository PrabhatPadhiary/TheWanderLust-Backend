using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/trips/{tripId}/destinations/{destinationId}/places")]
    [Authorize]
    public class TripPlacesController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripPlacesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(Guid tripId, Guid destinationId)
        {
            if (!await OwnsDestinationAsync(tripId, destinationId))
                return NotFound("Destination not found.");

            var places = await _dbContext.TripPlaces
                .Where(p => p.TripDestinationId == destinationId)
                .OrderBy(p => p.Category)
                .Select(p => new
                {
                    p.Id,
                    p.PlaceId,
                    p.PlaceName,
                    p.Vicinity,
                    p.Rating,
                    p.UserRatingsTotal,
                    p.PhotoUrl,
                    p.Category,
                    p.Notes,
                    p.CreatedAt
                })
                .ToListAsync();

            return Ok(places);
        }

        [HttpPost]
        public async Task<IActionResult> Add(Guid tripId, Guid destinationId, [FromBody] CreateTripPlaceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.PlaceId))
                return BadRequest("PlaceId is required.");

            if (string.IsNullOrWhiteSpace(dto.Category))
                return BadRequest("Category is required (food | stay | activity).");

            if (!await OwnsDestinationAsync(tripId, destinationId))
                return NotFound("Destination not found.");

            var alreadyAdded = await _dbContext.TripPlaces
                .AnyAsync(p => p.TripDestinationId == destinationId && p.PlaceId == dto.PlaceId);

            if (alreadyAdded)
                return Conflict("Place already added to this destination.");

            var place = new TripPlace
            {
                Id = Guid.NewGuid(),
                TripDestinationId = destinationId,
                PlaceId = dto.PlaceId,
                PlaceName = dto.PlaceName,
                Vicinity = dto.Vicinity,
                Rating = dto.Rating,
                UserRatingsTotal = dto.UserRatingsTotal,
                PhotoUrl = dto.PhotoUrl,
                Category = dto.Category,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TripPlaces.Add(place);
            await _dbContext.SaveChangesAsync();

            return Ok(new { place.Id, place.PlaceId, place.PlaceName, place.Category });
        }

        [HttpDelete("/api/trip-places/{id}")]
        public async Task<IActionResult> Remove(Guid id)
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return Unauthorized();

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            if (user == null)
                return Unauthorized();

            var place = await _dbContext.TripPlaces
                .Include(p => p.TripDestination)
                    .ThenInclude(d => d.Trip)
                .FirstOrDefaultAsync(p => p.Id == id
                    && p.TripDestination.Trip.UserId == user.Id);

            if (place == null)
                return NotFound("Place not found.");

            _dbContext.TripPlaces.Remove(place);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Place removed from trip." });
        }

        private async Task<bool> OwnsDestinationAsync(Guid tripId, Guid destinationId)
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return false;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            if (user == null)
                return false;

            var ownsTrip = await _dbContext.Trips.AnyAsync(t => t.Id == tripId && t.UserId == user.Id);
            if (!ownsTrip)
                return false;

            return await _dbContext.TripDestinations.AnyAsync(d => d.Id == destinationId && d.TripId == tripId);
        }
    }
}
