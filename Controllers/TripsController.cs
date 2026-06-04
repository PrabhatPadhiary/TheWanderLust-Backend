using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Context;
using TheWanderLustWebAPI.Models;
using TheWanderLustWebAPI.Models.Dtos;

namespace TheWanderLustWebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TripsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public TripsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trips = await _dbContext.Trips
                .Where(t => t.UserId == userId.Value
                    || t.Members.Any(m => m.UserId == userId.Value))
                .OrderByDescending(t => t.StartDate)
                .ToListAsync();

            var result = trips.Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.StartDate,
                t.EndDate,
                t.CoverPhotoUrl,
                t.TravelersCount,
                t.PrimaryDestination,
                t.Status,
                t.CreatedAt
            });

            return Ok(result);
        }

        [HttpGet("by-destination/{googlePlaceId}")]
        public async Task<IActionResult> GetByDestination(string googlePlaceId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trips = await _dbContext.Trips
                .Include(t => t.Destinations)
                .Where(t => t.UserId == userId.Value
                    && t.Destinations.Any(d => d.GooglePlaceId == googlePlaceId))
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            var trip = trips.FirstOrDefault(t => t.Status != "completed");

            if (trip == null)
                return NotFound();

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.StartDate,
                trip.EndDate,
                trip.Status,
                trip.PrimaryDestination,
                trip.TravelersCount
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == id
                    && (t.UserId == userId.Value || t.Members.Any(m => m.UserId == userId.Value)));

            if (trip == null)
                return NotFound("Trip not found.");

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.Description,
                trip.StartDate,
                trip.EndDate,
                trip.CoverPhotoUrl,
                trip.TravelersCount,
                trip.PrimaryDestination,
                trip.Status,
                trip.CreatedAt,
                Destinations = trip.Destinations.Select(d => new
                {
                    d.Id,
                    d.GooglePlaceId,
                    d.Name,
                    d.Latitude,
                    d.Longitude,
                    d.PhotoUrl,
                    d.Order,
                    d.StartDate,
                    d.EndDate
                }),
                Members = trip.Members.Select(m => new
                {
                    m.UserId,
                    m.Role,
                    m.JoinedAt
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTripDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Name))
                return BadRequest("Trip name is required.");

            if (string.IsNullOrWhiteSpace(dto.Destination?.Name))
                return BadRequest("Destination name is required.");

            if (string.IsNullOrWhiteSpace(dto.Destination?.GooglePlaceId))
                return BadRequest("Destination GooglePlaceId is required.");

            if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate < dto.StartDate)
                return BadRequest("End date must be after start date.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = new Trip
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Name = dto.Name,
                Description = dto.Description,
                StartDate = ToUtc(dto.StartDate),
                EndDate = ToUtc(dto.EndDate),
                CoverPhotoUrl = dto.CoverPhotoUrl,
                TravelersCount = dto.TravelersCount,
                PrimaryDestination = dto.PrimaryDestination,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Trips.Add(trip);

            // Auto-add creator as trip owner
            var tripMember = new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                UserId = userId.Value,
                Role = "owner",
                JoinedAt = DateTime.UtcNow
            };
            _dbContext.TripMembers.Add(tripMember);

            TripDestination? destination = null;
            if (!string.IsNullOrWhiteSpace(dto.Destination.Name))
            {
                destination = new TripDestination
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    GooglePlaceId = dto.Destination.GooglePlaceId,
                    Name = dto.Destination.Name,
                    Latitude = dto.Destination.Latitude,
                    Longitude = dto.Destination.Longitude,
                    PhotoUrl = dto.Destination.PhotoUrl,
                    Order = dto.Destination.Order,
                    StartDate = ToUtc(dto.Destination.StartDate),
                    EndDate = ToUtc(dto.Destination.EndDate),
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.TripDestinations.Add(destination);
            }

            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = trip.Id }, new
            {
                trip.Id,
                trip.Name,
                trip.StartDate,
                trip.EndDate,
                trip.Status,
                Destination = destination == null ? null : new
                {
                    destination.Id,
                    destination.GooglePlaceId,
                    destination.Name,
                    destination.Latitude,
                    destination.Longitude
                }
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTripDto dto)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value);

            if (trip == null)
                return NotFound("Trip not found.");

            if (!string.IsNullOrWhiteSpace(dto.Name))
                trip.Name = dto.Name;

            if (dto.Description != null)
                trip.Description = dto.Description;

            if (dto.StartDate.HasValue)
                trip.StartDate = ToUtc(dto.StartDate);

            if (dto.EndDate.HasValue)
                trip.EndDate = ToUtc(dto.EndDate);

            if (trip.EndDate.HasValue && trip.StartDate.HasValue && trip.EndDate < trip.StartDate)
                return BadRequest("End date must be after start date.");

            if (dto.CoverPhotoUrl != null)
                trip.CoverPhotoUrl = dto.CoverPhotoUrl;

            if (dto.TravelersCount.HasValue)
                trip.TravelersCount = dto.TravelersCount.Value;

            if (dto.PrimaryDestination != null)
                trip.PrimaryDestination = dto.PrimaryDestination;

            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Status });
        }

        [HttpPost("{id}/destinations")]
        public async Task<IActionResult> AddDestination(Guid id, [FromBody] CreateTripDestinationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Name))
                return BadRequest("Destination name is required.");

            if (string.IsNullOrWhiteSpace(dto.GooglePlaceId))
                return BadRequest("GooglePlaceId is required.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value);

            if (trip == null)
                return NotFound("Trip not found.");

            var alreadyExists = await _dbContext.TripDestinations
                .AnyAsync(d => d.TripId == id && d.GooglePlaceId == dto.GooglePlaceId);

            if (alreadyExists)
                return Conflict("This destination is already added to the trip.");

            var destination = new TripDestination
            {
                Id = Guid.NewGuid(),
                TripId = id,
                GooglePlaceId = dto.GooglePlaceId,
                Name = dto.Name,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                PhotoUrl = dto.PhotoUrl,
                Order = dto.Order,
                StartDate = ToUtc(dto.StartDate),
                EndDate = ToUtc(dto.EndDate),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TripDestinations.Add(destination);
            await _dbContext.SaveChangesAsync();

            return Ok(new { destination.Id, destination.GooglePlaceId, destination.Name, destination.Latitude, destination.Longitude });
        }

        [HttpDelete("{id}/destinations/{destinationId}")]
        public async Task<IActionResult> DeleteDestination(Guid id, Guid destinationId)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value);

            if (trip == null)
                return NotFound("Trip not found.");

            var destination = await _dbContext.TripDestinations
                .FirstOrDefaultAsync(d => d.Id == destinationId && d.TripId == id);

            if (destination == null)
                return NotFound("Destination not found.");

            _dbContext.TripDestinations.Remove(destination);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Destination removed from trip." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId.Value);

            if (trip == null)
                return NotFound("Trip not found.");

            _dbContext.Trips.Remove(trip);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Trip deleted." });
        }

        [HttpPost("{id}/join")]
        public async Task<IActionResult> Join(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var trip = await _dbContext.Trips
                .Include(t => t.Destinations)
                .Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            var alreadyMember = trip.Members.Any(m => m.UserId == userId.Value);
            if (alreadyMember)
                return Conflict("You are already a member of this trip.");

            var member = new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = id,
                UserId = userId.Value,
                Role = "member",
                JoinedAt = DateTime.UtcNow
            };

            _dbContext.TripMembers.Add(member);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                trip.Id,
                trip.Name,
                trip.Description,
                trip.StartDate,
                trip.EndDate,
                trip.CoverPhotoUrl,
                trip.TravelersCount,
                trip.PrimaryDestination,
                trip.Status,
                trip.CreatedAt,
                Destinations = trip.Destinations.Select(d => new
                {
                    d.Id,
                    d.GooglePlaceId,
                    d.Name,
                    d.Latitude,
                    d.Longitude,
                    d.PhotoUrl,
                    d.Order,
                    d.StartDate,
                    d.EndDate
                })
            });
        }

        private async Task<Guid?> GetCurrentUserId()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return user?.Id;
        }

        private static DateTime? ToUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
    }
}
