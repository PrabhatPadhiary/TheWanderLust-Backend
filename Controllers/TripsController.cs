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
                .Include(t => t.Destinations)
                    .ThenInclude(d => d.Places)
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
                t.TotalBudget,
                t.Currency,
                t.Status,
                t.CreatedAt,
                PlaceIds = t.Destinations
                    .SelectMany(d => d.Places)
                    .Select(p => p.PlaceId)
                    .ToList()
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
                    .ThenInclude(d => d.Places)
                .Include(t => t.Members)
                    .ThenInclude(m => m.User)
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
                trip.TotalBudget,
                trip.Currency,
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
                    d.EndDate,
                    Places = d.Places.Select(p => new
                    {
                        p.Id,
                        p.PlaceId,
                        p.PlaceName,
                        p.Vicinity,
                        p.Rating,
                        p.UserRatingsTotal,
                        p.PhotoUrl,
                        p.Category,
                        p.Latitude,
                        p.Longitude,
                        p.Notes,
                        p.CreatedAt
                    })
                }),
                Members = trip.Members.Select(m => new
                {
                    m.UserId,
                    Name = m.User.Name,
                    Email = m.User.Email,
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
                TotalBudget = dto.TotalBudget,
                Currency = dto.Currency,
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

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

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

            if (dto.TotalBudget.HasValue)
                trip.TotalBudget = dto.TotalBudget.Value;

            if (dto.Currency != null)
                trip.Currency = dto.Currency;

            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Status, trip.TravelersCount });
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

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

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

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner" && role != "member")
                return Forbid();

            var destination = await _dbContext.TripDestinations
                .FirstOrDefaultAsync(d => d.Id == destinationId && d.TripId == id);

            if (destination == null)
                return NotFound("Destination not found.");

            _dbContext.TripDestinations.Remove(destination);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Destination removed from trip." });
        }

        [HttpPut("{id}/budget")]
        public async Task<IActionResult> SetBudget(Guid id, [FromBody] SetTripBudgetDto dto)
        {
            if (dto.TotalBudget <= 0)
                return BadRequest("Budget must be greater than zero.");

            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            trip.TotalBudget = dto.TotalBudget;
            trip.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "₹" : dto.Currency;
            trip.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new { trip.TotalBudget, trip.Currency });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Unauthorized();

            var role = await GetMemberRole(id, userId.Value);
            if (role == null)
                return NotFound("Trip not found.");
            if (role != "owner")
                return Forbid();

            var trip = await _dbContext.Trips.FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null)
                return NotFound("Trip not found.");

            _dbContext.Trips.Remove(trip);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Trip deleted." });
        }



        private async Task<Guid?> GetCurrentUserId()
        {
            var firebaseUid = User.FindFirst("firebase_uid")?.Value;
            if (string.IsNullOrEmpty(firebaseUid))
                return null;

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.FirebaseId == firebaseUid);
            return user?.Id;
        }

        private async Task<string?> GetMemberRole(Guid tripId, Guid userId)
        {
            var member = await _dbContext.TripMembers
                .FirstOrDefaultAsync(m => m.TripId == tripId && m.UserId == userId);
            return member?.Role;
        }

        private static DateTime? ToUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
    }
}
